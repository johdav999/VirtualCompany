using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesRoomAgentService
{
    internal async Task<SalesRoomConversationToolResult> ContinueConversationAsync(SalesRoomConversationTurn turn,
        CancellationToken ct)
    {
        var b = turn.Binding;
        var started = Stopwatch.GetTimestamp();
        try
        {
            // Identity comes from the server's admitted participant, never from model arguments.
            var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.Id == b.ParticipantId, ct);
            var room = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == b.CompanyId && x.Id == b.ConversationId, ct);
            if (participant?.MemberUserId is not Guid user || room is null)
                return new(false, "participant_denied", "Only the organizer or an authorized co-host can resume.");
            await ResumeCoreAsync(b.CompanyId, user, b.ConversationId,
                new(turn.CommandId, room.Version, turn.FloorVersion, b.PresentationVersion), turn, ct);
            SalesRoomBenchmarkTelemetry.RecordLatency("continuation_request_to_accepted", Stopwatch.GetElapsedTime(started));
            return new(true, "continuation_queued", "Continuation was accepted at the saved position; playback is queued.");
        }
        catch (SalesRoomAgentException ex)
        {
            SalesRoomBenchmarkTelemetry.RecordConversationFailure(ex.Code);
            await RecordConversationPauseAsync(turn, ex, ct);
            return new(false, ex.Code, ex.Message);
        }
        catch (UnauthorizedAccessException) { return new(false, "participant_denied", "The participant cannot control this presentation."); }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
        {
            // The unique operation/queue keys and concurrency tokens arbitrate simultaneous calls.
            // Do not retry with a new command ID or move the checkpoint after a conflict.
            db.ChangeTracker.Clear();
            return new(false, "conversation_changed", "Room control changed. The host can review the saved position and resume.");
        }
    }

    private async Task RecordConversationPauseAsync(SalesRoomConversationTurn turn, SalesRoomAgentException error,
        CancellationToken ct)
    {
        // Only actionable missing-resource failures may pause the unchanged turn. Never
        // overwrite a newer host command, stopped room, or successful concurrent resume.
        if (error.Code is not (SalesRoomAgentProblemCodes.FloorNotReady or SalesRoomAgentProblemCodes.ReleaseRequired)) return;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var b = turn.Binding;
            var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, b.CompanyId,
                b.ConversationId, b.ParticipantId, Now, ct);
            if (authority is null || !new AgentConversation(b, AgentConversationPhase.WaitingForReply).Check(authority, Now).Allowed)
                return;
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId, ct);
            if (floor.Version != turn.FloorVersion) return;
            var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == b.CompanyId && x.Id == b.ConversationId, ct);
            room.PauseAgent(b.OwnerId, b.OwnerGeneration, error.Code, error.Message, room.AgentVoiceHealth);
            floor.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private async Task RequireConversationTurnAsync(SalesRoomConversationTurn turn, SalesBrowserRoom room,
        SalesRoomFloor floor, CancellationToken ct)
    {
        var b = turn.Binding;
        var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, b.CompanyId, b.ConversationId,
            b.ParticipantId, Now, ct);
        var decision = authority is null ? new AgentConversationDecision(false, "conversation_unavailable") :
            new AgentConversation(b, AgentConversationPhase.WaitingForReply).Check(authority, Now);
        if (!decision.Allowed || authority?.ControllerAllowed != true || b.Mode != "autonomous" ||
            turn.Intent != AgentConversationIntent.Continue || turn.HeardTurnId == Guid.Empty || turn.ExpiresUtc <= Now ||
            room.MeetingSessionId != b.SessionId || floor.Version != turn.FloorVersion ||
            floor.State != SalesRoomFloorStates.Host || floor.Overlap || floor.PendingTurnId != null)
            throw Error("conversation_stale", "Continuation is no longer authorized. The host can review the floor and resume.");

        var played = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == b.CompanyId && x.RoomId == b.ConversationId && x.SessionId == b.SessionId &&
            x.Id == turn.PlayedSpeechId && x.AgentId == b.AgentId && x.AgentGeneration == b.OwnerGeneration &&
            x.TurnGeneration == b.TurnGeneration && x.ResponseGeneration == b.ResponseGeneration &&
            (x.Kind == SalesRoomAgentSpeechKinds.Bridge || x.Kind == SalesRoomAgentSpeechKinds.Answer) &&
            x.Status == SalesRoomAgentSpeechStates.Spoken, ct);
        if (played?.CompletedUtc is not DateTime finished || finished.AddSeconds(45) <= Now)
            throw Error("conversation_stale", "A completed answer or follow-up and a fresh continuation request are required.");
        var conversation = new AgentConversation(b, played.Kind == SalesRoomAgentSpeechKinds.Bridge
            ? AgentConversationPhase.SpeakingBridge : AgentConversationPhase.SpeakingAnswer);
        conversation.Played(played.Id, conversation.Version, authority!, finished);
        conversation.Heard(turn.HeardTurnId, conversation.Version, authority!, Now);
        var proposal = conversation.Propose(AgentConversationAction.Resume, turn.Intent,
            conversation.Version, authority!, Now);
        if (!proposal.Allowed || !conversation.Consume(conversation.Pending!.Id,
                conversation.Version, authority!, Now).Allowed)
            throw Error("conversation_stale", "A fresh, unambiguous continuation request is required.");
        var answered = await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == b.CompanyId && x.SessionId == b.SessionId && x.Id == played.QuestionId, ct);
        if (answered is null || await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == b.CompanyId &&
                x.RoomId == b.ConversationId && (x.Status == SalesRoomAgentSpeechStates.Queued ||
                x.Status == SalesRoomAgentSpeechStates.Processing || x.CompletedUtc > finished), ct) ||
            await db.SalesMeetingQuestions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == b.CompanyId &&
                x.SessionId == b.SessionId && x.Sequence > answered.Sequence, ct))
            throw Error("conversation_stale", "Wait for the current answer and follow-up to finish before continuing.");
    }
}
