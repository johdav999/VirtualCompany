using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed partial class SalesRoomAgentWorker
{
    // Addressing/speech detection does not establish that the captured words contain a question.
    // In particular, never invent a product topic for a backchannel or a clipped transcript.
    private async Task<bool> IsSubstantiveQuestionAsync(SalesBrowserRoom room, Guid participantId,
        string text, SalesRoomAgentWorkItem work, CancellationToken ct, bool completenessConfirmed = false)
    {
        if (room.AgentId is not Guid agentId || room.MeetingSessionId is not Guid sessionId) return false;
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
        if (floor is null) return false;
        var context = new SalesRoomConversationContext(room.CompanyId, agentId, room.Id, sessionId,
            participantId, Guid.NewGuid(), "", "", false, floor.SlideNumber, floor.TalkingPointIndex, floor.ControlMode);
        if (!completenessConfirmed && !await RunWithLeaseRenewalAsync(work,
                token => conversationReasoner.IsCompleteAsync(context, text, token), ct)) return false;
        var intent = await RunWithLeaseRenewalAsync(work,
            token => conversationReasoner.InterpretAsync(context, text, token), ct);
        return intent == AgentConversationIntent.Question;
    }

    // Social turns use the bounded, independently validated bridge lane. They neither
    // create factual questions nor grant permission to continue the presentation.
    internal async Task<SalesRoomConversationToolResult> AcknowledgeConversationAsync(
        SalesRoomConversationTurn turn, string heard, SalesRoomAgentWorkItem work, CancellationToken ct)
    {
        var b = turn.Binding;
        if (turn.Intent != AgentConversationIntent.Acknowledgement || string.IsNullOrWhiteSpace(heard) ||
            heard.Length > 2000 || turn.ExpiresUtc <= Now || work.CompanyId != b.CompanyId || work.RoomId != b.ConversationId ||
            work.Generation != b.OwnerGeneration || work.LeaseOwnerId != b.OwnerId)
            return new(false, "conversation_stale", "The conversation changed before the reply.");

        db.ChangeTracker.Clear();
        var room = await RoomAsync(work, ct);
        var played = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.Id == turn.PlayedSpeechId &&
            x.SessionId == b.SessionId && x.AgentId == b.AgentId && x.AgentGeneration == b.OwnerGeneration &&
            x.TurnGeneration == b.TurnGeneration && x.ResponseGeneration == b.ResponseGeneration &&
            (x.Kind == SalesRoomAgentSpeechKinds.Answer || x.Kind == SalesRoomAgentSpeechKinds.Bridge) &&
            x.Status == SalesRoomAgentSpeechStates.Spoken, ct);
        if (played?.CompletedUtc is not DateTime finished || finished.AddSeconds(45) <= Now)
            return new(false, "conversation_stale", "The reply no longer belongs to the current conversation.");
        var initialAuthority = await SalesRoomConversationPolicy.LoadAsync(db, Options, b.CompanyId,
            b.ConversationId, b.ParticipantId, Now, ct);
        if (initialAuthority is null || !new AgentConversation(b, AgentConversationPhase.WaitingForReply)
                .Authorize(initialAuthority, AgentConversationAction.SpeakBridge, Now, bridgeValidated: true).Allowed)
            return new(false, "conversation_stale", "Conversation replies are unavailable in the current room state.");
        var answer = await ReleasedQuestionAsync(room, played, ct);
        var answerVersion = answer.ConcurrencyVersion;
        var context = new SalesRoomConversationContext(b.CompanyId, b.AgentId, b.ConversationId, b.SessionId,
            b.ParticipantId, answer.Id, answer.QuestionText, answer.AnswerText!,
            answer.Status == VirtualCompany.Domain.Enums.SalesMeetingQuestionStatus.PartiallySupported,
            b.Slide, b.Point, b.Mode, played.Kind == SalesRoomAgentSpeechKinds.Bridge ? played.ReleasedText : null, heard);
        var proposal = await RunWithLeaseRenewalAsync(work,
            token => conversationReasoner.ProposeBridgeAsync(context, token), ct);
        if (proposal is null || !ConversationBridgePolicy.IsStructurallySafe(proposal.Text))
            return new(true, "waiting", "The presentation remains paused while listening.");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AnyAsync(x =>
                    x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.CommandId == turn.CommandId, ct))
                return new SalesRoomConversationToolResult(true, "reply_queued", "The conversational reply was already queued.");
            var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, b.CompanyId,
                b.ConversationId, b.ParticipantId, Now, ct);
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId, ct);
            if (authority is null || !new AgentConversation(b, AgentConversationPhase.WaitingForReply)
                    .Authorize(authority, AgentConversationAction.SpeakBridge, Now, bridgeValidated: true).Allowed ||
                turn.ExpiresUtc <= Now || finished.AddSeconds(45) <= Now ||
                floor is not { State: SalesRoomFloorStates.Host, PendingTurnId: null, Overlap: false } ||
                floor.Version != turn.FloorVersion)
                return new SalesRoomConversationToolResult(false, "conversation_stale", "The floor changed before the reply.");
            room = await RoomAsync(work, ct);
            var currentAnswer = await ReleasedQuestionAsync(room, played, ct);
            if (currentAnswer.ConcurrencyVersion != answerVersion ||
                await db.SalesMeetingQuestions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == b.CompanyId &&
                    x.SessionId == b.SessionId && x.Sequence > currentAnswer.Sequence, ct) ||
                await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == b.CompanyId &&
                    x.RoomId == b.ConversationId && (x.Status == SalesRoomAgentSpeechStates.Queued ||
                    x.Status == SalesRoomAgentSpeechStates.Processing || x.CompletedUtc > finished), ct))
                return new SalesRoomConversationToolResult(false, "conversation_stale", "A newer turn superseded this reply.");
            floor.AgentClaim(b.ResponseGeneration, b.TurnGeneration, Now);
            var reply = new SalesRoomAgentSpeech(Guid.NewGuid(), b.CompanyId, b.ConversationId, b.SessionId,
                turn.CommandId, b.AgentId, b.OwnerGeneration, b.TurnGeneration, SalesRoomAgentSpeechKinds.Bridge,
                room.OrganizerUserId, Now, questionId: answer.Id, responseGeneration: b.ResponseGeneration);
            reply.PrepareBridge(proposal.Text, JsonSerializer.Serialize(new BridgeEvidence(proposal.ProposalRunId,
                proposal.ValidationRunId, answerVersion, HashAnswer(answer.AnswerText!), turn.HeardTurnId)), Now);
            db.SalesRoomAgentSpeech.Add(reply);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new SalesRoomConversationToolResult(true, "reply_queued", "A brief conversational reply is queued.");
        });
    }
}
