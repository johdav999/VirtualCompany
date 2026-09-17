using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class SalesRoomAgentCoordinator(
    IServiceScopeFactory scopes,
    IOptionsMonitor<SalesRoomAgentOptions> configured,
    IOptionsMonitor<SalesRoomLifecycleOptions> lifecycle,
    TimeProvider clock,
    ILogger<SalesRoomAgentCoordinator> logger) : BackgroundService, ISalesRoomAgentCommandSink
{
    private readonly Channel<SalesRoomAgentWorkItem> work = Channel.CreateUnbounded<SalesRoomAgentWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<(Guid Company, Guid Room), Handle> active = new();

    public async ValueTask SignalAsync(SalesRoomAgentWorkItem item, CancellationToken ct)
    {
        if (item.Action == "stop" && active.TryGetValue((item.CompanyId, item.RoomId), out var stopped))
        {
            stopped.Stop.Cancel();
            await stopped.Control.TakeOverAsync(ct);
        }
        if (item.Action == "takeover" && active.TryGetValue((item.CompanyId, item.RoomId), out var current) &&
            current.Generation == item.Generation)
            await current.Control.TakeOverAsync(ct);
        await work.Writer.WriteAsync(item, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileSessionsAsync(stoppingToken);
        using var reconciliationStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var reconciliation = ReconcileLoopAsync(reconciliationStop.Token);
        try
        {
            await foreach (var item in work.Reader.ReadAllAsync(stoppingToken))
            {
                var key = (item.CompanyId, item.RoomId);
                if (item.Action == "stop")
                {
                    if (active.TryRemove(key, out var ended)) ended.Stop.Cancel();
                    continue;
                }
                if (AdmissionBlocked() || item.LeaseOwnerId == Guid.Empty || item.Generation < 1) continue;
                if (active.TryGetValue(key, out var current))
                {
                    if (current.Generation == item.Generation && !current.Task.IsCompleted) continue;
                    current.Stop.Cancel(); active.TryRemove(key, out _);
                }
                var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var control = new SalesRoomAgentRunControl();
                var task = RunRoomAsync(item, control, stop.Token);
                var handle = new Handle(item, stop, task, control);
                active[key] = handle;
                _ = task.ContinueWith(_ =>
                {
                    active.TryRemove(new KeyValuePair<(Guid, Guid), Handle>(key, handle));
                    stop.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        finally
        {
            reconciliationStop.Cancel();
            try { await reconciliation; } catch (OperationCanceledException) when (reconciliationStop.IsCancellationRequested) { }
        }
    }

    private async Task RunRoomAsync(SalesRoomAgentWorkItem item, SalesRoomAgentRunControl control, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SalesRoomAgentWorker>().RunAsync(item, control, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Browser room agent worker failed safely for room {RoomId}.", item.RoomId); }
    }

    private bool AdmissionBlocked() => !configured.CurrentValue.Enabled || configured.CurrentValue.EmergencyDisabled ||
        configured.CurrentValue.DrainEnabled || !lifecycle.CurrentValue.Enabled || lifecycle.CurrentValue.DrainEnabled;

    private async Task ReconcileLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(configured.CurrentValue.ReconciliationIntervalSeconds), ct);
            await ReconcileSessionsAsync(ct);
        }
    }

    private async Task ReconcileSessionsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;
            var stopAll = AdmissionBlocked();
            var rooms = await db.SalesBrowserRooms.IgnoreQueryFilters().Where(x => x.AgentLeaseOwnerId != null &&
                (x.AgentHealth == SalesRoomAgentHealthStates.Starting || x.AgentHealth == SalesRoomAgentHealthStates.Ready ||
                 x.AgentHealth == SalesRoomAgentHealthStates.Speaking || x.AgentHealth == SalesRoomAgentHealthStates.Paused)).ToListAsync(ct);
            rooms = rooms.Where(x => ShouldStopForReconciliation(x, stopAll, now)).ToList();
            foreach (var room in rooms)
            {
                var code = stopAll ? configured.CurrentValue.EmergencyDisabled ? "emergency_disabled" : "deployment_draining"
                    : "worker_lease_expired";
                room.StopAgent(code,
                    stopAll ? "Room AI stopped because an operator disabled or drained the service."
                        : "The agent stopped after its worker lease expired. The host must explicitly start it again.", now);
                var pending = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId &&
                    x.RoomId == room.Id && (x.Status == SalesRoomAgentSpeechStates.Queued ||
                    x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct);
                foreach (var item in pending)
                    item.Fail(SalesRoomAgentSpeechStates.Interrupted, code,
                        "The owning worker ended before this speech completed; it will not be replayed.", now);
                SalesRoomBenchmarkTelemetry.RecordOwnership(code);
            }
            if (rooms.Count > 0) await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (DbUpdateConcurrencyException)
        {
            SalesRoomBenchmarkTelemetry.RecordOwnership("reconcile_fenced");
            logger.LogInformation("Another browser-room instance completed lease reconciliation first.");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not reconcile browser room agent leases."); }
    }

    internal static bool ShouldStopForReconciliation(SalesBrowserRoom room, bool stopAll, DateTime nowUtc) =>
        room.AgentLeaseOwnerId is not null && (stopAll || room.AgentLeaseExpiresUtc <= nowUtc) &&
        room.AgentHealth is SalesRoomAgentHealthStates.Starting or SalesRoomAgentHealthStates.Ready or
            SalesRoomAgentHealthStates.Speaking or SalesRoomAgentHealthStates.Paused;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        work.Writer.TryComplete();
        await StopOwnedSessionsAsync(cancellationToken);
        foreach (var handle in active.Values) handle.Stop.Cancel();
        await base.StopAsync(cancellationToken);
    }

    private async Task StopOwnedSessionsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var handle in active.Values)
            {
                var item = handle.Item;
                var room = await db.SalesBrowserRooms.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == item.CompanyId && x.Id == item.RoomId, ct);
                if (room is null || !room.IsAgentOwner(item.LeaseOwnerId, item.Generation, now)) continue;
                room.StopAgent("deployment_draining", "The owning worker stopped cleanly during deployment drain.", now);
                var pending = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().Where(x => x.CompanyId == item.CompanyId &&
                    x.RoomId == item.RoomId && (x.Status == SalesRoomAgentSpeechStates.Queued ||
                    x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct);
                foreach (var speech in pending)
                    speech.Fail(SalesRoomAgentSpeechStates.Interrupted, "deployment_draining",
                        "The owning worker stopped before this speech completed; it will not be replayed.", now);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            SalesRoomBenchmarkTelemetry.RecordOwnership("shutdown_fenced");
            logger.LogInformation("Another browser-room instance changed ownership during shutdown.");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not release every locally owned browser-room lease during shutdown."); }
    }

    private sealed record Handle(SalesRoomAgentWorkItem Item, CancellationTokenSource Stop, Task Task, SalesRoomAgentRunControl Control)
    {
        public long Generation => Item.Generation;
    }
}

internal sealed class SalesRoomAgentRunControl
{
    private readonly object gate = new();
    private ISalesRoomMediaConnection? media;
    private CancellationTokenSource? response;
    public void Attach(ISalesRoomMediaConnection connection) { lock (gate) media = connection; }
    public void Detach(ISalesRoomMediaConnection connection) { lock (gate) { if (ReferenceEquals(media, connection)) media = null; } }
    public CancellationTokenSource BeginResponse(CancellationToken lifetime)
    {
        lock (gate)
        {
            response?.Cancel(); response?.Dispose();
            return response = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        }
    }
    public void EndResponse(CancellationTokenSource value) { lock (gate) { if (ReferenceEquals(response, value)) response = null; } value.Dispose(); }
    public bool CancelResponse()
    {
        lock (gate)
        {
            var interrupted = response is { IsCancellationRequested: false };
            response?.Cancel();
            return interrupted;
        }
    }
    public async Task TakeOverAsync(CancellationToken ct)
    {
        ISalesRoomMediaConnection? current;
        lock (gate) { response?.Cancel(); current = media; }
        if (current is not null) await current.CancelSpeechAsync(ct);
    }
}

internal sealed class SalesRoomTranscriptCorrelation<TContext> where TContext : class
{
    private readonly Queue<TContext> pending = new();
    private readonly Dictionary<string, TContext> committed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> earlyCompletions = new(StringComparer.Ordinal);
    private readonly HashSet<string> committedIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedIds = new(StringComparer.Ordinal);

    public int PendingCount => pending.Count + committed.Count + earlyCompletions.Count;
    public int SeenCount => committedIds.Count + completedIds.Count;

    public void Enqueue(TContext context) => pending.Enqueue(context);

    public bool Commit(string itemId, out TContext? context, out string? text)
    {
        context = null; text = null;
        if (string.IsNullOrWhiteSpace(itemId) || !committedIds.Add(itemId) || !pending.TryDequeue(out var submitted))
            return false;
        if (earlyCompletions.Remove(itemId, out var completedText))
        {
            context = submitted; text = completedText; return true;
        }
        committed[itemId] = submitted;
        return false;
    }

    public bool Complete(string itemId, string text, out TContext? context, out string? completedText)
    {
        context = null; completedText = null;
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(text) || !completedIds.Add(itemId))
            return false;
        if (committed.Remove(itemId, out var submitted))
        {
            context = submitted; completedText = text; return true;
        }
        earlyCompletions[itemId] = text;
        return false;
    }
}
internal sealed class SalesRoomAgentWorker(
    VirtualCompanyDbContext db,
    IServiceScopeFactory captureScopes,
    ICompanyExecutionScopeFactory executionScopes,
    ISalesRoomMediaTransport mediaTransport,
    IRealtimeAgentPcmSessionGateway pcm,
    IRealtimeAgentSessionGateway realtime,
    ISalesNarrationService narration,
    IApprovedSpeechGateway speech,
    ISalesMeetingPresentationConductor conductor,
    ISalesMeetingQuestionAnsweringService questions,
    ISalesRoomFloorEventPublisher floorEvents,
    IOptionsMonitor<SalesRoomAgentOptions> configured,
    IOptionsMonitor<SalesRoomLifecycleOptions> lifecycle,
    TimeProvider clock,
    ILogger<SalesRoomAgentWorker> logger)
{
    private SalesRoomAgentOptions Options => configured.CurrentValue;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task RunAsync(SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(work, control, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (DbUpdateConcurrencyException)
            {
                SalesRoomBenchmarkTelemetry.RecordOwnership("worker_concurrency_retry");
                logger.LogInformation(
                    "Browser room agent is reconnecting after a concurrent room command for room {RoomId}.",
                    work.RoomId);
                db.ChangeTracker.Clear();
                var current = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CompanyId == work.CompanyId && x.Id == work.RoomId, ct);
                if (current is null || !current.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
                    current.State != SalesBrowserRoomStates.Live)
                    return;
            }
            catch (Exception ex)
            {
                SalesRoomBenchmarkTelemetry.RecordFailure("agent_worker");
                var failureCode = ex switch
                {
                    SalesRoomVadException vad => vad.Code,
                    RealtimeAgentUnavailableException provider => provider.Code,
                    SalesRoomMediaException mediaError => mediaError.Code,
                    _ => "agent_worker_failed"
                };
                logger.LogWarning(
                    "Browser room agent paused safely for room {RoomId}; exception type {ExceptionType}, code {FailureCode}.",
                    work.RoomId, ex.GetType().Name, failureCode);
                await PauseAsync(work, failureCode);
                return;
            }
        }
    }

    private async Task RunOnceAsync(SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, CancellationToken ct)
    {
        using var companyScope = executionScopes.BeginScope(work.CompanyId);
        ISalesRoomMediaConnection? media = null; string? providerSession = null;
        IDisposable? benchmarkSession = null;
        var events = Channel.CreateBounded<RuntimeEvent>(new BoundedChannelOptions(64)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
        try
        {
            var room = await RoomAsync(work, ct);
            if (room.MeetingSessionId is not Guid sessionId || room.AgentId is not Guid agentId ||
                !room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
            var participants = await ConsentedParticipantsAsync(room, ct);
            var organizerParticipantId = participants.Single(x => x.MemberUserId == room.OrganizerUserId).Id;
            var humans = participants.Select(x => new SalesRoomMediaParticipant(x.Id, false, x.Generation,
                new DateTimeOffset(DateTime.SpecifyKind(room.ExpiresUtc < x.ExpiresUtc ? room.ExpiresUtc : x.ExpiresUtc, DateTimeKind.Utc)))).ToArray();
            if (AdmissionBlocked())
            {
                await StopForPolicyAsync(room, work, configured.CurrentValue.EmergencyDisabled ? "emergency_disabled" : "deployment_draining");
                return;
            }
            var connectStarted = Stopwatch.GetTimestamp();
            var provider = await RunStartupWithLeaseRenewalAsync(work, async startupToken =>
            {
                media = await mediaTransport.ConnectAgentAsync(new(room.CompanyId, room.Id),
                    new(agentId, true, work.Generation, new DateTimeOffset(DateTime.SpecifyKind(room.ExpiresUtc, DateTimeKind.Utc))), humans, startupToken);
                SalesRoomBenchmarkTelemetry.RecordLatency("voice_connect", Stopwatch.GetElapsedTime(connectStarted));
                control.Attach(media);
                await AlignOutputGenerationAsync(media, room.AgentTurnGeneration, startupToken);
                return await pcm.CreatePcmSessionAsync(new(room.CompanyId, room.OrganizerUserId, agentId,
                    "sales_browser_room_segmented_transcription",
                    "Transcribe each explicitly committed utterance exactly and preserve interrogative wording. " +
                    "Meeting vocabulary can include Virtual Company, Alex, finance agent, sales agent, marketing agent, " +
                    "support agent, and questions about what those agents can do. Do not answer, speak, call tools, or infer speaker identity.",
                    [], TimeSpan.FromMinutes(Math.Min(Options.MaximumSessionMinutes,
                        Math.Max(1, (room.ExpiresUtc - Now).TotalMinutes))), work.RoomId.ToString("N"), true), startupToken);
            }, ct);
            providerSession = provider.ProviderSessionId;
            db.ChangeTracker.Clear();
            room = await RoomAsync(work, ct);
            if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
            room.AgentReady(work.LeaseOwnerId, work.Generation);
            await db.SaveChangesAsync(ct);
            benchmarkSession = SalesRoomBenchmarkTelemetry.BeginSession();

            var detector = new EnergyLocalVoiceActivityDetector(OptionsWrapper(Options));
            var segmenter = new SalesRoomVoiceActivitySegmenter(Options, detector);
            using var runtimeStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var inputTask = ReadInputAsync(media, segmenter, control, events.Writer, room.CompanyId, sessionId, room.AgentTurnGeneration, runtimeStop.Token);
            var providerTask = ReadProviderAsync(providerSession, events.Writer, runtimeStop.Token);
            var transcriptCorrelation = new SalesRoomTranscriptCorrelation<UtteranceContext>();
            var participantsInterruptingAgent = new HashSet<Guid>();
            var lastReceived = 0L; var lastDetected = 0L; var lastForwarded = 0L;
            var renewAt = Now.AddSeconds(Options.RenewalSeconds);
            var organizerMissing = false;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(200, ct);
                db.ChangeTracker.Clear();
                room = await RoomAsync(work, ct);
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
                    room.State != SalesBrowserRoomStates.Live) break;
                if (AdmissionBlocked())
                {
                    await media.CancelSpeechAsync(CancellationToken.None);
                    await StopForPolicyAsync(room, work,
                        configured.CurrentValue.EmergencyDisabled ? "emergency_disabled" : "deployment_draining");
                    break;
                }
                if (!await HasAllConsentAsync(room, ct))
                {
                    await media.CancelSpeechAsync(CancellationToken.None);
                    room.PauseAgent(work.LeaseOwnerId, work.Generation, "consent_required",
                        "AI paused because an admitted participant has not consented. Human audio and manual slides remain available.");
                    await db.SaveChangesAsync(CancellationToken.None);
                    break;
                }
                var hostConnected = media.IsParticipantConnected(organizerParticipantId);
                if (!hostConnected && !organizerMissing && room.AgentStartedUtc <= Now.AddSeconds(-Options.OrganizerDisconnectGraceSeconds))
                {
                    organizerMissing = true; control.CancelResponse();
                    await media.CancelSpeechAsync(CancellationToken.None);
                    room.PauseAgent(work.LeaseOwnerId, work.Generation, "organizer_disconnected",
                        "AI paused because the organizer left. Only a preauthorized connected co-host may resume.");
                    SalesRoomPlaybackStopRequest? stopRequest = null;
                    var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                    if (floor is not null)
                    {
                        var stopId = Guid.NewGuid();
                        floor.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
                        var required = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.State == SalesRoomParticipantStates.Admitted && x.Connected, ct);
                        floor.RequestPlaybackStop(stopId, required,
                            Now.AddMilliseconds(Options.PlaybackStopAcknowledgementTimeoutMilliseconds), Now);
                        stopRequest = new(room.Id, stopId, floor.ResponseGeneration, floor.PlaybackStopDeadlineUtc!.Value);
                    }
                    await db.SaveChangesAsync(CancellationToken.None);
                    if (stopRequest is not null)
                        await floorEvents.RequestPlaybackStopAsync(room.CompanyId, sessionId, stopRequest, ct);
                }
                if (hostConnected) organizerMissing = false;

                foreach (var utterance in segmenter.FlushExpired(DateTimeOffset.UtcNow))
                    if (!events.Writer.TryWrite(new(RuntimeEventKind.Utterance, utterance)))
                        throw new SalesRoomAccessException("transcription_backpressure");

                while (events.Reader.TryRead(out var runtime))
                {
                    if (runtime.Kind is RuntimeEventKind.DetectorFailure or RuntimeEventKind.ProviderFailure)
                    {
                        await media.CancelSpeechAsync(CancellationToken.None);
                        var detectorFailed = runtime.Kind == RuntimeEventKind.DetectorFailure;
                        room.PauseAgent(work.LeaseOwnerId, work.Generation,
                            detectorFailed ? "vad_failed" : "transcription_unavailable",
                            detectorFailed ? "Speech detection failed. AI is paused; use typed questions while the human call continues."
                                : "Speech transcription failed. AI is paused; use typed questions while the human call continues.");
                        await db.SaveChangesAsync(CancellationToken.None);
                        runtimeStop.Cancel(); await AwaitQuietly(inputTask, providerTask); return;
                    }
                    if (runtime.Kind == RuntimeEventKind.UnexpectedProviderAudio)
                    {
                        await media.CancelSpeechAsync(CancellationToken.None);
                        room.PauseAgent(work.LeaseOwnerId, work.Generation, "unchecked_provider_audio",
                            "Unexpected provider audio was blocked before publication.");
                        await db.SaveChangesAsync(CancellationToken.None);
                        runtimeStop.Cancel(); await AwaitQuietly(inputTask, providerTask); return;
                    }
                    if (runtime.Kind == RuntimeEventKind.SpeechStarted)
                    {
                        control.CancelResponse();
                        var interruptedAgent = runtime.InterruptedAgent ||
                            room.AgentHealth == SalesRoomAgentHealthStates.Speaking;
                        if (interruptedAgent && runtime.ParticipantId is Guid interruptingParticipant)
                            participantsInterruptingAgent.Add(interruptingParticipant);
                        room.PreemptAgent(work.LeaseOwnerId, work.Generation,
                            "A participant started speaking. Agent audio stopped immediately.");
                        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                        SalesRoomPlaybackStopRequest? stopRequest = null;
                        var speaker = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == runtime.ParticipantId, ct);
                        if (floor is not null && speaker is not null)
                        {
                            var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                                x.CompanyId == room.CompanyId && x.Id == sessionId, ct);
                            floor.HumanStarted(speaker.Id, speaker.Generation, false, room.AgentTurnGeneration,
                                meeting.ConcurrencyVersion, Math.Max(1, meeting.CurrentSlideIndex),
                                meeting.CurrentTalkingPointIndex, meeting.ResumeMarker, Now);
                            var stopId = Guid.NewGuid();
                            var required = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
                                x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                                x.State == SalesRoomParticipantStates.Admitted && x.Connected, ct);
                            floor.RequestPlaybackStop(stopId, required,
                                Now.AddMilliseconds(Options.PlaybackStopAcknowledgementTimeoutMilliseconds), Now);
                            stopRequest = new(room.Id, stopId, floor.ResponseGeneration, floor.PlaybackStopDeadlineUtc!.Value);
                        }
                        if (interruptedAgent)
                        {
                            var processing = await db.SalesRoomAgentSpeech.Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                                x.Status == SalesRoomAgentSpeechStates.Processing).ToListAsync(ct);
                            foreach (var item in processing) item.Fail(SalesRoomAgentSpeechStates.Interrupted, "human_speaking",
                                "A participant spoke before the agent finished.", Now);
                        }
                        await db.SaveChangesAsync(ct);
                        if (stopRequest is not null)
                            await floorEvents.RequestPlaybackStopAsync(room.CompanyId, sessionId, stopRequest, ct);
                        continue;
                    }
                    if (runtime.Utterance is { } utterance)
                    {
                        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == utterance.ParticipantId, ct);
                        if (participant is null || participant.State != SalesRoomParticipantStates.Admitted || !participant.AiProcessingAllowed)
                        { segmenter.Clear(utterance.ParticipantId); continue; }
                        if (transcriptCorrelation.PendingCount >= 64 || transcriptCorrelation.SeenCount >= 4096)
                            throw new SalesRoomAccessException("transcription_backpressure");
                        foreach (var chunk in PcmChunks(utterance.Samples))
                            await pcm.SendInputAudioAsync(providerSession, chunk, ct);
                        await pcm.SendClientEventAsync(providerSession,
                            JsonSerializer.Serialize(new { event_id = "commit_" + Guid.NewGuid().ToString("N"), type = "input_audio_buffer.commit" }), ct);
                        transcriptCorrelation.Enqueue(new(utterance with { Samples = ReadOnlyMemory<short>.Empty }, participant.Version,
                            participant.TranscriptRetentionAllowed, participantsInterruptingAgent.Remove(utterance.ParticipantId)));
                        continue;
                    }
                    if (runtime.ProviderJson is not null)
                    {
                        using var providerPayload = JsonDocument.Parse(runtime.ProviderJson);
                        if (providerPayload.RootElement.TryGetProperty("type", out var eventType) &&
                            eventType.GetString() == "input_audio_buffer.committed" &&
                            providerPayload.RootElement.TryGetProperty("item_id", out var committedItem))
                        {
                            var committedId = committedItem.GetString();
                            if (committedId is not null &&
                                transcriptCorrelation.Commit(committedId, out var completedContext, out var completedText))
                                await ProcessTranscriptAsync(completedContext!, completedText!);
                            continue;
                        }
                        RealtimeAgentEvent normalized;
                        try { normalized = await realtime.NormalizeEventAsync(new(providerSession, runtime.ProviderEventId!, runtime.ProviderSequence, runtime.ProviderJson), ct); }
                        catch (RealtimeAgentEventException) { continue; }
                        if (normalized.Type == RealtimeAgentEventTypes.ParticipantTranscriptCompleted)
                        {
                            using var payload = JsonDocument.Parse(runtime.ProviderJson);
                            var itemId = payload.RootElement.TryGetProperty("item_id", out var providerItem) ? providerItem.GetString() : runtime.ProviderEventId;
                            // Completion and commit acknowledgements can arrive in either order. The correlator waits for
                            // both so only the server-side commit can bind speech to a participant.
                            if (itemId is not null && !string.IsNullOrWhiteSpace(normalized.Text) &&
                                transcriptCorrelation.Complete(itemId, normalized.Text, out var completedContext, out var completedText))
                                await ProcessTranscriptAsync(completedContext!, completedText!);
                        }
                        if (normalized.InputTokens > 0 || normalized.OutputTokens > 0 || normalized.AudioDurationMilliseconds > 0)
                        {
                            room.RecordAgentAudio(work.LeaseOwnerId, work.Generation, 0, 0, 0,
                                normalized.AudioDurationMilliseconds, normalized.InputTokens, normalized.OutputTokens);
                            SalesRoomBenchmarkTelemetry.RecordProvider(normalized.AudioDurationMilliseconds,
                                normalized.InputTokens, normalized.OutputTokens);
                        }
                    }
                }

                var receivedDelta = segmenter.ReceivedMilliseconds - lastReceived;
                var detectedDelta = segmenter.DetectedSpeechMilliseconds - lastDetected;
                var forwardedDelta = segmenter.ForwardedMilliseconds - lastForwarded;
                if (receivedDelta > 0 || detectedDelta > 0 || forwardedDelta > 0)
                {
                    room.RecordAgentAudio(work.LeaseOwnerId, work.Generation, receivedDelta, detectedDelta, forwardedDelta, 0, 0, 0);
                    SalesRoomBenchmarkTelemetry.RecordInput(receivedDelta, detectedDelta, forwardedDelta);
                    lastReceived = segmenter.ReceivedMilliseconds; lastDetected = segmenter.DetectedSpeechMilliseconds;
                    lastForwarded = segmenter.ForwardedMilliseconds;
                }
                if (room.AgentForwardedAudioMilliseconds > Options.MaximumInputAudioSeconds * 1000L ||
                    room.AgentOutputAudioMilliseconds > Options.MaximumOutputAudioSeconds * 1000L)
                {
                    await media.CancelSpeechAsync(CancellationToken.None);
                    room.PauseAgent(work.LeaseOwnerId, work.Generation, "quota_exceeded",
                        "The configured room AI audio limit was reached. Human calling and manual slides remain available.");
                    await db.SaveChangesAsync(CancellationToken.None); break;
                }
                if (room.AgentStartedUtc <= Now.AddMinutes(-Options.MaximumSessionMinutes))
                {
                    SalesRoomBenchmarkTelemetry.RecordQuota("call_duration");
                    await media.CancelSpeechAsync(CancellationToken.None);
                    await StopForPolicyAsync(room, work, "duration_limit");
                    break;
                }
                var callSpend = SalesRoomOperationsPolicy.EstimatedSpend(room, Options);
                SalesRoomBenchmarkTelemetry.RecordEstimatedSpend(callSpend);
                if (callSpend >= Options.MaximumSpendPerCallUsd)
                {
                    SalesRoomBenchmarkTelemetry.RecordQuota("call_spend");
                    await media.CancelSpeechAsync(CancellationToken.None);
                    await StopForPolicyAsync(room, work, "spend_limit");
                    break;
                }
                if (Now >= renewAt)
                {
                    if (await CompanyMonthlySpendAsync(room.CompanyId, ct) >= Options.MaximumMonthlySpendPerCompanyUsd)
                    {
                        SalesRoomBenchmarkTelemetry.RecordQuota("company_monthly_spend");
                        await media.CancelSpeechAsync(CancellationToken.None);
                        await StopForPolicyAsync(room, work, "spend_limit");
                        break;
                    }
                    if (!room.RenewAgentLease(work.LeaseOwnerId, work.Generation, Now.AddSeconds(Options.LeaseSeconds), Now)) break;
                    renewAt = Now.AddSeconds(Options.RenewalSeconds);
                }

                var queued = await db.SalesRoomAgentSpeech.Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                        x.AgentGeneration == work.Generation && x.Status == SalesRoomAgentSpeechStates.Queued)
                    .OrderBy(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
                SalesRoomBenchmarkTelemetry.QueueDepth.Record(
                    transcriptCorrelation.PendingCount + (queued is null ? 0 : 1));
                if (queued is not null)
                {
                    await SpeakAsync(room, queued, media, work, control, ct);
                }
                var currentFloor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                currentFloor?.ExpireStop(Now);
                await db.SaveChangesAsync(ct);
            }
            runtimeStop.Cancel(); segmenter.ClearAll();
            events.Writer.TryComplete();
            await AwaitQuietly(inputTask, providerTask);

            async Task ProcessTranscriptAsync(UtteranceContext context, string text)
            {
                await using var captureScope = captureScopes.CreateAsyncScope();
                var retained = await captureScope.ServiceProvider.GetRequiredService<ISalesRoomCaptureService>().RetainAsync(
                    new(room.CompanyId, room.Id, context.Value.ParticipantId, context.ConsentVersion, context.Retain,
                        work.Generation, work.LeaseOwnerId, context.Value.TrackId, context.Value.TrackGeneration,
                        context.Value.StartedAt.UtcDateTime, context.Value.EndedAt.UtcDateTime, context.Value.Overlapped, text), ct);
                var current = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == context.Value.ParticipantId, ct);
                // Persistent question/AI-run content can only derive from committed, permitted evidence.
                var fence = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                    x.CompanyId == room.CompanyId && x.Id == room.Id, ct);
                if (current is { AiProcessingAllowed: true } &&
                    current.Version == context.ConsentVersion && fence.State == SalesBrowserRoomStates.Live &&
                    fence.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now))
                    await HandleTranscriptAsync(room, current, context.Value, text,
                        context.InterruptedAgent, retained.HasValue, media, work, floorEvents, questions, ct);
            }
        }

        finally

        {
            benchmarkSession?.Dispose();
            events.Writer.TryComplete();
            if (media is not null)
            {
                control.Detach(media);
                var statistics = media.GetStatistics();
                SalesRoomBenchmarkTelemetry.RecordMedia(statistics.DroppedFrames, statistics.Reconnects);
                try { await media.CancelSpeechAsync(CancellationToken.None); await media.DisposeAsync(); } catch { }
            }
            if (providerSession is not null) { try { await pcm.TerminatePcmSessionAsync(providerSession, CancellationToken.None); } catch { } }
        }
    }

    private async Task ReadInputAsync(ISalesRoomMediaConnection media, SalesRoomVoiceActivitySegmenter segmenter,
        SalesRoomAgentRunControl control, ChannelWriter<RuntimeEvent> writer, Guid companyId, Guid sessionId,
        long presentationVersion, CancellationToken ct)
    {
        try
        {
            await foreach (var frame in media.ReceiveAsync(ct))
            {
                var result = segmenter.Push(frame);
                if (result.SpeechStarted)
                {
                    var interruptedAgent = control.CancelResponse();
                    await media.CancelSpeechAsync(CancellationToken.None);
                    conductor.Preempt(companyId, sessionId, presentationVersion);
                    await writer.WriteAsync(new(RuntimeEventKind.SpeechStarted,
                        ParticipantId: frame.ParticipantId, InterruptedAgent: interruptedAgent), ct);
                }
                if (result.Utterance is not null) await writer.WriteAsync(new(RuntimeEventKind.Utterance, result.Utterance), ct);
            }
        }
        catch (SalesRoomVadException) { writer.TryWrite(new(RuntimeEventKind.DetectorFailure)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { writer.TryWrite(new(RuntimeEventKind.DetectorFailure)); }
    }

    private async Task ReadProviderAsync(string session, ChannelWriter<RuntimeEvent> writer, CancellationToken ct)
    {
        try
        {
            await foreach (var value in pcm.ReceiveOutputAsync(session, ct))
            {
                if (!value.Audio.IsEmpty) { await writer.WriteAsync(new(RuntimeEventKind.UnexpectedProviderAudio), ct); continue; }
                if (value.ProviderEventJson is not null)
                    await writer.WriteAsync(new(RuntimeEventKind.ProviderEvent, ProviderJson: value.ProviderEventJson,
                        ProviderEventId: value.ProviderEventId, ProviderSequence: value.Sequence), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { writer.TryWrite(new(RuntimeEventKind.ProviderFailure)); }
    }

    private async Task SpeakAsync(SalesBrowserRoom room, SalesRoomAgentSpeech item,
        ISalesRoomMediaConnection media, SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, CancellationToken ct)
    {
        using var responseStop = control.BeginResponse(ct);
        var responseCt = responseStop.Token;
        item.Claim(Now); await db.SaveChangesAsync(responseCt);
        byte[] bytes; string? text = null, evidence = null, response = null;
        var generatedMilliseconds = 0;
        var published = 0;
        try
        {
            if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) || item.TurnGeneration != room.AgentTurnGeneration)
                throw new WithheldSpeech("turn_fenced", "The requested speech belongs to an obsolete room turn.");
            await EnsureFloorAsync(room, item, responseCt);
            await EnsureConsentAsync(room, responseCt);
            if (item.Kind == SalesRoomAgentSpeechKinds.Narration)
            {
                var plan = await conductor.PrepareAsync(new(room.CompanyId, room.OrganizerUserId, item.SessionId,
                    null, null, null, null, null, item.CommandId.ToString("N"), item.AgentId), responseCt);
                if (plan is null)
                {
                    logger.LogWarning("No active presentation snapshot was available for room {RoomId}, session {SessionId}, company {CompanyId}.",
                        room.Id, item.SessionId, room.CompanyId);
                    throw new WithheldSpeech("presentation_not_ready",
                        "Narration is paused because the active presentation could not be loaded.");
                }
                if (!await AudienceReadyAsync(room, plan.Snapshot.Stage.Version, responseCt))
                    throw new WithheldSpeech(plan.ReasonCode ?? "audience_not_ready",
                        $"Narration is paused until every required participant renders presentation version {plan.Snapshot.Stage.Version}; reason: {plan.ReasonCode ?? "audience_not_ready"}.");
                var session = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(x => x.CompanyId == room.CompanyId && x.Id == item.SessionId, ct);
                var playback = await narration.OpenPlaybackAsync(room.CompanyId, item.RequestedByUserId,
                    new(item.NarrationRevisionId!.Value, item.NarrationSegmentId!.Value, item.SessionId,
                        session.CustomerCompanyId, item.OffsetMilliseconds, item.TurnGeneration), responseCt);
                if (playback.SlideNumber != plan.Snapshot.Stage.SlideNumber || playback.TalkingPoint < 1)
                    throw new WithheldSpeech("narration_slide_changed", "The approved narration does not match the customer-visible slide.");
                bytes = playback.Pcm;
            }
            else
            {
                var question = await ReleasedQuestionAsync(room, item, responseCt);
                text = question.AnswerText!;
                evidence = JsonSerializer.Serialize(question.Evidence.Select(x => new { x.SourceId, x.SourceType, x.SourceTitle }).Distinct());
                var profile = await speech.GetProfileAsync(responseCt);
                if (!profile.Available) throw new WithheldSpeech("voice_unavailable", "Approved-text speech is unavailable.");
                var language = await db.SalesNarrationRevisions.AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
                        x.SessionId == room.MeetingSessionId && x.ApprovedUtc != null && x.RevokedUtc == null && x.RetainUntilUtc > Now)
                    .OrderByDescending(x => x.ApprovedUtc).Select(x => x.Language).FirstOrDefaultAsync(ct) ?? "en";
                var generated = await speech.GenerateAsync(new(room.CompanyId, item.RequestedByUserId, item.AgentId,
                    text, language, profile.Voice, profile.ConfigurationVersion, item.Id.ToString("N")), responseCt);
                if (!generated.ContentMatches) throw new WithheldSpeech("speech_content_mismatch", "Generated audio did not match the released answer.");
                db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                _ = await ReleasedQuestionAsync(room, item, responseCt);
                await EnsureFloorAsync(room, item, responseCt);
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) || item.TurnGeneration != room.AgentTurnGeneration)
                    throw new WithheldSpeech("turn_fenced", "The room turn changed before answer publication.");
                bytes = generated.Pcm; response = generated.ProviderResponseId;
                room.RecordAgentAudio(work.LeaseOwnerId, work.Generation, 0, 0, 0, 0, generated.InputTokens, generated.OutputTokens);
            }
            if (bytes.Length == 0 || bytes.Length % 2 != 0 || bytes.Length > 24_000 * 2 * 120)
                throw new WithheldSpeech("invalid_approved_audio", "The approved audio was empty or outside the room limit.");
            var samples = MemoryMarshal.Cast<byte, short>(bytes).ToArray();
            generatedMilliseconds = checked((int)(samples.Length * 1000L / 24_000));
            await AlignOutputGenerationAsync(media, item.TurnGeneration, responseCt);
            room.AgentSpeaking(work.LeaseOwnerId, work.Generation); await db.SaveChangesAsync(responseCt);
            for (var offset = 0; offset < samples.Length; offset += 480)
            {
                var length = Math.Min(480, samples.Length - offset);
                ReadOnlyMemory<short> frame = samples.AsMemory(offset, length);
                if (length < 480)
                {
                    var padded = new short[480]; frame.Span.CopyTo(padded); frame = padded;
                }
                if (!await media.SendAsync(item.TurnGeneration, 24_000, frame, responseCt))
                    throw new WithheldSpeech("speech_interrupted", "Agent speech was stopped before completion.");
                published += length;
            }
            if (!await media.CompleteSpeechAsync(item.TurnGeneration, responseCt))
                throw new WithheldSpeech("speech_interrupted", "Agent speech was stopped before completion.");
            var duration = checked((int)(published * 1000L / 24_000));
            SalesRoomBenchmarkTelemetry.RecordOutput(item.Kind, generatedMilliseconds, duration, 0);
            item.Complete(text, evidence, response, duration, Now);
            room.AgentSpeechCompleted(work.LeaseOwnerId, work.Generation, duration);
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id, responseCt);
            SalesNarrationSegment? nextSegment = null;
            if (floor is not null && floor.ResponseGeneration == item.ResponseGeneration &&
                item.Kind == SalesRoomAgentSpeechKinds.Narration &&
                item.NarrationRevisionId is Guid revisionId && item.NarrationSegmentId is Guid segmentId)
            {
                var completedSegment = await db.SalesNarrationSegments.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.CompanyId == room.CompanyId && x.RevisionId == revisionId && x.Id == segmentId, responseCt);
                nextSegment = await db.SalesNarrationSegments.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == room.CompanyId && x.RevisionId == revisionId &&
                        x.SlideNumber == completedSegment.SlideNumber && x.TalkingPoint > completedSegment.TalkingPoint)
                    .OrderBy(x => x.TalkingPoint).FirstOrDefaultAsync(responseCt);
            }
            var continueCurrentSlide = floor is not null && floor.ResponseGeneration == item.ResponseGeneration &&
                nextSegment is not null && floor.ControlMode is SalesPresentationControlModes.Assisted or SalesPresentationControlModes.Autonomous;
            var continueDeck = floor is not null && floor.ResponseGeneration == item.ResponseGeneration &&
                item.Kind == SalesRoomAgentSpeechKinds.Narration && nextSegment is null &&
                floor.ControlMode == SalesPresentationControlModes.Autonomous;
            if (floor is not null && floor.ResponseGeneration == item.ResponseGeneration)
            {
                if (continueCurrentSlide)
                {
                    floor.AgentAdvanced(floor.PresentationVersion, floor.SlideNumber, nextSegment!.TalkingPoint,
                        $"slide:{floor.SlideNumber}:talking-point:{nextSegment.TalkingPoint}", room.AgentTurnGeneration, Now);
                    var commandId = StableTurnId(room.Id, floor.HostParticipantId, "narration-point",
                        floor.ResponseGeneration, new DateTimeOffset(Now, TimeSpan.Zero));
                    db.SalesRoomAgentSpeech.Add(new SalesRoomAgentSpeech(Guid.NewGuid(), room.CompanyId, room.Id,
                        item.SessionId, commandId, item.AgentId, work.Generation, room.AgentTurnGeneration,
                        SalesRoomAgentSpeechKinds.Narration, item.RequestedByUserId, Now,
                        item.NarrationRevisionId, nextSegment.Id, responseGeneration: floor.ResponseGeneration));
                }
                else if (!continueDeck)
                {
                    floor.AgentCompleted(floor.HostParticipantId, Now, nextSegment?.TalkingPoint);
                }
            }
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), room.CompanyId, AuditActorTypes.User, item.RequestedByUserId,
                "sales.browser_room.agent_spoke", "sales_room_agent_speech", item.Id.ToString("D"), AuditEventOutcomes.Succeeded,
                "The room agent published one release-checked speech item to its shared voice track.",
                item.Kind == SalesRoomAgentSpeechKinds.Answer ? ["released answer", "question evidence"] : ["approved narration asset", "audience binding"],
                new Dictionary<string, string?> { ["roomId"] = room.Id.ToString("D"), ["kind"] = item.Kind,
                    ["questionId"] = item.QuestionId?.ToString("D"), ["segmentId"] = item.NarrationSegmentId?.ToString("D") },
                item.CommandId.ToString("N"), Now));
            await db.SaveChangesAsync(responseCt);
            if (continueCurrentSlide)
                await floorEvents.AllowPlaybackAsync(room.CompanyId, item.SessionId,
                    new(room.Id, floor!.ResponseGeneration), responseCt);
            else if (continueDeck)
                await ContinueAutonomousDeckAsync(room.CompanyId, room.Id, item, work, responseCt);
        }
        catch (OperationCanceledException) when (responseCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            var played = checked((int)(published * 1000L / 24_000));
            SalesRoomBenchmarkTelemetry.RecordOutput(item.Kind, generatedMilliseconds, played,
                Math.Max(0, generatedMilliseconds - played));
            var duration = item.OffsetMilliseconds + checked((int)(published * 1000L / 24_000));
            db.ChangeTracker.Clear();
            room = await RoomAsync(work, CancellationToken.None);
            var currentItem = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == room.CompanyId && x.Id == item.Id, CancellationToken.None);
            if (currentItem is not null && currentItem.Status == SalesRoomAgentSpeechStates.Processing)
                currentItem.Fail(SalesRoomAgentSpeechStates.Interrupted, "speech_interrupted",
                    "Human speech or takeover stopped this response.", Now);
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id, CancellationToken.None);
            if (floor is not null && floor.ResponseGeneration == item.ResponseGeneration)
                floor.PauseAt(duration, room.AgentTurnGeneration, Now);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is WithheldSpeech or SalesNarrationException)
        {
            var ex = error as WithheldSpeech ?? new WithheldSpeech("narration_release_invalid", error.Message);
            var played = checked((int)(published * 1000L / 24_000));
            SalesRoomBenchmarkTelemetry.RecordOutput(item.Kind, generatedMilliseconds, played,
                Math.Max(0, generatedMilliseconds - played));
            item.Fail(ex.Code == "speech_interrupted" ? SalesRoomAgentSpeechStates.Interrupted : SalesRoomAgentSpeechStates.Withheld,
                ex.Code, ex.Message, Now);
            if (room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now))
            {
                room.PauseAgent(work.LeaseOwnerId, work.Generation, ex.Code, ex.Message,
                    ex.Code == "voice_unavailable" ? "degraded" : room.AgentVoiceHealth);
                var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id, CancellationToken.None);
                if (floor is not null && floor.ResponseGeneration == item.ResponseGeneration &&
                    floor.State == SalesRoomFloorStates.Agent)
                    floor.PauseAt(item.OffsetMilliseconds + checked((int)(published * 1000L / 24_000)),
                        room.AgentTurnGeneration, Now);
            }
        }
        finally { control.EndResponse(responseStop); }
    }

    private async Task EnsureFloorAsync(SalesBrowserRoom room, SalesRoomAgentSpeech item, CancellationToken ct)
    {
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
        if (floor is null || floor.State != SalesRoomFloorStates.Agent ||
            floor.TurnGeneration != item.TurnGeneration || floor.ResponseGeneration != item.ResponseGeneration)
            throw new WithheldSpeech("floor_fenced", "The floor changed before this response could be published.");
    }

    private async Task ContinueAutonomousDeckAsync(Guid companyId, Guid roomId, SalesRoomAgentSpeech completed,
        SalesRoomAgentWorkItem work, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var room = await RoomAsync(work, ct);
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.RoomId == roomId, ct);
        if (floor is null || floor.State != SalesRoomFloorStates.Agent ||
            floor.ControlMode != SalesPresentationControlModes.Autonomous ||
            floor.ResponseGeneration != completed.ResponseGeneration ||
            room.AgentTurnGeneration != completed.TurnGeneration ||
            room.AgentLeaseOwnerId is not Guid owner || room.AgentId is not Guid agentId)
            return;
        await EnsureConsentAsync(room, ct);

        SalesPresentationNarrationPlan? plan;
        try
        {
            plan = await conductor.PrepareAsync(new(room.CompanyId, room.OrganizerUserId, completed.SessionId,
                SalesPresentationToolNames.Next, null, null, null, null,
                "autonomous_" + completed.Id.ToString("N"), agentId), ct);
        }
        catch (SalesPresentationRuntimeConflictException)
        {
            return;
        }

        db.ChangeTracker.Clear();
        room = await RoomAsync(work, ct);
        floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.RoomId == roomId, ct);
        if (floor is null || floor.State != SalesRoomFloorStates.Agent ||
            floor.ControlMode != SalesPresentationControlModes.Autonomous ||
            floor.ResponseGeneration != completed.ResponseGeneration ||
            room.AgentTurnGeneration != completed.TurnGeneration ||
            room.AgentLeaseOwnerId != owner)
            return;
        if (plan is null || plan.ReasonCode == "last_slide_reached")
        {
            floor.AgentCompleted(floor.HostParticipantId, Now);
            await db.SaveChangesAsync(ct);
            return;
        }

        floor.AgentAdvanced(plan.Snapshot.Stage.Version, plan.Snapshot.Stage.SlideNumber,
            plan.TalkingPointIndex, plan.ResumeMarker, room.AgentTurnGeneration, Now);
        if (!plan.MayNarrate || !await AudienceReadyAsync(room, plan.Snapshot.Stage.Version, ct))
        {
            room.PauseAgent(owner, work.Generation, plan.ReasonCode ?? "stage_not_ready",
                "Autonomous narration paused because the new slide is not rendered for every required participant.");
            floor.PauseAt(0, room.AgentTurnGeneration, Now);
            await db.SaveChangesAsync(ct);
            return;
        }

        await EnsureConsentAsync(room, ct);
        var revision = await SalesRoomNarrationSelection.Current(db, companyId, completed.SessionId, Now).FirstOrDefaultAsync(ct);
        var segment = revision is null ? null : await db.SalesNarrationSegments.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == companyId && x.RevisionId == revision.Id &&
            x.SlideNumber == plan.Snapshot.Stage.SlideNumber).OrderBy(x => x.TalkingPoint).FirstOrDefaultAsync(ct);
        if (revision is null || segment is null)
        {
            room.PauseAgent(owner, work.Generation, "narration_release_missing",
                "Autonomous narration paused because the new slide has no approved narration.");
            floor.PauseAt(0, room.AgentTurnGeneration, Now);
            await db.SaveChangesAsync(ct);
            return;
        }
        var commandId = StableTurnId(room.Id, floor.HostParticipantId, "autonomous-slide",
            floor.ResponseGeneration, new DateTimeOffset(Now, TimeSpan.Zero));
        db.SalesRoomAgentSpeech.Add(new SalesRoomAgentSpeech(Guid.NewGuid(), companyId, roomId,
            completed.SessionId, commandId, agentId, work.Generation, room.AgentTurnGeneration,
            SalesRoomAgentSpeechKinds.Narration, room.OrganizerUserId, Now, revision.Id, segment.Id,
            responseGeneration: floor.ResponseGeneration));
        await db.SaveChangesAsync(ct);
        await floorEvents.AllowPlaybackAsync(companyId, completed.SessionId,
            new(roomId, floor.ResponseGeneration), ct);
    }

    private async Task HandleTranscriptAsync(SalesBrowserRoom room, SalesRoomParticipant participant,
        SalesRoomDetectedUtterance utterance, string transcript, bool interruptedAgent, bool transcriptRetained,
        ISalesRoomMediaConnection media, SalesRoomAgentWorkItem work, ISalesRoomFloorEventPublisher publisher,
        ISalesMeetingQuestionAnsweringService answering, CancellationToken ct)
    {
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
        if (floor is null) return;
        var agentName = room.AgentId is Guid agentId
            ? await db.Agents.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId && x.Id == agentId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(ct) ?? "Alex"
            : "Alex";
        var explicitlyAddressed = IsAddressedQuestion(transcript, agentName);
        var connectedHumans = 0;
        if (interruptedAgent && !explicitlyAddressed && !utterance.Overlapped)
        {
            var admitted = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().Where(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                    x.State == SalesRoomParticipantStates.Admitted)
                .Select(x => x.Id).ToListAsync(ct);
            connectedHumans = CountConnectedHumans(admitted, participant.Id, media.IsParticipantConnected);
        }
        var addressed = explicitlyAddressed || ShouldTreatInterruptedSpeechAsAddressedQuestion(
            transcript, interruptedAgent, connectedHumans, utterance.Overlapped);
        if (!addressed && !utterance.Overlapped)
        {
            logger.LogInformation(
                "Browser room transcript did not enter question routing. CompanyId={CompanyId} RoomId={RoomId} ParticipantId={ParticipantId} InterruptedAgent={InterruptedAgent} ExplicitlyAddressed={ExplicitlyAddressed} ConnectedHumans={ConnectedHumans}.",
                room.CompanyId, room.Id, participant.Id, interruptedAgent, explicitlyAddressed, connectedHumans);
            return;
        }
        if (addressed && !transcriptRetained)
        {
            logger.LogInformation(
                "Browser room agent withheld a detected question because transcript retention was not allowed. CompanyId={CompanyId} RoomId={RoomId} ParticipantId={ParticipantId}.",
                room.CompanyId, room.Id, participant.Id);
            if (room.AgentLeaseOwnerId is Guid pauseOwner)
                room.PauseAgent(pauseOwner, work.Generation, "transcript_retention_required",
                    "The agent heard a question but cannot create a grounded answer because this participant has not allowed transcript retention. Enable Allow my transcript to be retained and ask again, or use the typed question control.",
                    room.AgentVoiceHealth);
            floor.PauseAt(0, room.AgentTurnGeneration, Now);
            await db.SaveChangesAsync(ct);
            return;
        }
        Guid? questionId = null;
        var commandId = StableTurnId(room.Id, participant.Id, utterance.TrackId, utterance.TrackGeneration, utterance.StartedAt);
        if (addressed && !utterance.Overlapped && transcriptRetained && room.AgentId is Guid selectedAgent &&
            room.MeetingSessionId is Guid sessionId)
        {
            var sequence = (await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.CompanyId == room.CompanyId && x.SessionId == sessionId).MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
            var answer = await answering.AskAsync(room.CompanyId, room.OrganizerUserId, sessionId,
                new(commandId, sequence, selectedAgent, transcript.Trim(), "customer", participant.DisplayName, "browser_room"),
                commandId.ToString("N"), ct);
            questionId = answer?.Id;
            db.ChangeTracker.Clear();
            room = await RoomAsync(work, ct);
            floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
        }
        floor.ProposeTurn(participant.Id, participant.Generation, addressed, utterance.Overlapped, questionId, Now);
        await db.SaveChangesAsync(ct);
        if (floor.PendingTurnState != SalesRoomPendingTurnStates.Authorized || questionId is null ||
            room.AgentId is not Guid agent || room.MeetingSessionId is not Guid meetingId ||
            room.AgentLeaseOwnerId is not Guid owner) return;
        var question = await db.SalesMeetingQuestions.IgnoreQueryFilters().Include(x => x.Evidence).SingleAsync(x =>
            x.CompanyId == room.CompanyId && x.Id == questionId, ct);
        if (question.Status != SalesMeetingQuestionStatus.Completed || question.Evidence.Count == 0) return;
        if (question.Visibility != SalesMeetingAnswerVisibility.ApprovedForStage)
            question.ApproveForStage(room.OrganizerUserId, question.ConcurrencyVersion, Now);
        room.ResumeAgent(owner, work.Generation);
        floor.AuthorizeAgentResponse(floor.HostParticipantId, room.AgentTurnGeneration, Now);
        db.SalesRoomAgentSpeech.Add(new SalesRoomAgentSpeech(Guid.NewGuid(), room.CompanyId, room.Id, meetingId,
            commandId, agent, work.Generation, room.AgentTurnGeneration, SalesRoomAgentSpeechKinds.Answer,
            room.OrganizerUserId, Now, questionId: questionId, responseGeneration: floor.ResponseGeneration));
        await db.SaveChangesAsync(ct);
        await publisher.AllowPlaybackAsync(room.CompanyId, meetingId, new(room.Id, floor.ResponseGeneration), ct);
    }

    internal static bool IsAddressedQuestion(string text, string agentName)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = " " + text.Trim().ToLowerInvariant() + " ";
        var firstName = agentName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        var addressed = !string.IsNullOrWhiteSpace(firstName) &&
                            Regex.IsMatch(value, $@"\b{Regex.Escape(firstName)}\b", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(value, @"\balex\b|\bsales agent\b|\bsäljagent\b", RegexOptions.CultureInvariant);
        return addressed && IsQuestion(value);
    }

    internal static bool ShouldTreatInterruptedSpeechAsAddressedQuestion(string text, bool interruptedAgent,
        int connectedHumanCount, bool overlapped)
    {
        if (!interruptedAgent || connectedHumanCount != 1 || overlapped || string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = " " + text.Trim().ToLowerInvariant() + " ";
        if (IsQuestion(normalized)) return true;

        // Realtime transcription can preserve the words while losing interrogative grammar
        // (for example, "What do the finance agents do?" becoming "Find us agent two").
        // A substantive interruption from the room's only human is still an addressed turn.
        var words = Regex.Matches(normalized, @"[\p{L}\p{N}']+")
            .Select(match => match.Value).ToArray();
        if (words.Length < 3 || words.Sum(word => word.Length) < 10) return false;

        var phrase = string.Join(' ', words);
        return !Regex.IsMatch(phrase,
            @"^(thanks?|thank you|okay|ok|yes|no|great|fine|continue|go on|stop|wait|hold on|tack|okej|ja|nej|bra|fortsätt|stopp|vänta)\b",
            RegexOptions.CultureInvariant);
    }

    internal static int CountConnectedHumans(IEnumerable<Guid> admittedParticipantIds, Guid activeSpeakerId,
        Func<Guid, bool> mediaConnected) =>
        admittedParticipantIds.Distinct().Count(id => id == activeSpeakerId || mediaConnected(id));

    private static bool IsQuestion(string value) =>
        value.Contains('?') || new[] { " how ", " what ", " when ", " where ", " why ", " can ", " could ",
            " does ", " do ", " is ", " are ", " hur ", " vad ", " när ", " var ", " varför ", " kan ", " är " }
            .Any(value.Contains);

    private static Guid StableTurnId(Guid roomId, Guid participantId, string trackId, long generation, DateTimeOffset started)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{roomId:N}:{participantId:N}:{trackId}:{generation}:{started.UtcTicks}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private async Task<SalesMeetingQuestion> ReleasedQuestionAsync(SalesBrowserRoom room, SalesRoomAgentSpeech item, CancellationToken ct)
    {
        var question = await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().Include(x => x.Evidence).SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.SessionId == item.SessionId && x.Id == item.QuestionId && x.AgentId == item.AgentId, ct);
        if (question is null || question.Status != SalesMeetingQuestionStatus.Completed ||
            question.Visibility != SalesMeetingAnswerVisibility.ApprovedForStage || question.StageApprovedUtc is null ||
            string.IsNullOrWhiteSpace(question.AnswerText) || question.Evidence.Count == 0)
            throw new WithheldSpeech("answer_not_released", "The answer is no longer approved for customer-visible speech.");
        return question;
    }

    private async Task<SalesBrowserRoom> RoomAsync(SalesRoomAgentWorkItem work, CancellationToken ct) =>
        await db.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == work.CompanyId && x.Id == work.RoomId, ct);
    private async Task<List<SalesRoomParticipant>> ConsentedParticipantsAsync(SalesBrowserRoom room, CancellationToken ct)
    {
        var people = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
            x.RoomId == room.Id && x.State == SalesRoomParticipantStates.Admitted).ToListAsync(ct);
        if (people.Count == 0 || people.Any(x => !x.AiProcessingAllowed))
            throw new SalesRoomAgentException(SalesRoomAgentProblemCodes.ConsentRequired, "Every admitted participant must consent before AI starts.");
        return people;
    }
    private async Task<bool> HasAllConsentAsync(SalesBrowserRoom room, CancellationToken ct)
    {
        var counts = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
            x.RoomId == room.Id && x.State == SalesRoomParticipantStates.Admitted)
            .GroupBy(_ => 1).Select(x => new { Total = x.Count(), Consented = x.Count(p => p.AiProcessingAllowed) }).SingleOrDefaultAsync(ct);
        return counts is { Total: > 0 } && counts.Total == counts.Consented;
    }
    private async Task<T> RunStartupWithLeaseRenewalAsync<T>(SalesRoomAgentWorkItem work,
        Func<CancellationToken, Task<T>> startup, CancellationToken ct)
    {
        var pending = startup(ct);
        while (!pending.IsCompleted)
        {
            var delay = Task.Delay(TimeSpan.FromSeconds(Options.RenewalSeconds), ct);
            if (await Task.WhenAny(pending, delay) == pending) break;

            await using var scope = captureScopes.CreateAsyncScope();
            var leaseDb = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var now = Now;
            var room = await leaseDb.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == work.CompanyId && x.Id == work.RoomId, ct);
            if (!room.RenewAgentLease(work.LeaseOwnerId, work.Generation,
                    now.AddSeconds(Options.LeaseSeconds), now))
                throw new InvalidOperationException("The browser-room agent lease was lost during startup.");
            await leaseDb.SaveChangesAsync(ct);
        }

        return await pending;
    }
    private async Task EnsureConsentAsync(SalesBrowserRoom room, CancellationToken ct)
    { if (!await HasAllConsentAsync(room, ct)) throw new WithheldSpeech("consent_required", "AI paused because participant consent changed."); }
    private async Task<bool> AudienceReadyAsync(SalesBrowserRoom room, long presentationVersion, CancellationToken ct)
    {
        var rows = await db.SalesRoomPresentationAudience.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
            x.PresentationVersion == presentationVersion).ToListAsync(ct);
        var ready = rows.Count > 0 && rows.All(x => x.State is SalesRoomPresentationAudienceStates.Rendered or
            SalesRoomPresentationAudienceStates.Overridden);
        if (!ready) logger.LogWarning("Audience was not ready for room {RoomId}, presentation version {PresentationVersion}. Rows: {Rows}.",
            room.Id, presentationVersion, JsonSerializer.Serialize(rows.Select(x => new { x.PresentationVersion, x.State, x.ParticipantId })));
        return ready;
    }
    private async Task PauseAsync(SalesRoomAgentWorkItem work, string code)
    {
        try
        {
            db.ChangeTracker.Clear(); var room = await RoomAsync(work, CancellationToken.None);
            if (room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now))
            {
                room.PauseAgent(work.LeaseOwnerId, work.Generation, code,
                    "Room AI paused safely. The human call, manual slides, and typed questions remain available.");
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not persist the safe pause for browser room {RoomId}.", work.RoomId); }
    }

    private bool AdmissionBlocked() => !configured.CurrentValue.Enabled || configured.CurrentValue.EmergencyDisabled ||
        configured.CurrentValue.DrainEnabled || !lifecycle.CurrentValue.Enabled || lifecycle.CurrentValue.DrainEnabled ||
        SalesRoomOperationsPolicy.ConfigurationProblem(configured.CurrentValue, Now) is not null;

    private async Task StopForPolicyAsync(SalesBrowserRoom room, SalesRoomAgentWorkItem work, string code)
    {
        if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
        room.StopAgent(code, code switch
        {
            "emergency_disabled" => "Room AI stopped immediately because the emergency disable was activated.",
            "duration_limit" => "Room AI stopped at the configured call-duration limit.",
            "spend_limit" => "Room AI stopped at the configured spend limit.",
            _ => "Room AI stopped during an operational drain. Human calling and typed controls remain available."
        }, Now);
        var pending = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().Where(x => x.CompanyId == room.CompanyId &&
            x.RoomId == room.Id && (x.Status == SalesRoomAgentSpeechStates.Queued ||
            x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(CancellationToken.None);
        foreach (var item in pending)
            item.Fail(SalesRoomAgentSpeechStates.Interrupted, code,
                "Room AI stopped before this speech completed; it will not be replayed.", Now);
        SalesRoomBenchmarkTelemetry.RecordLifecycle(code);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<decimal> CompanyMonthlySpendAsync(Guid companyId, CancellationToken ct)
    {
        var month = new DateTime(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usage = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.CompanyId == companyId && (x.CreatedUtc >= month || x.AgentStartedUtc >= month))
            .Select(x => new { x.AgentProviderBilledAudioMilliseconds, x.AgentInputTokens, x.AgentOutputTokens })
            .ToListAsync(ct);
        var spend = usage.Sum(x => SalesRoomOperationsPolicy.EstimatedSpend(
            x.AgentProviderBilledAudioMilliseconds, x.AgentInputTokens, x.AgentOutputTokens, Options));
        SalesRoomBenchmarkTelemetry.RecordEstimatedSpend(spend);
        return spend;
    }
    private static IEnumerable<ReadOnlyMemory<byte>> PcmChunks(ReadOnlyMemory<short> samples)
    {
        const int chunkSamples = 48_000;
        for (var offset = 0; offset < samples.Length; offset += chunkSamples)
            yield return MemoryMarshal.AsBytes(samples.Span.Slice(offset, Math.Min(chunkSamples, samples.Length - offset))).ToArray();
    }
    private static async Task AlignOutputGenerationAsync(ISalesRoomMediaConnection media, long generation, CancellationToken ct)
    { while (media.GetStatistics().TurnGeneration < generation) await media.CancelSpeechAsync(ct); }
    private static Microsoft.Extensions.Options.IOptions<SalesRoomAgentOptions> OptionsWrapper(SalesRoomAgentOptions value) =>
        Microsoft.Extensions.Options.Options.Create(value);
    private static async Task AwaitQuietly(params Task[] tasks)
    { try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private enum RuntimeEventKind { SpeechStarted, Utterance, ProviderEvent, DetectorFailure, ProviderFailure, UnexpectedProviderAudio }
    private sealed record RuntimeEvent(RuntimeEventKind Kind, SalesRoomDetectedUtterance? Utterance = null,
        string? ProviderJson = null, string? ProviderEventId = null, long ProviderSequence = 0,
        Guid? ParticipantId = null, bool InterruptedAgent = false);
    private sealed record UtteranceContext(SalesRoomDetectedUtterance Value, long ConsentVersion, bool Retain,
        bool InterruptedAgent);
    private sealed class WithheldSpeech(string code, string message) : Exception(message) { public string Code { get; } = code; }
}
