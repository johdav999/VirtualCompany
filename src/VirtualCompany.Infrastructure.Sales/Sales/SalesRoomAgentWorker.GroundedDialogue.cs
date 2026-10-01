using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed partial class SalesRoomAgentWorker
{
    // Long model/retrieval work must not own the listening loop's DbContext or block
    // its bounded microphone channel. Publication still goes through HandleTranscriptAsync.
    private async Task GroundedQuestionInOwnScopeAsync(UtteranceContext input, string text, bool retained,
        bool duringPresentation, string providerSession, ISalesRoomMediaConnection media,
        SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, CancellationToken ct)
    {
        await using var scope = captureScopes.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<SalesRoomAgentWorker>();
        await worker.GroundedQuestionAsync(input, text, retained, duringPresentation, providerSession, media, work, control, ct);
    }

    private async Task GroundedQuestionAsync(UtteranceContext input, string text, bool retained,
        bool duringPresentation, string providerSession, ISalesRoomMediaConnection media,
        SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, CancellationToken ct)
    {
        using var company = executionScopes.BeginScope(work.CompanyId);
        var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, work.CompanyId,
            work.RoomId, input.Value.ParticipantId, Now, ct);
        if (authority is null || authority.Binding.Mode != "autonomous" || !retained) return;
        var turn = new SalesRoomDialogueTurn(authority.Binding, StableTurnId(work.RoomId, input.Value.ParticipantId,
            input.Value.TrackId, input.Value.TrackGeneration, input.Value.StartedAt), input.ConsentVersion, Now.AddSeconds(15));

        // Start evidence preparation concurrently; no result is released until the
        // acknowledgement lane finishes and the normal authority/release checks pass.
        var preparation = PrepareGroundedAnswerInOwnScopeAsync(turn, text, work, ct);
        try
        {
            using var acknowledgementTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            acknowledgementTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            await SpeakDialogueInOwnScopeAsync(turn, text, SalesRoomDialoguePolicy.Checking,
                providerSession, media, work, control, control.InterruptionVersion, acknowledgementTimeout.Token);
            db.ChangeTracker.Clear();
            var room = await RoomAsync(work, ct);
            var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == input.Value.ParticipantId, ct);
            await HandleTranscriptAsync(room, participant, input.Value, text, duringPresentation, retained,
                media, work, floorEvents, questions, ct, semanticQuestion: true, preparedAnswer: preparation);
        }
        finally
        {
            // Never dispose a preparation scope while its database/provider work is running.
            await AwaitQuietly(preparation);
        }
    }

    private async Task<SalesMeetingQuestionDto?> PrepareGroundedAnswerInOwnScopeAsync(SalesRoomDialogueTurn turn,
        string text, SalesRoomAgentWorkItem work, CancellationToken ct)
    {
        await using var scope = captureScopes.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<SalesRoomAgentWorker>();
        return await worker.PrepareGroundedAnswerAsync(turn, text, work, ct);
    }

    private async Task<SalesMeetingQuestionDto?> PrepareGroundedAnswerAsync(SalesRoomDialogueTurn turn,
        string text, SalesRoomAgentWorkItem work, CancellationToken ct)
    {
        using var company = executionScopes.BeginScope(work.CompanyId);
        var room = await RoomAsync(work, ct);
        await EnsureConsentAsync(room, ct);
        var authority = await SalesRoomConversationPolicy.BeginInputAsync(db, Options,
            work.CompanyId, work.RoomId, turn.Binding.ParticipantId, turn.InputId, Now, ct);
        if (authority is null) return null;
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == turn.Binding.ParticipantId, ct);
        if (!participant.TranscriptRetentionAllowed || participant.Version != turn.ConsentVersion) return null;
        var sequence = (await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == work.CompanyId && x.SessionId == turn.Binding.SessionId)
            .MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("MeetingTrace Stage=source_lookup_start RoomId={RoomId} TurnId={TurnId} AgentId={AgentId} CharacterCount={CharacterCount}",
            work.RoomId, turn.InputId, turn.Binding.AgentId, text.Length);
        var result = await RunWithLeaseRenewalAsync(work, token => questions.AskAsync(work.CompanyId,
            room.OrganizerUserId, turn.Binding.SessionId,
            new(turn.InputId, sequence, turn.Binding.AgentId, text.Trim(), "customer", participant.DisplayName, "browser_room"),
            turn.InputId.ToString("N"), token), ct);
        logger.LogInformation("MeetingTrace Stage=source_lookup_complete RoomId={RoomId} TurnId={TurnId} QuestionId={QuestionId} ElapsedMs={ElapsedMs}",
            work.RoomId, turn.InputId, result?.Id, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }
}
