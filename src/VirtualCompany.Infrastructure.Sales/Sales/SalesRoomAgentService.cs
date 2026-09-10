using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesRoomAgentService(
    VirtualCompanyDbContext db,
    ISalesRoomAgentCommandSink commands,
    IRealtimeAgentSessionGateway realtime,
    IApprovedSpeechGateway speech,
    ITeamsMeetingPresenterService presenters,
    ISalesMeetingQuestionAnsweringService questions,
    ISalesRoomFloorEventPublisher floorEvents,
    IOptionsMonitor<SalesRoomAgentOptions> configured,
    IOptionsMonitor<SalesRoomLifecycleOptions> lifecycle,
    TimeProvider clock) : ISalesRoomAgentService
{
    private SalesRoomAgentOptions Options => configured.CurrentValue;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<SalesRoomAgentStatusView> GetAsync(Guid companyId, Guid userId, Guid roomId, CancellationToken ct)
    {
        var (room, _, _) = await ControllerAsync(companyId, userId, roomId, false, ct);
        return await ViewAsync(room, ct);
    }

    public async Task<SalesRoomAgentStatusView> StartAsync(Guid companyId, Guid userId, Guid roomId,
        StartSalesRoomAgent command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId);
        RequireAgentAdmission();
        var provider = await realtime.GetHealthAsync(ct);
        var voice = await speech.GetProfileAsync(ct);
        if (!provider.Available || !voice.Available)
            throw Error(SalesRoomAgentProblemCodes.Unavailable, "Agent voice is unavailable. Continue with the human call, manual slides, or typed questions.", 503);
        SalesBrowserRoom room;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            room = await OrganizerAsync(companyId, userId, roomId, true, ct);
            var replay = await ReplayAsync(companyId, roomId, command.CommandId, "agent_start", ct);
            if (!replay)
            {
                if (room.Version != command.ExpectedVersion) throw Error(SalesRoomAgentProblemCodes.Conflict, "The room changed. Refresh before starting the agent.");
                if (room.MeetingSessionId is not Guid sessionId) throw Error(SalesRoomAgentProblemCodes.Conflict, "The room has no meeting session.");
                await EnforceStartLimitsAsync(companyId, room.Id, ct);
                TeamsPresenterRuntime runtime;
                try { runtime = await presenters.ResolveAsync(companyId, sessionId, ct); }
                catch (TeamsCallControlException ex)
                {
                    throw Error(SalesRoomAgentProblemCodes.Unavailable,
                        $"The selected presenter cannot join this room: {ex.Message}", 503);
                }
                await RequireConsentAsync(room, ct);
                var abandoned = await db.SalesRoomAgentSpeech.Where(x => x.CompanyId == companyId && x.RoomId == roomId &&
                    (x.Status == SalesRoomAgentSpeechStates.Queued || x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct);
                foreach (var speechItem in abandoned)
                    speechItem.Fail(SalesRoomAgentSpeechStates.Interrupted, "worker_replaced",
                        "A prior worker lease ended before this speech completed; it will not be replayed.", Now);
                var owner = Guid.NewGuid();
                try { room.StartAgent(runtime.AgentId, userId, owner, Now.AddSeconds(Options.LeaseSeconds), Now); }
                catch (InvalidOperationException ex) { throw Error(SalesRoomAgentProblemCodes.Conflict, ex.Message); }
                var host = await db.SalesRoomParticipants.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == companyId && x.RoomId == roomId && x.MemberUserId == userId &&
                    x.State == SalesRoomParticipantStates.Admitted, ct)
                    ?? throw Error(SalesRoomAgentProblemCodes.FloorNotReady, "Join the room before starting its agent.");
                var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(x =>
                    x.CompanyId == companyId && x.Id == sessionId, ct);
                var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == companyId && x.RoomId == roomId, ct);
                if (floor is null)
                {
                    floor = new SalesRoomFloor(companyId, roomId, host.Id, room.AgentTurnGeneration,
                        meeting.ConcurrencyVersion, Math.Max(1, meeting.CurrentSlideIndex),
                        meeting.CurrentTalkingPointIndex, meeting.ResumeMarker, meeting.PresentationControlMode, Now);
                    db.SalesRoomFloors.Add(floor);
                }
                else floor.SetMode(meeting.PresentationControlMode, Now);
                Record(room, command.CommandId, "agent_start", userId, command);
                SalesRoomBenchmarkTelemetry.RecordOwnership("acquired");
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                await commands.SignalAsync(new(companyId, roomId, owner, room.AgentGeneration, "start"), ct);
                return await ViewAsync(room, ct);
            }
            await tx.CommitAsync(ct);
        }
        return await ViewAsync(room, ct);
    }

    public async Task<SalesRoomAgentStatusView> StopAsync(Guid companyId, Guid userId, Guid roomId,
        StopSalesRoomAgent command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId);
        Guid owner = Guid.Empty; long generation = 0; SalesBrowserRoom room;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            room = await OrganizerAsync(companyId, userId, roomId, true, ct);
            var replay = await ReplayAsync(companyId, roomId, command.CommandId, "agent_stop", ct);
            if (!replay)
            {
                if (room.Version != command.ExpectedVersion) throw Error(SalesRoomAgentProblemCodes.Conflict, "The room changed. Refresh before stopping the agent.");
                owner = room.AgentLeaseOwnerId ?? Guid.Empty; generation = room.AgentGeneration;
                room.StopAgent("host_stopped", Bounded(command.Reason, 200), Now);
                var pending = await db.SalesRoomAgentSpeech.Where(x => x.CompanyId == companyId && x.RoomId == roomId &&
                    (x.Status == SalesRoomAgentSpeechStates.Queued || x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct);
                foreach (var item in pending) item.Fail(SalesRoomAgentSpeechStates.Interrupted, "host_stopped", "The host stopped AI before this speech completed.", Now);
                Record(room, command.CommandId, "agent_stop", userId, command);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            else await tx.CommitAsync(ct);
        }
        await commands.SignalAsync(new(companyId, roomId, owner, generation, "stop"), ct);
        return await ViewAsync(room, ct);
    }

    public Task<SalesRoomAgentStatusView> InvokeNarrationAsync(Guid companyId, Guid userId, Guid roomId,
        InvokeSalesRoomNarration command, CancellationToken ct) =>
        QueueSpeechAsync(companyId, userId, roomId, command.CommandId, command.ExpectedVersion,
            SalesRoomAgentSpeechKinds.Narration, command.RevisionId, command.SegmentId, null, command.OffsetMilliseconds, ct);

    public async Task<SalesRoomAgentStatusView> AskAsync(Guid companyId, Guid userId, Guid roomId,
        AskSalesRoomAgent command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId);
        if (string.IsNullOrWhiteSpace(command.Question) || command.Question.Trim().Length > 2000)
            throw Error(SalesRoomAgentProblemCodes.Conflict, "Enter a question of 2,000 characters or fewer.", 400);
        var room = await OrganizerAsync(companyId, userId, roomId, false, ct);
        if (room.Version != command.ExpectedVersion) throw Error(SalesRoomAgentProblemCodes.Conflict, "The room changed. Refresh before asking.");
        if (room.MeetingSessionId is not Guid sessionId || room.AgentId is not Guid agentId)
            throw Error(SalesRoomAgentProblemCodes.Conflict, "Start the selected room agent before asking it a question.");
        var sequence = (await db.SalesMeetingQuestions.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
            .MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
        await questions.AskAsync(companyId, userId, sessionId,
            new(command.CommandId, sequence, agentId, command.Question.Trim(), "host", "Organizer", "typed"),
            command.CommandId.ToString("N"), ct);
        db.ChangeTracker.Clear();
        room = await OrganizerAsync(companyId, userId, roomId, false, ct);
        return await ViewAsync(room, ct);
    }

    public async Task<SalesRoomAgentStatusView> SpeakAnswerAsync(Guid companyId, Guid userId, Guid roomId,
        SpeakSalesRoomAnswer command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId);
        var room = await OrganizerAsync(companyId, userId, roomId, false, ct);
        if (room.MeetingSessionId is not Guid sessionId) throw Error(SalesRoomAgentProblemCodes.Conflict, "The room has no meeting session.");
        var answer = await db.SalesMeetingQuestions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.SessionId == sessionId && x.Id == command.QuestionId, ct) ?? throw Error(SalesRoomAgentProblemCodes.ReleaseRequired, "The grounded answer was not found.", 404);
        if (answer.Visibility != SalesMeetingAnswerVisibility.ApprovedForStage)
        {
            await questions.ApproveForStageAsync(companyId, userId, sessionId, answer.Id, command.ExpectedQuestionVersion,
                command.CommandId.ToString("N"), ct);
            db.ChangeTracker.Clear();
        }
        return await QueueSpeechAsync(companyId, userId, roomId, command.CommandId, command.ExpectedVersion,
            SalesRoomAgentSpeechKinds.Answer, null, null, command.QuestionId, 0, ct);
    }

    public async Task<SalesRoomAgentStatusView> TakeOverAsync(Guid companyId, Guid userId, Guid roomId,
        TakeOverSalesRoomAgent command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId);
        var (room, participant, floor) = await ControllerAsync(companyId, userId, roomId, true, ct);
        var session = await SessionAsync(room, ct);
        var connected = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.CompanyId == companyId && x.RoomId == roomId && x.State == SalesRoomParticipantStates.Admitted && x.Connected, ct);
        var stopId = Guid.NewGuid();
        room.TakeOverAgent("The salesperson took over. Agent output and pending actions were cancelled.");
        if (session.PresentationControlMode != SalesPresentationControlModes.Manual)
            session.SetPresentationControlMode(SalesPresentationControlModes.Manual, session.ConcurrencyVersion, userId, Now);
        floor.TakeOver(participant.Id, floor.Version, stopId, connected,
            Now.AddMilliseconds(Options.PlaybackStopAcknowledgementTimeoutMilliseconds), room.AgentTurnGeneration,
            session.ConcurrencyVersion, Math.Max(1, session.CurrentSlideIndex), session.CurrentTalkingPointIndex,
            session.ResumeMarker, Now);
        var pending = await db.SalesRoomAgentSpeech.Where(x => x.CompanyId == companyId && x.RoomId == roomId &&
            (x.Status == SalesRoomAgentSpeechStates.Queued || x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct);
        foreach (var speechItem in pending) speechItem.Fail(SalesRoomAgentSpeechStates.Interrupted, "host_takeover",
            "The salesperson took over before this response completed.", Now);
        Record(room, command.CommandId, "agent_takeover", userId, command);
        await db.SaveChangesAsync(ct);
        await commands.SignalAsync(new(companyId, roomId, room.AgentLeaseOwnerId ?? Guid.Empty, room.AgentGeneration, "takeover"), ct);
        await floorEvents.RequestPlaybackStopAsync(companyId, session.Id,
            new(roomId, stopId, floor.ResponseGeneration, floor.PlaybackStopDeadlineUtc!.Value), ct);
        return await ViewAsync(room, ct);
    }

    public async Task<SalesRoomAgentStatusView> ResumeAsync(Guid companyId, Guid userId, Guid roomId,
        ResumeSalesRoomAgent command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId);
        RequireAgentAdmission();
        var (room, participant, floor) = await ControllerAsync(companyId, userId, roomId, true, ct);
        if (room.Version != command.ExpectedVersion || floor.Version != command.ExpectedFloorVersion)
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "The room floor changed. Refresh before resuming.");
        var session = await SessionAsync(room, ct);
        if (session.ConcurrencyVersion != command.ExpectedPresentationVersion)
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "The presentation changed. Refresh before resuming.");
        if (!await AudienceReadyAsync(room, session, ct))
            throw Error(SalesRoomAgentProblemCodes.FloorNotReady,
                "Automatic narration is paused until every required participant renders this slide. The host may use the existing explicit audience override.");
        if (room.AgentLeaseOwnerId is not Guid owner || room.AgentId is not Guid agentId)
            throw Error(SalesRoomAgentProblemCodes.Unavailable, "Start the room agent before resuming.");
        room.ResumeAgent(owner, room.AgentGeneration);
        try
        {
            floor.Resume(participant.Id, floor.Version, session.ConcurrencyVersion, Math.Max(1, session.CurrentSlideIndex),
                session.CurrentTalkingPointIndex, session.ResumeMarker, room.AgentTurnGeneration, Now);
        }
        catch (InvalidOperationException ex) { throw Error(SalesRoomAgentProblemCodes.FloorConflict, ex.Message); }
        var source = await CurrentNarrationAsync(room, session, ct)
            ?? throw Error(SalesRoomAgentProblemCodes.ReleaseRequired, "The current slide has no approved narration to resume.");
        var speechItem = new SalesRoomAgentSpeech(Guid.NewGuid(), companyId, roomId, session.Id, command.CommandId,
            agentId, room.AgentGeneration, room.AgentTurnGeneration, SalesRoomAgentSpeechKinds.Narration, userId, Now,
            source.RevisionId, source.SegmentId, null, floor.ResumeOffsetMilliseconds, floor.ResponseGeneration);
        db.SalesRoomAgentSpeech.Add(speechItem);
        Record(room, command.CommandId, "agent_resume", userId, command);
        await db.SaveChangesAsync(ct);
        await floorEvents.AllowPlaybackAsync(companyId, session.Id, new(roomId, floor.ResponseGeneration), ct);
        await commands.SignalAsync(new(companyId, roomId, owner, room.AgentGeneration, "wake"), ct);
        return await ViewAsync(room, ct);
    }

    public Task<SalesRoomAgentStatusView> ConfirmPendingTurnAsync(Guid companyId, Guid userId, Guid roomId,
        ConfirmSalesRoomPendingTurn command, CancellationToken ct) =>
        ResolvePendingTurnAsync(companyId, userId, roomId, command.CommandId, command.ExpectedVersion,
            command.ExpectedFloorVersion, true, ct);

    public Task<SalesRoomAgentStatusView> DismissPendingTurnAsync(Guid companyId, Guid userId, Guid roomId,
        DismissSalesRoomPendingTurn command, CancellationToken ct) =>
        ResolvePendingTurnAsync(companyId, userId, roomId, command.CommandId, command.ExpectedVersion,
            command.ExpectedFloorVersion, false, ct);

    public async Task<SalesRoomAgentStatusView> AuthorizeCoHostAsync(Guid companyId, Guid userId, Guid roomId,
        AuthorizeSalesRoomCoHost command, CancellationToken ct)
    {
        var room = await OrganizerAsync(companyId, userId, roomId, true, ct);
        var floor = await FloorAsync(room, ct);
        if (room.Version != command.ExpectedVersion || floor.Version != command.ExpectedFloorVersion)
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "The room floor changed. Refresh before assigning a co-host.");
        if (command.ParticipantId is Guid candidate && !await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.RoomId == roomId && x.Id == candidate && x.MemberUserId != null &&
                x.MemberUserId != userId && x.State == SalesRoomParticipantStates.Admitted, ct))
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "A co-host must be another admitted company member.");
        floor.AuthorizeCoHost(command.ParticipantId, Now);
        Record(room, command.CommandId, "agent_cohost", userId, command);
        await db.SaveChangesAsync(ct);
        return await ViewAsync(room, ct);
    }

    public async Task AddressAsync(SalesBrowserPresentationAccessContext access, AddressSalesRoomAgent command, CancellationToken ct)
    {
        ValidateCommand(command.CommandId);
        if (string.IsNullOrWhiteSpace(command.Question) || command.Question.Trim().Length > 2000)
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "Enter a question of 2,000 characters or fewer.", 400);
        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.CompanyId == access.CompanyId && x.RoomId == access.RoomId && x.Id == access.ParticipantId, ct);
        if (participant.Version != command.ExpectedParticipantVersion || participant.Generation != access.ParticipantGeneration ||
            participant.State != SalesRoomParticipantStates.Admitted)
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "Your room access changed. Refresh before addressing the agent.");
        var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x =>
            x.CompanyId == access.CompanyId && x.Id == access.RoomId, ct);
        await RequireConsentAsync(room, ct);
        if (room.AgentId is not Guid agentId || room.MeetingSessionId is not Guid sessionId)
            throw Error(SalesRoomAgentProblemCodes.Unavailable, "The host must start the room agent first.");
        var sequence = (await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == room.CompanyId && x.SessionId == sessionId).MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
        var answered = await questions.AskAsync(room.CompanyId, room.OrganizerUserId, sessionId,
            new(command.CommandId, sequence, agentId, command.Question.Trim(), "customer", participant.DisplayName, "typed"),
            command.CommandId.ToString("N"), ct);
        if (answered is null) throw Error(SalesRoomAgentProblemCodes.Unavailable, "The addressed question could not be processed.", 503);
        db.ChangeTracker.Clear();
        room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == access.CompanyId && x.Id == access.RoomId, ct);
        var floor = await FloorAsync(room, ct);
        if (floor.PendingQuestionId == answered.Id || await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.CommandId == command.CommandId, ct))
            return;
        floor.ProposeTurn(participant.Id, participant.Generation, true, false, answered.Id, Now);
        await db.SaveChangesAsync(ct);
        if (floor.PendingTurnState == SalesRoomPendingTurnStates.Authorized)
            await AuthorizeAndQueuePendingAsync(room, floor, room.OrganizerUserId, Guid.NewGuid(), ct);
    }

    public async Task AcknowledgePlaybackStopAsync(SalesBrowserPresentationAccessContext access,
        AcknowledgeSalesRoomPlaybackStop acknowledgement, CancellationToken ct)
    {
        if (acknowledgement.StopId == Guid.Empty || acknowledgement.ResponseGeneration < 1 ||
            string.IsNullOrWhiteSpace(acknowledgement.ConnectionId)) throw new UnauthorizedAccessException();
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == access.CompanyId && x.RoomId == access.RoomId, ct);
        if (floor?.LastPlaybackStopId != acknowledgement.StopId || floor.ResponseGeneration != acknowledgement.ResponseGeneration)
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "The playback stop request is stale.");
        var exists = await db.SalesRoomPlaybackStopAcknowledgements.IgnoreQueryFilters().AnyAsync(x =>
            x.CompanyId == access.CompanyId && x.RoomId == access.RoomId && x.StopId == acknowledgement.StopId &&
            x.ParticipantId == access.ParticipantId && x.ParticipantGeneration == access.ParticipantGeneration, ct);
        if (exists) return;
        db.SalesRoomPlaybackStopAcknowledgements.Add(new(access.CompanyId, access.RoomId, acknowledgement.StopId,
            access.ParticipantId, access.ParticipantGeneration, acknowledgement.ResponseGeneration,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(acknowledgement.ConnectionId))).ToLowerInvariant(), Now));
        floor.AcknowledgeStop(acknowledgement.StopId, Now);
        await db.SaveChangesAsync(ct);
    }

    private async Task<SalesRoomAgentStatusView> ResolvePendingTurnAsync(Guid companyId, Guid userId, Guid roomId,
        Guid commandId, long expectedRoomVersion, long expectedFloorVersion, bool confirm, CancellationToken ct)
    {
        ValidateCommand(commandId);
        var (room, participant, floor) = await ControllerAsync(companyId, userId, roomId, true, ct);
        if (room.Version != expectedRoomVersion || floor.Version != expectedFloorVersion)
            throw Error(SalesRoomAgentProblemCodes.FloorConflict, "The pending turn changed. Refresh before continuing.");
        if (!confirm)
        {
            floor.DismissPending(participant.Id, floor.Version, Now);
            Record(room, commandId, "agent_turn_dismiss", userId, new { expectedRoomVersion, expectedFloorVersion });
            await db.SaveChangesAsync(ct);
            return await ViewAsync(room, ct);
        }
        floor.ApprovePending(participant.Id, floor.Version, Now);
        Record(room, commandId, "agent_turn_confirm", userId, new { expectedRoomVersion, expectedFloorVersion });
        await AuthorizeAndQueuePendingAsync(room, floor, userId, commandId, ct);
        return await ViewAsync(room, ct);
    }

    private async Task AuthorizeAndQueuePendingAsync(SalesBrowserRoom room, SalesRoomFloor floor,
        Guid actorUserId, Guid commandId, CancellationToken ct)
    {
        RequireAgentAdmission();
        if (floor.PendingQuestionId is not Guid questionId || room.AgentId is not Guid agentId ||
            room.AgentLeaseOwnerId is not Guid owner || room.MeetingSessionId is not Guid sessionId)
            throw Error(SalesRoomAgentProblemCodes.FloorNotReady, "The addressed turn has no completed grounded answer.");
        var question = await db.SalesMeetingQuestions.IgnoreQueryFilters().Include(x => x.Evidence).SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.SessionId == sessionId && x.Id == questionId && x.AgentId == agentId, ct);
        if (question is null || question.Status != SalesMeetingQuestionStatus.Completed || question.Evidence.Count == 0)
            throw Error(SalesRoomAgentProblemCodes.ReleaseRequired, "The addressed answer is not verified from approved evidence.");
        if (question.Visibility != SalesMeetingAnswerVisibility.ApprovedForStage)
            question.ApproveForStage(actorUserId, question.ConcurrencyVersion, Now);
        room.ResumeAgent(owner, room.AgentGeneration);
        var controller = floor.PreauthorizedCoHostParticipantId is Guid cohost &&
                         await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                             x.CompanyId == room.CompanyId && x.Id == cohost && x.MemberUserId == actorUserId, ct)
            ? cohost : floor.HostParticipantId;
        floor.AuthorizeAgentResponse(controller, room.AgentTurnGeneration, Now);
        db.SalesRoomAgentSpeech.Add(new SalesRoomAgentSpeech(Guid.NewGuid(), room.CompanyId, room.Id, sessionId,
            commandId, agentId, room.AgentGeneration, room.AgentTurnGeneration, SalesRoomAgentSpeechKinds.Answer,
            actorUserId, Now, questionId: questionId, responseGeneration: floor.ResponseGeneration));
        await db.SaveChangesAsync(ct);
        await floorEvents.AllowPlaybackAsync(room.CompanyId, sessionId, new(room.Id, floor.ResponseGeneration), ct);
        await commands.SignalAsync(new(room.CompanyId, room.Id, owner, room.AgentGeneration, "wake"), ct);
    }

    private async Task<SalesRoomAgentStatusView> QueueSpeechAsync(Guid companyId, Guid userId, Guid roomId,
        Guid commandId, long expectedVersion, string kind, Guid? revision, Guid? segment, Guid? question,
        int offset, CancellationToken ct)
    {
        ValidateCommand(commandId);
        RequireAgentAdmission();
        SalesBrowserRoom room; SalesRoomAgentSpeech? item;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            room = await OrganizerAsync(companyId, userId, roomId, true, ct);
            item = await db.SalesRoomAgentSpeech.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.RoomId == roomId && x.CommandId == commandId, ct);
            if (item is null)
            {
                if (room.Version != expectedVersion) throw Error(SalesRoomAgentProblemCodes.Conflict, "The room changed. Refresh before asking the agent to speak.");
                if (!room.IsAgentOwner(room.AgentLeaseOwnerId ?? Guid.Empty, room.AgentGeneration, Now) || room.AgentId is not Guid agentId || room.MeetingSessionId is not Guid sessionId)
                    throw Error(SalesRoomAgentProblemCodes.Unavailable, "Start the room agent before asking it to speak.");
                await RequireConsentAsync(room, ct);
                if (kind == SalesRoomAgentSpeechKinds.Narration)
                {
                    if (offset < 0 || revision is null || segment is null ||
                        !await db.SalesNarrationRevisions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.SessionId == sessionId &&
                            x.Id == revision && x.ApprovedUtc != null && x.RevokedUtc == null && x.RetainUntilUtc > Now, ct) ||
                        !await db.SalesNarrationSegments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.RevisionId == revision && x.Id == segment, ct))
                        throw Error(SalesRoomAgentProblemCodes.ReleaseRequired, "Choose a current approved narration segment.");
                }
                else
                {
                    var released = await db.SalesMeetingQuestions.AsNoTracking().Include(x => x.Evidence).SingleOrDefaultAsync(x =>
                        x.CompanyId == companyId && x.SessionId == sessionId && x.Id == question && x.AgentId == agentId, ct);
                    if (released is null || released.Status != SalesMeetingQuestionStatus.Completed ||
                        released.Visibility != SalesMeetingAnswerVisibility.ApprovedForStage || released.StageApprovedUtc == null ||
                        string.IsNullOrWhiteSpace(released.AnswerText) || released.Evidence.Count == 0)
                        throw Error(SalesRoomAgentProblemCodes.ReleaseRequired, "Only a verified answer with approved evidence can be spoken.");
                }
                var floor = await FloorAsync(room, ct);
                if (room.AgentHealth == SalesRoomAgentHealthStates.Paused && room.AgentLeaseOwnerId is Guid resumeOwner)
                    room.ResumeAgent(resumeOwner, room.AgentGeneration);
                try { floor.AgentClaim(floor.ResponseGeneration, room.AgentTurnGeneration, Now); }
                catch (InvalidOperationException ex) { throw Error(SalesRoomAgentProblemCodes.FloorConflict, ex.Message); }
                item = new(Guid.NewGuid(), companyId, roomId, sessionId, commandId, agentId, room.AgentGeneration,
                    room.AgentTurnGeneration, kind, userId, Now, revision, segment, question, offset,
                    floor.ResponseGeneration);
                db.SalesRoomAgentSpeech.Add(item);
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.User, userId,
                    "sales.browser_room.agent_speech_requested", "sales_room_agent_speech", item.Id.ToString("D"),
                    AuditEventOutcomes.Pending, "The organizer requested customer-visible speech from an approved source.",
                    ["room consent", kind == SalesRoomAgentSpeechKinds.Narration ? "approved narration" : "released grounded answer"],
                    new Dictionary<string, string?> { ["roomId"] = roomId.ToString("D"), ["kind"] = kind,
                        ["questionId"] = question?.ToString("D"), ["narrationSegmentId"] = segment?.ToString("D") },
                    commandId.ToString("N"), Now));
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        if (room.MeetingSessionId is Guid speechSession)
        {
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                x.CompanyId == companyId && x.RoomId == roomId, ct);
            await floorEvents.AllowPlaybackAsync(companyId, speechSession, new(roomId, floor.ResponseGeneration), ct);
        }
        await commands.SignalAsync(new(companyId, roomId, room.AgentLeaseOwnerId ?? Guid.Empty, room.AgentGeneration, "wake"), ct);
        return await ViewAsync(room, ct);
    }

    private async Task<(SalesBrowserRoom Room, SalesRoomParticipant Participant, SalesRoomFloor Floor)> ControllerAsync(
        Guid company, Guid user, Guid roomId, bool tracked, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == company && x.UserId == user && x.Status == CompanyMembershipStatus.Active, ct))
            throw new UnauthorizedAccessException("An active company membership is required.");
        var roomQuery = db.SalesBrowserRooms.IgnoreQueryFilters(); if (!tracked) roomQuery = roomQuery.AsNoTracking();
        var room = await roomQuery.SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == roomId, ct)
            ?? throw Error(SalesRoomAgentProblemCodes.Unavailable, "The browser room was not found.", 404);
        var participantQuery = db.SalesRoomParticipants.IgnoreQueryFilters(); if (!tracked) participantQuery = participantQuery.AsNoTracking();
        var participant = await participantQuery.SingleOrDefaultAsync(x => x.CompanyId == company && x.RoomId == roomId &&
            x.MemberUserId == user && x.State == SalesRoomParticipantStates.Admitted, ct)
            ?? throw new UnauthorizedAccessException("Join the browser room before controlling its floor.");
        var floorQuery = db.SalesRoomFloors.IgnoreQueryFilters(); if (!tracked) floorQuery = floorQuery.AsNoTracking();
        var floor = await floorQuery.SingleOrDefaultAsync(x => x.CompanyId == company && x.RoomId == roomId, ct)
            ?? throw Error(SalesRoomAgentProblemCodes.FloorNotReady, "Start the room agent to initialize floor control.");
        if (participant.Id != floor.HostParticipantId && participant.Id != floor.PreauthorizedCoHostParticipantId)
            throw new UnauthorizedAccessException("Only the host or preauthorized co-host controls the room agent.");
        return (room, participant, floor);
    }

    private async Task<SalesBrowserRoom> OrganizerAsync(Guid company, Guid user, Guid room, bool tracked, CancellationToken ct)
    {
        if (company == Guid.Empty || user == Guid.Empty || room == Guid.Empty) throw new UnauthorizedAccessException();
        if (!await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == company && x.UserId == user && x.Status == CompanyMembershipStatus.Active, ct))
            throw new UnauthorizedAccessException("An active company membership is required.");
        var query = db.SalesBrowserRooms.IgnoreQueryFilters(); if (!tracked) query = query.AsNoTracking();
        var entity = await query.SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == room, ct)
            ?? throw Error(SalesRoomAgentProblemCodes.Unavailable, "The browser room was not found.", 404);
        if (entity.OrganizerUserId != user) throw new UnauthorizedAccessException("Only the organizer can control the room agent.");
        return entity;
    }

    private async Task<SalesRoomFloor> FloorAsync(SalesBrowserRoom room, CancellationToken ct) =>
        await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct)
        ?? throw Error(SalesRoomAgentProblemCodes.FloorNotReady, "The room floor is not initialized.");

    private async Task<SalesMeetingSession> SessionAsync(SalesBrowserRoom room, CancellationToken ct)
    {
        if (room.MeetingSessionId is not Guid sessionId)
            throw Error(SalesRoomAgentProblemCodes.FloorNotReady, "The room has no meeting session.");
        return await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(x =>
            x.CompanyId == room.CompanyId && x.Id == sessionId, ct);
    }

    private async Task<bool> AudienceReadyAsync(SalesBrowserRoom room, SalesMeetingSession session, CancellationToken ct)
    {
        var rows = await db.SalesRoomPresentationAudience.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
            x.PresentationVersion == session.ConcurrencyVersion).ToListAsync(ct);
        return rows.Count > 0 && rows.All(x => x.State is SalesRoomPresentationAudienceStates.Rendered or SalesRoomPresentationAudienceStates.Overridden);
    }

    private async Task<(Guid RevisionId, Guid SegmentId)?> CurrentNarrationAsync(
        SalesBrowserRoom room, SalesMeetingSession session, CancellationToken ct)
    {
        var revision = await db.SalesNarrationRevisions.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == room.CompanyId && x.SessionId == session.Id && x.ApprovedUtc != null &&
            x.RevokedUtc == null && x.RetainUntilUtc > Now).OrderByDescending(x => x.ApprovedUtc).FirstOrDefaultAsync(ct);
        if (revision is null) return null;
        var segment = await db.SalesNarrationSegments.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == room.CompanyId && x.RevisionId == revision.Id &&
            x.SlideNumber == Math.Max(1, session.CurrentSlideIndex) &&
            x.TalkingPoint >= Math.Max(1, session.CurrentTalkingPointIndex)).OrderBy(x => x.TalkingPoint).FirstOrDefaultAsync(ct);
        return segment is null ? null : (revision.Id, segment.Id);
    }

    private async Task RequireConsentAsync(SalesBrowserRoom room, CancellationToken ct)
    {
        var admitted = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
            x.RoomId == room.Id && x.State == SalesRoomParticipantStates.Admitted).ToListAsync(ct);
        if (admitted.Count == 0 || admitted.Any(x => !x.AiProcessingAllowed))
            throw Error(SalesRoomAgentProblemCodes.ConsentRequired, "Every admitted participant must consent before room AI can process audio or speak.");
    }

    private void RequireAgentAdmission()
    {
        if (!Options.Enabled)
            throw Error(SalesRoomAgentProblemCodes.Disabled,
                "Room AI is disabled. The human call, manual slides, and typed questions remain available.", 503);
        if (Options.EmergencyDisabled)
            throw Error(SalesRoomAgentProblemCodes.EmergencyDisabled,
                "Room AI is emergency-disabled. Continue with the human call, manual slides, or typed questions.", 503);
        if (Options.DrainEnabled || lifecycle.CurrentValue.DrainEnabled || !lifecycle.CurrentValue.Enabled)
            throw Error(SalesRoomAgentProblemCodes.Draining,
                "Room AI is draining for an operational change. Existing human calling and typed controls remain available.", 503);
        var problem = SalesRoomOperationsPolicy.ConfigurationProblem(Options, Now);
        if (problem is not null)
            throw Error(SalesRoomAgentProblemCodes.Unavailable,
                "Room AI cost or provider-rate controls are not current. Continue with typed questions while an operator updates readiness.", 503);
    }

    private async Task EnforceStartLimitsAsync(Guid companyId, Guid roomId, CancellationToken ct)
    {
        var activeStates = new[] { SalesRoomAgentHealthStates.Starting, SalesRoomAgentHealthStates.Ready,
            SalesRoomAgentHealthStates.Speaking, SalesRoomAgentHealthStates.Paused };
        var active = db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.Id != roomId && x.AgentLeaseOwnerId != null && x.AgentLeaseExpiresUtc > Now && activeStates.Contains(x.AgentHealth));
        if (await active.CountAsync(ct) >= Options.MaximumActiveAgentsGlobal)
        {
            SalesRoomBenchmarkTelemetry.RecordQuota("global_concurrency");
            throw Error(SalesRoomAgentProblemCodes.ConcurrencyLimit,
                "Room AI capacity is currently full. Continue with the human call and typed questions.", 429);
        }
        if (await active.CountAsync(x => x.CompanyId == companyId, ct) >= Options.MaximumActiveAgentsPerCompany)
        {
            SalesRoomBenchmarkTelemetry.RecordQuota("company_concurrency");
            throw Error(SalesRoomAgentProblemCodes.ConcurrencyLimit,
                "This company has reached its active room AI limit. Continue with the human call and typed questions.", 429);
        }
        var month = new DateTime(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usage = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.CompanyId == companyId && (x.CreatedUtc >= month || x.AgentStartedUtc >= month))
            .Select(x => new { x.AgentProviderBilledAudioMilliseconds, x.AgentInputTokens, x.AgentOutputTokens })
            .ToListAsync(ct);
        var spend = usage.Sum(x => SalesRoomOperationsPolicy.EstimatedSpend(
            x.AgentProviderBilledAudioMilliseconds, x.AgentInputTokens, x.AgentOutputTokens, Options));
        SalesRoomBenchmarkTelemetry.RecordEstimatedSpend(spend);
        if (spend >= Options.MaximumMonthlySpendPerCompanyUsd)
        {
            SalesRoomBenchmarkTelemetry.RecordQuota("company_monthly_spend");
            throw Error(SalesRoomAgentProblemCodes.SpendLimit,
                "This company's monthly room AI budget is exhausted. Continue with typed questions while an operator reviews usage.", 429);
        }
    }

    private async Task<SalesRoomAgentStatusView> ViewAsync(SalesBrowserRoom room, CancellationToken ct)
    {
        var participants = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
            x.RoomId == room.Id && x.State == SalesRoomParticipantStates.Admitted).ToListAsync(ct);
        var recent = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id)
            .OrderByDescending(x => x.CreatedUtc).Take(5).Select(x => new SalesRoomAgentSpeechView(x.Id, x.Kind, x.Status,
                x.DurationMilliseconds, x.FailureCode, x.FailureSummary, x.CreatedUtc)).ToListAsync(ct);
        SalesRoomAgentAnswerView? latest = null;
        Guid? narrationRevisionId = null, narrationSegmentId = null;
        var narrationState = "not_prepared";
        if (room.MeetingSessionId is Guid session)
        {
            var question = await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().Include(x => x.Evidence)
                .Where(x => x.CompanyId == room.CompanyId && x.SessionId == session).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
            if (question is not null) latest = new(question.Id, question.QuestionText, question.AnswerText,
                question.Status.ToStorageValue(), question.Visibility.ToStorageValue(), question.ConcurrencyVersion,
                question.Evidence.Select(x => new SalesRoomAgentEvidenceView(x.SourceId, x.SourceType, x.SourceTitle)).Distinct().ToArray());
            var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == room.CompanyId && x.Id == session, ct);
            var revision = await db.SalesNarrationRevisions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
                x.SessionId == session && x.ApprovedUtc != null && x.RevokedUtc == null && x.RetainUntilUtc > Now)
                .OrderByDescending(x => x.ApprovedUtc).FirstOrDefaultAsync(ct);
            if (revision is not null)
            {
                var segment = await db.SalesNarrationSegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
                    x.RevisionId == revision.Id && x.SlideNumber == meeting.CurrentSlideIndex)
                    .OrderBy(x => x.TalkingPoint).FirstOrDefaultAsync(ct);
                narrationRevisionId = revision.Id; narrationSegmentId = segment?.Id;
                narrationState = segment is null ? "slide_not_prepared" : "ready";
            }
        }
        var name = room.AgentId is Guid agent
            ? await db.Agents.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId && x.Id == agent).Select(x => x.DisplayName).SingleOrDefaultAsync(ct) ?? "Sales agent"
            : "Sales agent";
        var estimated = SalesRoomOperationsPolicy.EstimatedSpend(room, Options);
        var unavailable = !Options.Enabled || Options.EmergencyDisabled || Options.DrainEnabled ||
            lifecycle.CurrentValue.DrainEnabled || !lifecycle.CurrentValue.Enabled;
        var health = unavailable && room.AgentHealth is SalesRoomAgentHealthStates.NotStarted or SalesRoomAgentHealthStates.Stopped
            ? SalesRoomAgentHealthStates.Unavailable : room.AgentHealth;
        var voiceHealth = unavailable ? "disabled" : string.IsNullOrWhiteSpace(room.AgentVoiceHealth)
            ? "not_connected" : room.AgentVoiceHealth;
        SalesRoomFloorView? floorView = null;
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
        if (floor is not null)
        {
            var owner = floor.FloorOwnerParticipantId is Guid ownerId
                ? participants.FirstOrDefault(x => x.Id == ownerId)?.DisplayName : null;
            var pendingName = floor.PendingParticipantId is Guid pendingId
                ? participants.FirstOrDefault(x => x.Id == pendingId)?.DisplayName : null;
            int? elapsed = floor.PlaybackStopRequestedUtc is DateTime requested
                ? checked((int)Math.Max(0, (Now - requested).TotalMilliseconds)) : null;
            floorView = new(floor.State, owner ?? (floor.State == SalesRoomFloorStates.Agent ? name : "No one"),
                floor.FloorOwnerParticipantId, floor.ControlMode, floor.PendingTurnId, floor.PendingParticipantId,
                pendingName, floor.PendingQuestionId, floor.PendingTurnState, floor.PendingAddressedAgent, floor.Overlap,
                floor.TurnGeneration, floor.ResponseGeneration, floor.PresentationVersion, floor.SlideNumber,
                floor.TalkingPointIndex, floor.ResumeMarker, floor.ResumeOffsetMilliseconds,
                floor.PreauthorizedCoHostParticipantId, floor.Version,
                new(floor.LastPlaybackStopId, floor.PlaybackStopState, floor.PlaybackStopRequiredCount,
                    floor.PlaybackStopAcknowledgedCount, floor.PlaybackStopRequestedUtc, floor.PlaybackStopDeadlineUtc, elapsed));
        }
        return new(room.Id, room.AgentId, name, health, voiceHealth, room.AgentGeneration,
            room.AgentTurnGeneration, participants.Count(x => x.AiProcessingAllowed), participants.Count,
            participants.Count > 0 && participants.All(x => x.AiProcessingAllowed), room.AgentStartedUtc, room.AgentLeaseExpiresUtc,
            narrationRevisionId, narrationSegmentId, narrationState,
            room.AgentReceivedAudioMilliseconds, room.AgentDetectedSpeechMilliseconds, room.AgentForwardedAudioMilliseconds,
            room.AgentProviderBilledAudioMilliseconds > 0 ? room.AgentProviderBilledAudioMilliseconds : null,
            room.AgentProviderBilledAudioMilliseconds > 0 ? "reported_by_provider" : "provider_duration_not_reported",
            room.AgentOutputAudioMilliseconds, room.AgentInputTokens, room.AgentOutputTokens, estimated,
            room.AgentLastErrorCode, room.AgentLastErrorSummary, room.Version, latest, recent, floorView);
    }

    private async Task<bool> ReplayAsync(Guid company, Guid room, Guid command, string action, CancellationToken ct)
    {
        var prior = await db.SalesRoomOperations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.CommandId == command, ct);
        if (prior is null) return false;
        if (prior.RoomId != room || prior.Action != action) throw Error(SalesRoomAgentProblemCodes.Conflict, "This command ID was already used for another action.");
        return true;
    }
    private void Record(SalesBrowserRoom room, Guid command, string action, Guid actor, object payload)
    {
        var operation = new SalesRoomOperation(room.CompanyId, room.Id, command, action, null, actor,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))), Now, false);
        operation.Complete(); db.SalesRoomOperations.Add(operation);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), room.CompanyId, AuditActorTypes.User, actor,
            "sales.browser_room." + action, "sales_browser_room", room.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            action == "agent_start" ? "The organizer started the consent-aware room agent." : "The organizer stopped the room agent.",
            ["room consent", "agent authority"], correlationId: command.ToString("N"), occurredUtc: Now));
    }
    private static void ValidateCommand(Guid command) { if (command == Guid.Empty) throw Error(SalesRoomAgentProblemCodes.Conflict, "A command ID is required.", 400); }
    private static string? Bounded(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(max, value.Trim().Length)];
    private static SalesRoomAgentException Error(string code, string message, int status = 409) => new(code, message, status);
}
