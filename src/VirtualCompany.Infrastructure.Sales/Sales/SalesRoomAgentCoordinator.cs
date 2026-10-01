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
using VirtualCompany.Domain.Agents;
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
                var priorFailure = PreservePausedFailure(room, stopAll);
                if (priorFailure) code = room.AgentLastErrorCode!;
                room.StopAgent(code,
                    priorFailure ? room.AgentLastErrorSummary ?? "Room AI paused after an error. Restart the agent to retry."
                    : stopAll ? "Room AI stopped because an operator disabled or drained the service."
                        : "The agent stopped after its worker lease expired. The host must explicitly start it again.", now);
                var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                floor?.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, now);
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

    internal static bool PreservePausedFailure(SalesBrowserRoom room, bool stopAll) =>
        !stopAll && room.AgentHealth == SalesRoomAgentHealthStates.Paused &&
        !string.IsNullOrWhiteSpace(room.AgentLastErrorCode);

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
    internal SalesRoomConversationTiming Timing { get; } = new();
    private readonly object gate = new();
    private readonly SemaphoreSlim outputOwner = new(1, 1);
    private ISalesRoomMediaConnection? media;
    private CancellationTokenSource? response;
    private long interruptionVersion;
    private long? confirmedResponse;
    private (long From, long To)? confirmedHandoff;
    public long InterruptionVersion { get { lock (gate) return interruptionVersion; } }
    public void Attach(ISalesRoomMediaConnection connection) { lock (gate) media = connection; }
    public void Detach(ISalesRoomMediaConnection connection) { lock (gate) { if (ReferenceEquals(media, connection)) media = null; } }
    public CancellationTokenSource BeginResponse(CancellationToken lifetime, long? expectedInterruptionVersion = null)
    {
        if (!outputOwner.Wait(0)) throw new InvalidOperationException("Output is still owned by an earlier speech operation.");
        return ClaimResponse(lifetime, expectedInterruptionVersion);
    }
    public async Task<CancellationTokenSource> BeginResponseAsync(CancellationToken lifetime, long? expectedInterruptionVersion = null)
    {
        await outputOwner.WaitAsync(lifetime);
        return ClaimResponse(lifetime, expectedInterruptionVersion);
    }
    private CancellationTokenSource ClaimResponse(CancellationToken lifetime, long? expectedInterruptionVersion)
    {
        lock (gate)
        {
            confirmedResponse = null; confirmedHandoff = null;
            response = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            if (expectedInterruptionVersion.HasValue && expectedInterruptionVersion.Value != interruptionVersion)
                response.Cancel();
            return response;
        }
    }
    public void EndResponse(CancellationTokenSource value)
    {
        lock (gate)
        {
            if (!ReferenceEquals(response, value)) return;
            response = null; value.Dispose(); outputOwner.Release();
        }
    }
    public bool CancelResponse(long? confirmedResponseGeneration = null)
    {
        lock (gate)
        {
            interruptionVersion++;
            confirmedResponse = confirmedResponseGeneration;
            confirmedHandoff = null;
            var interrupted = response is { IsCancellationRequested: false };
            response?.Cancel();
            return interrupted;
        }
    }
    public void RecordConfirmedHandoff(CancellationTokenSource cancelled, long from, long to)
    {
        lock (gate)
            if (ReferenceEquals(response, cancelled) && confirmedResponse == from && cancelled.IsCancellationRequested)
                confirmedHandoff = (from, to);
    }
    public bool AcceptsConfirmedHandoff(long from, SalesRoomFloor floor)
    {
        lock (gate)
            return confirmedHandoff == (from, floor.ResponseGeneration) && floor.State == SalesRoomFloorStates.Paused;
    }
    public async Task TakeOverAsync(CancellationToken ct)
    {
        Task? flush;
        lock (gate)
        {
            interruptionVersion++; confirmedResponse = null; confirmedHandoff = null; response?.Cancel();
            // Begin the generation fence under the same lock as output admission. A late
            // takeover must never capture an old owner and then flush a newly admitted one.
            flush = media?.CancelSpeechAsync(ct);
        }
        if (flush is not null) await flush;
    }
}

internal sealed class SalesRoomTranscriptCorrelation<TContext> where TContext : class
{
    private readonly Queue<(TContext Context, DateTime SubmittedUtc)> pending = new();
    private readonly Dictionary<string, (TContext Context, DateTime SubmittedUtc)> committed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> earlyCompletions = new(StringComparer.Ordinal);
    private readonly HashSet<string> committedIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedIds = new(StringComparer.Ordinal);

    public int PendingCount => pending.Count + committed.Count + earlyCompletions.Count;
    public int SeenCount => committedIds.Count + completedIds.Count;

    public void Enqueue(TContext context, DateTime? submittedUtc = null) =>
        pending.Enqueue((context, submittedUtc ?? DateTime.UtcNow));

    public bool HasTimedOut(DateTime now, TimeSpan timeout) =>
        pending.TryPeek(out var oldest) && now - oldest.SubmittedUtc >= timeout ||
        committed.Values.Any(value => now - value.SubmittedUtc >= timeout);

    public bool Commit(string itemId, out TContext? context, out string? text)
    {
        context = null; text = null;
        if (string.IsNullOrWhiteSpace(itemId) || !committedIds.Add(itemId) || !pending.TryDequeue(out var submitted))
            return false;
        if (earlyCompletions.Remove(itemId, out var completedText))
        {
            context = submitted.Context; text = completedText; return true;
        }
        committed[itemId] = submitted;
        return false;
    }

    public bool Complete(string itemId, string text, out TContext? context, out string? completedText)
    {
        context = null; completedText = null;
        if (string.IsNullOrWhiteSpace(itemId) || !completedIds.Add(itemId))
            return false;
        if (committed.Remove(itemId, out var submitted))
        {
            context = submitted.Context; completedText = text; return true;
        }
        earlyCompletions[itemId] = text;
        return false;
    }
}
internal sealed partial class SalesRoomAgentWorker(
    VirtualCompanyDbContext db,
    IServiceScopeFactory captureScopes,
    ICompanyExecutionScopeFactory executionScopes,
    ISalesRoomMediaTransport mediaTransport,
    IRealtimeAgentPcmSessionGateway pcm,
    IRealtimeAgentSessionGateway realtime,
    IRealtimeAgentConversationGateway realtimeConversation,
    ISalesNarrationService narration,
    IApprovedSpeechGateway speech,
    ISalesRoomConversationReasoner conversationReasoner,
    ISalesMeetingPresentationConductor conductor,
    ISalesMeetingQuestionAnsweringService questions,
    ISalesRoomFloorEventPublisher floorEvents,
    IOptionsMonitor<SalesRoomAgentOptions> configured,
    IOptionsMonitor<SalesRoomLifecycleOptions> lifecycle,
    ISpeechFrameClassifierFactory speechClassifiers,
    TimeProvider clock,
    ILogger<SalesRoomAgentWorker> logger)
{
    private const string QuestionAcknowledgement = "I heard your question. Let me check the approved sources. ";
    private SalesRoomAgentOptions Options => configured.CurrentValue;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task RunAsync(SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, CancellationToken ct)
    {
        var recoveries = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(work, control, ct, recovering: recoveries > 0);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (ProviderSessionRecovery)
            {
                if (recoveries >= Options.MaximumProviderSessionRecoveries)
                {
                    await PauseAsync(work, "provider_recovery_exhausted");
                    return;
                }
                recoveries++;
                try
                {
                    if (!await PrepareProviderRecoveryAsync(work, ct))
                    { await PauseAsync(work, "provider_recovery_denied"); return; }
                    SalesRoomBenchmarkTelemetry.RecordOwnership("provider_session_recovery");
                    await Task.Delay(TimeSpan.FromSeconds(Options.ProviderRecoveryBackoffSeconds), ct);
                    // Recheck after backoff too. A host stop, rollback or consent change wins.
                    if (!await ProviderRecoveryAllowedAsync(work, ct))
                    { await PauseAsync(work, "provider_recovery_denied"); return; }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    // Concurrent authority changes and unavailable permission checks cannot
                    // become an unbounded reconnect loop or publish the retired turn.
                    await PauseAsync(work, "provider_recovery_denied");
                    return;
                }
            }
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
                    SalesRoomAgentException agentError => agentError.Code,
                    _ => "agent_worker_failed"
                };
                logger.LogWarning(
                    "Browser room agent paused safely for room {RoomId}; exception type {ExceptionType}, code {FailureCode}.",
                    work.RoomId, ex.GetType().Name, failureCode);
                logger.LogDebug("MeetingTrace Stage=worker_failure RoomId={RoomId} FailureType={FailureType} Code={Code} Stack={Stack}",
                    work.RoomId, ex.GetType().Name, failureCode, ex.StackTrace);
                await PauseAsync(work, failureCode);
                return;
            }
        }
    }

    private async Task RunOnceAsync(SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, CancellationToken ct,
        bool recovering = false)
    {
        using var companyScope = executionScopes.BeginScope(work.CompanyId);
        ISalesRoomMediaConnection? media = null; string? providerSession = null;
        IDisposable? benchmarkSession = null;
        Task? activeSpeech = null;
        Task? groundedWork = null;
        var inputsAfterLookup = new Queue<(UtteranceContext Context, string Text, Guid? Retained,
            string? RoutedAction, SalesRoomTurnReadiness? Readiness)>();
        CancellationTokenSource? runtimeStop = null;
        Task? inputTask = null, providerTask = null;
        AgentConversationSession? conversationSession = null;
        DateTime? awaitingContextualReplyUntil = null;
        (SalesRoomConversationTurn Turn, UtteranceContext Input, string Text, bool Retained, DateTime? ReplyDeadline)? pendingTool = null;
        Guid? toolResultAwaitingResponseEnd = null;
        // Each event may carry a full bounded PCM utterance; keep queued audio bounded too.
        var events = Channel.CreateBounded<RuntimeEvent>(new BoundedChannelOptions(64)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
        try
        {
            var room = await RoomAsync(work, ct);
            if (recovering && !await ProviderRecoveryAllowedAsync(work, ct))
            { await PauseAsync(work, "provider_recovery_denied"); return; }
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
            var conversationProfile = Options.HybridConversationEnabled;
            var semanticProfile = conversationProfile && Options.SemanticConversationInputEnabled;
            logger.LogInformation("MeetingTrace Stage=session_start CompanyId={CompanyId} RoomId={RoomId} AgentId={AgentId} OwnerGeneration={OwnerGeneration} Hybrid={Hybrid} LiveDialogue={LiveDialogue}",
                work.CompanyId, work.RoomId, room.AgentId, work.Generation, conversationProfile, semanticProfile);
            var semanticEagerness = Options.SemanticVadEagerness;
            var connectStarted = Stopwatch.GetTimestamp();
            var provider = await RunWithLeaseRenewalAsync(work, async startupToken =>
            {
                media = await mediaTransport.ConnectAgentAsync(new(room.CompanyId, room.Id),
                    new(agentId, true, work.Generation, new DateTimeOffset(DateTime.SpecifyKind(room.ExpiresUtc, DateTimeKind.Utc))), humans, startupToken);
                SalesRoomBenchmarkTelemetry.RecordLatency("voice_connect", Stopwatch.GetElapsedTime(connectStarted));
                control.Attach(media);
                await AlignOutputGenerationAsync(media, room.AgentTurnGeneration, startupToken);
                return await pcm.CreatePcmSessionAsync(new(room.CompanyId, room.OrganizerUserId, agentId,
                    conversationProfile ? "sales_browser_room_conversation" : "sales_browser_room_segmented_transcription",
                    (semanticProfile ? SalesRoomDialoguePolicy.Instructions : conversationProfile
                        ? "Transcribe each explicitly committed utterance exactly. Never create a response on your own. " +
                          "Only respond to an explicit application response.create. Keep conversational output concise; " +
                          "do not invent product facts, commitments, or tool results. For each requested reply, choose exactly " +
                          "one available tool with empty arguments, based only on the latest confirmed turn and actually " +
                          "played follow-up. A new question takes priority over continuation. An ambiguous yes after " +
                          "multiple questions means wait. Never speak or announce continuation before backend acceptance. "
                        : "Transcribe each explicitly committed utterance exactly and preserve interrogative wording. " +
                          "Do not answer, speak, call tools, or infer speaker identity. ") +
                    "Meeting vocabulary can include Virtual Company, Alex, finance agent, sales agent, marketing agent, " +
                    "support agent, and questions about what those agents can do.",
                    semanticProfile ? SalesRoomDialoguePolicy.Tools : conversationProfile ? SalesRoomConversationTools.Definitions : [], TimeSpan.FromMinutes(Math.Min(semanticProfile ? Options.ProviderSessionMinutes : Options.MaximumSessionMinutes,
                    Math.Max(1, (room.ExpiresUtc - Now).TotalMinutes))), work.RoomId.ToString("N"), !semanticProfile,
                    ConversationProfile: conversationProfile, SemanticVadEagerness: semanticProfile ? semanticEagerness : null), startupToken);
            }, ct);
            providerSession = provider.ProviderSessionId;
            db.ChangeTracker.Clear();
            room = await RoomAsync(work, ct);
            if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
            if (recovering && !await ProviderRecoveryAllowedAsync(work, ct))
            { await PauseAsync(work, "provider_recovery_denied"); return; }
            room = await RoomAsync(work, ct);
            conversationSession = new(room.CompanyId, agentId, room.Id, sessionId, work.LeaseOwnerId, work.Generation);
            room.AgentReady(work.LeaseOwnerId, work.Generation);
            await db.SaveChangesAsync(ct);
            benchmarkSession = SalesRoomBenchmarkTelemetry.BeginSession();

            var detector = new SpeechAwareLocalVoiceActivityDetector(speechClassifiers);
            var segmenter = new SalesRoomVoiceActivitySegmenter(Options, detector);
            using var semantic = semanticProfile ? new SalesRoomSemanticAudioInput(Options, detector) : null;
            runtimeStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            inputTask = ReadInputAsync(media!, segmenter, events.Writer, runtimeStop.Token, semanticProfile);
            providerTask = ReadProviderAsync(providerSession, events.Writer, runtimeStop.Token);
            var transcriptCorrelation = new SalesRoomTranscriptCorrelation<UtteranceContext>();
            var turnBuffer = new AgentSpeechTurnBuffer();
            (UtteranceContext Context, Guid RetainedId, SalesRoomTurnReadiness Decision)? deferredCompleteTurn = null;
            (SalesRoomDialogueTurn Turn, UtteranceContext Input, string Text, Guid Retained, IReadOnlyList<string> Tools)? pendingDialogue = null;
            SalesRoomConversationToolResult? playbackToolResult = null;
            var candidateSnapshots = new Dictionary<(Guid ParticipantId, string TrackId, long TrackGeneration), CandidateSnapshot>();
            var lastReceived = 0L; var lastDetected = 0L; var lastForwarded = 0L;
            var pendingProviderAudio = 0L; var pendingInputTokens = 0; var pendingOutputTokens = 0;
            var renewAt = Now.AddSeconds(Options.RenewalSeconds);
            var organizerMissing = false;
            var nextInputDiagnostics = Now.AddSeconds(5);
            var maximumInputFrameAgeMs = 0d;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(200, ct);
                db.ChangeTracker.Clear();
                room = await RoomAsync(work, ct);
                if (groundedWork is { IsCompleted: true })
                {
                    await groundedWork;
                    groundedWork = null;
                    while (groundedWork is null && inputsAfterLookup.TryDequeue(out var waiting))
                    {
                        await ProcessTranscriptAsync(waiting.Context, waiting.Text, waiting.Retained,
                            waiting.RoutedAction, waiting.Readiness);
                    }
                    db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                }
                if (conversationProfile != Options.HybridConversationEnabled ||
                    semanticProfile != (Options.HybridConversationEnabled && Options.SemanticConversationInputEnabled) ||
                    semanticProfile && semanticEagerness != Options.SemanticVadEagerness)
                {
                    await CancelActiveSpeechAsync();
                    await StopForPolicyAsync(room, work, Options.HybridConversationEnabled
                        ? "conversation_restart_required" : "conversation_disabled",
                        "Realtime conversation settings changed. Restart the agent to apply them.");
                    break;
                }
                if (activeSpeech is { IsCompleted: true })
                {
                    await activeSpeech;
                    activeSpeech = null;
                    db.ChangeTracker.Clear();
                    room = await RoomAsync(work, ct);
                    if (Options.HybridConversationEnabled && providerSession is not null)
                    {
                        var playedAnswers = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking()
                            .Where(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                                x.AgentGeneration == work.Generation &&
                                (x.Kind == SalesRoomAgentSpeechKinds.Answer || x.Kind == SalesRoomAgentSpeechKinds.Bridge) &&
                                x.Status == SalesRoomAgentSpeechStates.Spoken && x.CompletedUtc > Now.AddMinutes(-2))
                            .OrderByDescending(x => x.CompletedUtc).ThenByDescending(x => x.Kind).Take(8).ToListAsync(ct);
                        foreach (var played in playedAnswers.OrderBy(x => x.CompletedUtc).ThenBy(x => x.Kind))
                        {
                            if (conversationSession.HasPlayed(played.Id) || string.IsNullOrWhiteSpace(played.ReleasedText)) continue;
                            try
                            {
                                if (played.Kind == SalesRoomAgentSpeechKinds.Answer)
                                {
                                    if (!conversationSession.HasHeard(played.CommandId)) continue;
                                    await realtimeConversation.RecordPlayedResponseAsync(providerSession, played.CommandId,
                                        played.ReleasedText[..Math.Min(2000, played.ReleasedText.Length)], ct);
                                }
                                else if (ReadBridgeEvidence(played)?.ReplyTurnId is Guid replyTurn)
                                {
                                    if (!conversationSession.HasHeard(replyTurn)) continue;
                                    await realtimeConversation.RecordPlayedResponseAsync(providerSession, replyTurn,
                                        played.ReleasedText[..Math.Min(2000, played.ReleasedText.Length)], ct);
                                }
                                else if (played.QuestionId is Guid answeredId)
                                {
                                    var originatingTurn = await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking()
                                        .Where(x => x.CompanyId == room.CompanyId && x.Id == answeredId)
                                        .Select(x => x.ClientQuestionId).SingleOrDefaultAsync(ct);
                                    if (!conversationSession.HasHeard(originatingTurn)) continue;
                                    await realtimeConversation.RecordPlayedFollowUpAsync(providerSession, originatingTurn,
                                        played.Id, played.ReleasedText[..Math.Min(2000, played.ReleasedText.Length)], ct);
                                }
                            }
                            catch (RealtimeAgentEventException ex)
                            {
                                // Provider context is auxiliary; room speech already completed. A
                                // rolled-over/trimmed context must not replay or stop the meeting.
                                logger.LogInformation("Played speech could not enter bounded provider context for room {RoomId}: {Code}.",
                                    room.Id, ex.Code);
                                conversationSession.RecordPlayed(played.Id);
                                continue;
                            }
                            conversationSession.RecordPlayed(played.Id);
                            // Completed playback establishes optional action context. Listening
                            // itself belongs to the session and never depends on this history.
                            {
                                var completedFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking()
                                    .SingleAsync(x => x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                                var completedAuthority = await SalesRoomConversationPolicy.LoadAsync(db, Options,
                                    room.CompanyId, room.Id, completedFloor.HostParticipantId, Now, ct);
                                if (completedAuthority is not null && completedFloor.State == SalesRoomFloorStates.Host &&
                                    completedAuthority.Binding.TurnGeneration == played.TurnGeneration &&
                                    completedAuthority.Binding.ResponseGeneration == played.ResponseGeneration)
                                {
                                    awaitingContextualReplyUntil = played.CompletedUtc!.Value.AddSeconds(45);
                                    if (played.QuestionId is Guid timedQuestion)
                                        control.Timing.Complete("answer_complete_to_listening", timedQuestion);
                                }
                            }
                        }
                    }
                }
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
                    room.State != SalesBrowserRoomStates.Live) break;
                if (room.ExpiresUtc <= Now || provider.ExpiresUtc <= Now)
                {
                    await CancelActiveSpeechAsync();
                    if (room.ExpiresUtc > Now && semanticProfile)
                    { await SaveRecoveryUsageAsync(); throw new ProviderSessionRecovery(); }
                    await StopForPolicyAsync(room, work, room.ExpiresUtc <= Now ? "room_expired" : "session_expired");
                    break;
                }
                if (AdmissionBlocked())
                {
                    await CancelActiveSpeechAsync();
                    await StopForPolicyAsync(room, work,
                        configured.CurrentValue.EmergencyDisabled ? "emergency_disabled" : "deployment_draining");
                    break;
                }
                if (!await HasAllConsentAsync(room, ct))
                {
                    await CancelActiveSpeechAsync();
                    room.PauseAgent(work.LeaseOwnerId, work.Generation, "consent_required",
                        "AI paused because an admitted participant has not consented. Human audio and manual slides remain available.");
                    await db.SaveChangesAsync(CancellationToken.None);
                    break;
                }
                var hostConnected = media.IsParticipantConnected(organizerParticipantId);
                if (!hostConnected && !organizerMissing && room.AgentStartedUtc <= Now.AddSeconds(-Options.OrganizerDisconnectGraceSeconds))
                {
                    organizerMissing = true;
                    await CancelActiveSpeechAsync();
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

                foreach (var utterance in semantic is null ? segmenter.FlushExpired(DateTimeOffset.UtcNow) : [])
                    if (!events.Writer.TryWrite(new(RuntimeEventKind.Utterance, utterance)))
                        throw new SalesRoomAccessException("transcription_backpressure");
                foreach (var stale in candidateSnapshots.Where(x => Now - x.Value.StartedUtc > TimeSpan.FromSeconds(45))
                    .Select(x => x.Key).ToArray()) candidateSnapshots.Remove(stale);

                if (transcriptCorrelation.HasTimedOut(Now, TimeSpan.FromSeconds(12)))
                {
                    await CancelActiveSpeechAsync();
                    room.PauseAgent(work.LeaseOwnerId, work.Generation, "transcription_timeout",
                        "Speech confirmation did not complete. AI is paused; use typed questions while the human call continues.");
                    await db.SaveChangesAsync(CancellationToken.None);
                    break;
                }

                while (events.Reader.TryRead(out var runtime))
                {
                    if (semantic is not null && runtime.Frame is { } frame)
                    {
                        maximumInputFrameAgeMs = Math.Max(maximumInputFrameAgeMs,
                            Math.Max(0, (clock.GetUtcNow() - frame.ReceivedAt).TotalMilliseconds));
                        var speaker = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == frame.ParticipantId, ct);
                        if (!CanReceiveSpeech(speaker, media.IsParticipantConnected(frame.ParticipantId), Now) ||
                            !speaker!.TranscriptRetentionAllowed)
                        {
                            if (semantic.Revoke(frame.ParticipantId)) await ClearSemanticBufferAsync();
                            continue; // Typed questions remain available when retention is declined.
                        }
                        var inputFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                        var previousCapture = semantic.ActiveScope;
                        var forwarded = semantic.Push(frame, new(speaker.Id, frame.TrackId, frame.TrackGeneration,
                            speaker.Generation, speaker.Version, speaker.TranscriptRetentionAllowed, room.AgentTurnGeneration,
                            inputFloor.ResponseGeneration, inputFloor.State == SalesRoomFloorStates.Agent));
                        await SendSemanticAudioAsync(forwarded);
                        if (previousCapture is null && semantic.ActiveScope is not null)
                            SalesRoomBenchmarkTelemetry.RecordSemanticInput("capture_started");
                        continue;
                    }
                    if (runtime.Kind is RuntimeEventKind.DetectorFailure or RuntimeEventKind.ProviderFailure)
                    {
                        SalesRoomBenchmarkTelemetry.RecordConversationFailure(runtime.Kind == RuntimeEventKind.ProviderFailure
                            ? "transcription_unavailable" : "vad_failed");
                        await CancelActiveSpeechAsync();
                        if (runtime.Kind == RuntimeEventKind.ProviderFailure && semanticProfile)
                        { await SaveRecoveryUsageAsync(); throw new ProviderSessionRecovery(); }
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
                        await CancelActiveSpeechAsync();
                        room.PauseAgent(work.LeaseOwnerId, work.Generation, "unchecked_provider_audio",
                            "Unexpected provider audio was blocked before publication.");
                        await db.SaveChangesAsync(CancellationToken.None);
                        runtimeStop.Cancel(); await AwaitQuietly(inputTask, providerTask); return;
                    }
                    if (runtime.Kind == RuntimeEventKind.CandidateStarted && runtime.ParticipantId is Guid candidateId &&
                        runtime.TrackId is { } candidateTrack)
                    {
                        var speaker = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == candidateId, ct);
                        if (CanReceiveSpeech(speaker, media.IsParticipantConnected(candidateId), Now))
                        {
                            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                                x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                            if (candidateSnapshots.Count >= 32) throw new SalesRoomAccessException("transcription_backpressure");
                            candidateSnapshots[(candidateId, candidateTrack, runtime.TrackGeneration)] =
                                new(speaker!.Version, speaker.Generation, room.AgentTurnGeneration, floor?.ResponseGeneration,
                                    floor?.State == SalesRoomFloorStates.Agent, Now);
                        }
                        continue;
                    }
                    if (runtime.Utterance is { } utterance)
                    {
                        if (!candidateSnapshots.Remove((utterance.ParticipantId, utterance.TrackId,
                                utterance.TrackGeneration), out var candidate)) continue;
                        var participant = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == utterance.ParticipantId, ct);
                        if (!CanReceiveSpeech(participant, media.IsParticipantConnected(utterance.ParticipantId), Now) ||
                            participant!.Version != candidate.ConsentVersion ||
                            participant.Generation != candidate.ParticipantGeneration ||
                            room.AgentTurnGeneration != candidate.TurnGeneration ||
                            !media.IsParticipantConnected(participant.Id) ||
                            !segmenter.IsCurrentTrack(participant.Id, utterance.TrackId, utterance.TrackGeneration))
                        { segmenter.Clear(utterance.ParticipantId); continue; }
                        if (transcriptCorrelation.PendingCount >= 64 || transcriptCorrelation.SeenCount >= 4096)
                            throw new SalesRoomAccessException("transcription_backpressure");
                        var submittedFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                        if (!MatchesSpeechFloor(submittedFloor, candidate.TurnGeneration, candidate.ResponseGeneration)) continue;
                        foreach (var chunk in PcmChunks(utterance.Samples))
                            await pcm.SendInputAudioAsync(providerSession, chunk, ct);
                        await pcm.SendClientEventAsync(providerSession,
                            JsonSerializer.Serialize(new { event_id = "commit_" + Guid.NewGuid().ToString("N"), type = "input_audio_buffer.commit" }), ct);
                        transcriptCorrelation.Enqueue(new(utterance with { Samples = ReadOnlyMemory<short>.Empty }, candidate.ConsentVersion,
                            participant.TranscriptRetentionAllowed, candidate.ParticipantGeneration, candidate.TurnGeneration,
                            candidate.ResponseGeneration, candidate.BeganDuringPresentation), Now);
                        continue;
                    }
                    if (runtime.ProviderJson is not null)
                    {
                        using var providerPayload = JsonDocument.Parse(runtime.ProviderJson);
                        if (semantic is null && providerPayload.RootElement.TryGetProperty("type", out var eventType) &&
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
                        if (semantic is not null && providerPayload.RootElement.GetProperty("type").GetString() == "response.done" &&
                            providerPayload.RootElement.GetProperty("response").TryGetProperty("status", out var responseStatus) &&
                            responseStatus.GetString() is "failed" or "incomplete")
                        {
                            pendingInputTokens += normalized.InputTokens;
                            pendingOutputTokens += normalized.OutputTokens;
                            if (pendingDialogue is { } failed && runtime.ProviderTurnId == failed.Turn.InputId)
                            {
                                pendingDialogue = null;
                                if (activeSpeech is null && room.AgentTurnGeneration == failed.Turn.Binding.TurnGeneration)
                                {
                                    room.ConversationReplyWithheld(work.LeaseOwnerId, work.Generation);
                                    await db.SaveChangesAsync(ct);
                                }
                            }
                            toolResultAwaitingResponseEnd = null;
                            continue; // A failed proposal is not a failed microphone connection.
                        }
                        if (semantic is not null && normalized.Type is RealtimeAgentEventTypes.ParticipantSpeechStarted or
                            RealtimeAgentEventTypes.ParticipantSpeechStopped or RealtimeAgentEventTypes.InputCommitted or
                            RealtimeAgentEventTypes.ParticipantTranscriptCompleted or RealtimeAgentEventTypes.ParticipantTranscriptFailed)
                        {
                            var completed = semantic.Observe(normalized, Now);
                            logger.LogDebug("MeetingTrace Stage=input_event RoomId={RoomId} EventType={EventType} CorrelatedTurn={CorrelatedTurn} PendingItems={PendingItems} Reason={Reason} CharacterCount={CharacterCount} StartMs={StartMs} EndMs={EndMs}",
                                room.Id, normalized.Type, completed is not null, semantic.PendingCount,
                                semantic.ObservationReason, normalized.Text?.Length ?? 0,
                                normalized.AudioStartMilliseconds, normalized.AudioEndMilliseconds);
                            if (normalized.Type == RealtimeAgentEventTypes.ParticipantTranscriptFailed)
                                SalesRoomBenchmarkTelemetry.RecordConversationFailure("semantic_transcription_failed");
                            if (completed is not null)
                            {
                                SalesRoomBenchmarkTelemetry.RecordSemanticInput("turn_completed");
                                var s = completed.Scope;
                                logger.LogInformation("MeetingTrace Stage=turn_completed RoomId={RoomId} TurnId={TurnId} CharacterCount={CharacterCount} DuringPresentation={DuringPresentation} TurnGeneration={TurnGeneration}",
                                    room.Id, StableTurnId(room.Id, s.ParticipantId, s.TrackId, s.TrackGeneration, completed.Utterance.StartedAt),
                                    completed.Text.Length, s.BeganDuringPresentation, s.TurnGeneration);
                                var latency = Now - completed.Utterance.StartedAt.UtcDateTime;
                                if (latency >= TimeSpan.Zero && latency < TimeSpan.FromMinutes(2))
                                    SalesRoomBenchmarkTelemetry.RecordLatency("semantic_capture_to_final", latency);
                                await ProcessTranscriptAsync(new(completed.Utterance, s.ConsentVersion, s.Retain,
                                    s.ParticipantGeneration, s.TurnGeneration, s.ResponseGeneration, s.BeganDuringPresentation), completed.Text);
                            }
                            continue;
                        }
                        if (normalized.Type == RealtimeAgentEventTypes.ProviderError)
                        {
                            logger.LogWarning("Room realtime provider rejected an event. RoomId={RoomId} Code={Code}", room.Id, normalized.ErrorCode);
                            await CancelActiveSpeechAsync();
                            room.PauseAgent(work.LeaseOwnerId, work.Generation, "realtime_provider_error",
                                "The realtime conversation provider rejected a request. Restart the agent to retry; the human call remains available.");
                            await db.SaveChangesAsync(CancellationToken.None);
                            runtimeStop.Cancel(); await AwaitQuietly(inputTask, providerTask); return;
                        }
                        if (normalized.Type == RealtimeAgentEventTypes.ParticipantTranscriptCompleted)
                        {
                            using var payload = JsonDocument.Parse(runtime.ProviderJson);
                            var itemId = payload.RootElement.TryGetProperty("item_id", out var providerItem) ? providerItem.GetString() : runtime.ProviderEventId;
                            // Completion and commit acknowledgements can arrive in either order. The correlator waits for
                            // both so only the server-side commit can bind speech to a participant.
                            if (itemId is not null &&
                                transcriptCorrelation.Complete(itemId, normalized.Text ?? "", out var completedContext, out var completedText))
                                await ProcessTranscriptAsync(completedContext!, completedText!);
                        }
                        if (semantic is not null && normalized.Type == RealtimeAgentEventTypes.ToolInvocation)
                        {
                            var result = new SalesRoomConversationToolResult(false, "conversation_stale", "Please repeat or clarify the question.");
                            Guid? resultTurn = null;
                            if (pendingDialogue is { } proposal && runtime.ProviderTurnId == proposal.Turn.InputId)
                            {
                                pendingDialogue = null;
                                resultTurn = proposal.Turn.InputId;
                                var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId,
                                    room.Id, proposal.Turn.Binding.ParticipantId, Now, ct);
                                var participantCurrent = media.IsParticipantConnected(proposal.Turn.Binding.ParticipantId) &&
                                    await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                                        x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == proposal.Turn.Binding.ParticipantId &&
                                        x.Version == proposal.Turn.ConsentVersion && x.TranscriptRetentionAllowed, ct);
                                if (authority is not null && proposal.Turn.ExpiresUtc > Now && !semantic.HasPendingSpeech &&
                                    participantCurrent &&
                                    new AgentConversation(proposal.Turn.Binding, AgentConversationPhase.Interpreting).Check(authority, Now).Allowed &&
                                    SalesRoomDialoguePolicy.Permitted(normalized.ToolName, normalized.ToolArgumentsJson) &&
                                    proposal.Tools.Contains(normalized.ToolName!) &&
                                    (!SalesRoomDialoguePolicy.IsPlayback(normalized.ToolName) || authority.ControllerAllowed))
                                {
                                    var previousSpeech = activeSpeech;
                                    playbackToolResult = null;
                                    if (normalized.ToolName == SalesRoomDialoguePolicy.State)
                                        playbackToolResult = new(true, "presentation_state", (await DialogueRequestAsync(proposal.Turn, ct)).PlaybackContext!);
                                    else if (normalized.ToolName != SalesRoomConversationTools.Wait)
                                        await ProcessTranscriptAsync(proposal.Input, proposal.Text, proposal.Retained, normalized.ToolName);
                                    var accepted = normalized.ToolName == SalesRoomConversationTools.Wait ||
                                        (normalized.ToolName == SalesRoomConversationTools.Question
                                            ? groundedWork is not null || await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                                                x.CompanyId == room.CompanyId && x.SessionId == sessionId &&
                                                x.ClientQuestionId == proposal.Turn.InputId, ct)
                                            : activeSpeech is not null && !ReferenceEquals(previousSpeech, activeSpeech));
                                    result = playbackToolResult ?? new(accepted, accepted ? normalized.ToolName == SalesRoomConversationTools.Wait ? "waiting" : "reply_processing" : "action_not_accepted",
                                        accepted ? "Processing accepted; speech has not yet completed and requires release validation."
                                            : "The action was not accepted. Please repeat or clarify the request.");
                                }
                            }
                            if (normalized.ToolCallId is { } dialogueCall)
                            {
                                try
                                {
                                    await realtimeConversation.SubmitToolResultAsync(providerSession!, dialogueCall, JsonSerializer.Serialize(result), ct);
                                    logger.LogInformation("MeetingTrace Stage=tool_result RoomId={RoomId} TurnId={TurnId} Tool={Tool} Accepted={Accepted} Code={Code}",
                                        room.Id, resultTurn, normalized.ToolName, result.Accepted, result.Code);
                                    if (resultTurn.HasValue) toolResultAwaitingResponseEnd = resultTurn;
                                }
                                catch (RealtimeAgentEventException) { /* Late/duplicate calls cannot execute or close input. */ }
                            }
                            continue;
                        }
                        if (Options.HybridConversationEnabled && normalized.Type == RealtimeAgentEventTypes.ToolInvocation)
                        {
                            var result = new SalesRoomConversationToolResult(false, "conversation_stale",
                                "This proposal has no current confirmed participant turn.");
                            Guid? resultTurn = null;
                            if (pendingTool is { } pending && runtime.ProviderTurnId == pending.Turn.HeardTurnId)
                            {
                                pendingTool = null; // One proposal per input; later/parallel calls cannot act.
                                resultTurn = pending.Turn.HeardTurnId;
                                var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId,
                                    room.Id, pending.Turn.Binding.ParticipantId, Now, ct);
                                var toolFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                                    x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                                var allowed = authority is not null && pending.Turn.ExpiresUtc > Now && activeSpeech is null &&
                                    transcriptCorrelation.PendingCount == 0 && (semantic?.PendingCount ?? 0) == 0 &&
                                    toolFloor is { State: SalesRoomFloorStates.Host, Overlap: false } &&
                                    toolFloor.Version == pending.Turn.FloorVersion &&
                                    media.IsParticipantConnected(pending.Turn.Binding.ParticipantId) &&
                                    new AgentConversation(pending.Turn.Binding, AgentConversationPhase.Interpreting)
                                        .Check(authority, Now).Allowed &&
                                    SalesRoomConversationTools.Matches(normalized.ToolName, normalized.ToolArgumentsJson, pending.Turn.Intent);
                                if (allowed && normalized.ToolName == SalesRoomConversationTools.Continue)
                                {
                                    await using var commandScope = captureScopes.CreateAsyncScope();
                                    using var commandCompany = commandScope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>()
                                        .BeginScope(room.CompanyId);
                                    var service = (SalesRoomAgentService)commandScope.ServiceProvider.GetRequiredService<ISalesRoomAgentService>();
                                    result = await service.ContinueConversationAsync(pending.Turn, ct);
                                }
                                else if (allowed && normalized.ToolName == SalesRoomConversationTools.Question)
                                {
                                    var speaker = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                                        x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == pending.Turn.Binding.ParticipantId, ct);
                                    await HandleTranscriptAsync(room, speaker, pending.Input.Value, pending.Text,
                                        false, pending.Retained, media, work, floorEvents, questions, ct, semanticQuestion: true);
                                    var processed = await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                                        x.CompanyId == room.CompanyId && x.SessionId == pending.Turn.Binding.SessionId &&
                                        x.ClientQuestionId == pending.Turn.HeardTurnId, ct);
                                    result = new(processed, processed ? "question_processed" : "question_not_accepted",
                                        processed ? "The grounded question workflow processed the request; existing release controls apply."
                                            : "The question could not be accepted. The host can retry using typed questions.");
                                }
                                else if (allowed && normalized.ToolName == SalesRoomConversationTools.Wait)
                                    result = pending.Turn.Intent == AgentConversationIntent.Acknowledgement
                                        ? await AcknowledgeConversationAsync(pending.Turn, pending.Text, work, ct)
                                        : new(true, "waiting", "The presentation remains paused. Host controls remain available.");
                                if (!allowed)
                                    logger.LogWarning("Room conversation proposal rejected. RoomId={RoomId} Intent={Intent} Tool={Tool} ActiveSpeech={ActiveSpeech} PendingTranscripts={PendingTranscripts} FloorMatches={FloorMatches}",
                                        room.Id, pending.Turn.Intent, normalized.ToolName, activeSpeech is not null,
                                        transcriptCorrelation.PendingCount, toolFloor?.Version == pending.Turn.FloorVersion);
                                awaitingContextualReplyUntil = ReplyDeadlineAfterTool(result.Accepted,
                                    normalized.ToolName, pending.ReplyDeadline, Now);
                                db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                            }
                            if (normalized.ToolCallId is { } callId)
                            {
                                await realtimeConversation.SubmitToolResultAsync(providerSession, callId,
                                    JsonSerializer.Serialize(result), ct);
                                toolResultAwaitingResponseEnd = resultTurn;
                            }
                            // No generated success speech: only the accepted, approved narration owns audio.
                        }
                        if (normalized.Type == RealtimeAgentEventTypes.UsageUpdated &&
                            toolResultAwaitingResponseEnd is Guid completedTurn && runtime.ProviderTurnId == completedTurn)
                        {
                            // response.output_item.done may precede response.done. Starting another
                            // response before that fence produces a provider response-already-active error.
                            toolResultAwaitingResponseEnd = null;
                            await realtimeConversation.ContinueAfterToolAsync(providerSession,
                                new(completedTurn, false, 512, KeepProviderContext: true), ct);
                        }
                        if (normalized.InputTokens > 0 || normalized.OutputTokens > 0 || normalized.AudioDurationMilliseconds > 0)
                        {
                            pendingProviderAudio += normalized.AudioDurationMilliseconds;
                            pendingInputTokens += normalized.InputTokens;
                            pendingOutputTokens += normalized.OutputTokens;
                            SalesRoomBenchmarkTelemetry.RecordProvider(normalized.AudioDurationMilliseconds,
                                normalized.InputTokens, normalized.OutputTokens);
                        }
                    }
                }

                if (pendingDialogue is { } expiredDialogue && expiredDialogue.Turn.ExpiresUtc <= Now)
                {
                    pendingDialogue = null;
                    if (activeSpeech is null && room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) &&
                        room.AgentTurnGeneration == expiredDialogue.Turn.Binding.TurnGeneration)
                    {
                        room.ConversationReplyWithheld(work.LeaseOwnerId, work.Generation);
                        await db.SaveChangesAsync(ct);
                    }
                }
                if (semantic is not null)
                {
                    if (semantic.ActiveScope is { } scope)
                    {
                        var speaker = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                            x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == scope.ParticipantId, ct);
                        if (!CanReceiveSpeech(speaker, media.IsParticipantConnected(scope.ParticipantId), Now) ||
                            speaker!.Version != scope.ConsentVersion || speaker.Generation != scope.ParticipantGeneration ||
                            !speaker.TranscriptRetentionAllowed || room.AgentTurnGeneration != scope.TurnGeneration)
                            if (semantic.Revoke(scope.ParticipantId)) await ClearSemanticBufferAsync();
                    }
                    if (semantic.Expire(Now) > 0) SalesRoomBenchmarkTelemetry.RecordConversationFailure("semantic_turn_timeout");
                    var silence = semantic.Tick(clock.GetUtcNow());
                    if (silence.ClearBuffer) SalesRoomBenchmarkTelemetry.RecordConversationFailure("semantic_capture_timeout");
                    await SendSemanticAudioAsync(silence);
                }
                if (semantic is not null && Now >= nextInputDiagnostics)
                {
                    var statistics = media.GetStatistics();
                    logger.LogDebug("MeetingTrace Stage=input_health RoomId={RoomId} MediaState={MediaState} ReceivedFrames={ReceivedFrames} DroppedFrames={DroppedFrames} MaximumFrameAgeMs={MaximumFrameAgeMs} Discontinuities={Discontinuities} ReceivedMs={ReceivedMs} DetectedSpeechMs={DetectedSpeechMs} ForwardedMs={ForwardedMs} PendingItems={PendingItems}",
                        room.Id, statistics.State, statistics.ReceivedFrames, statistics.DroppedFrames,
                        maximumInputFrameAgeMs, semantic.Discontinuities, semantic.ReceivedMilliseconds,
                        semantic.DetectedSpeechMilliseconds, semantic.ForwardedMilliseconds, semantic.PendingCount);
                    maximumInputFrameAgeMs = 0;
                    nextInputDiagnostics = Now.AddSeconds(5);
                }
                var receivedDelta = (semantic?.ReceivedMilliseconds ?? segmenter.ReceivedMilliseconds) - lastReceived;
                var detectedDelta = (semantic?.DetectedSpeechMilliseconds ?? segmenter.DetectedSpeechMilliseconds) - lastDetected;
                var forwardedDelta = (semantic?.ForwardedMilliseconds ?? segmenter.ForwardedMilliseconds) - lastForwarded;
                var recordInput = activeSpeech is null && groundedWork is null && (receivedDelta > 0 || detectedDelta > 0 || forwardedDelta > 0 ||
                    pendingProviderAudio > 0 || pendingInputTokens > 0 || pendingOutputTokens > 0);
                if (recordInput)
                {
                    room.RecordAgentAudio(work.LeaseOwnerId, work.Generation, receivedDelta, detectedDelta, forwardedDelta,
                        pendingProviderAudio, pendingInputTokens, pendingOutputTokens);
                }
                if (SalesRoomOperationsPolicy.AudioLimitProblem(room, Options) is { } audioLimit)
                {
                    await CancelActiveSpeechAsync();
                    SalesRoomBenchmarkTelemetry.RecordQuota("audio_limit");
                    await StopForPolicyAsync(room, work, "quota_exceeded", audioLimit);
                    break;
                }
                if (room.AgentStartedUtc <= Now.AddMinutes(-Options.MaximumSessionMinutes))
                {
                    SalesRoomBenchmarkTelemetry.RecordQuota("call_duration");
                    await CancelActiveSpeechAsync();
                    await StopForPolicyAsync(room, work, "duration_limit");
                    break;
                }
                var callSpend = SalesRoomOperationsPolicy.EstimatedSpend(room, Options);
                SalesRoomBenchmarkTelemetry.RecordEstimatedSpend(callSpend);
                if (callSpend >= Options.MaximumSpendPerCallUsd)
                {
                    SalesRoomBenchmarkTelemetry.RecordQuota("call_spend");
                    await CancelActiveSpeechAsync();
                    await StopForPolicyAsync(room, work, "spend_limit");
                    break;
                }
                if (Now >= renewAt)
                {
                    if (await CompanyMonthlySpendAsync(room.CompanyId, ct) >= Options.MaximumMonthlySpendPerCompanyUsd)
                    {
                        SalesRoomBenchmarkTelemetry.RecordQuota("company_monthly_spend");
                        await CancelActiveSpeechAsync();
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
                    transcriptCorrelation.PendingCount + (semantic?.PendingCount ?? 0) + (queued is null ? 0 : 1));
                var currentFloor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                currentFloor?.ExpireStop(Now);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException)
                {
                    // Playback can complete while this loop renews the lease. Re-read authority
                    // next tick; do not tear down a healthy media/transcription connection.
                    db.ChangeTracker.Clear();
                    renewAt = Now;
                    continue;
                }
                if (recordInput)
                {
                    SalesRoomBenchmarkTelemetry.RecordInput(receivedDelta, detectedDelta, forwardedDelta);
                    lastReceived += receivedDelta; lastDetected += detectedDelta; lastForwarded += forwardedDelta;
                    pendingProviderAudio = pendingInputTokens = pendingOutputTokens = 0;
                }
                // A local candidate (including fan noise) cannot stall the next talking point.
                // Only a confirmed, authorized question changes the floor and cancels output.
                if (turnBuffer.TakeExpiredIncomplete(Now) is { } unfinished && deferredCompleteTurn is { } held)
                {
                    // A bounded wait ran out without a continuation. Ask, never invent a
                    // question or release source claims. Retain the input fence until drain.
                    turnBuffer.Hold(unfinished.Scope, unfinished.Text, Now, complete: true);
                    deferredCompleteTurn = (held.Context, held.RetainedId, SalesRoomTurnReadiness.Clarify);
                    logger.LogInformation("MeetingTrace Stage=turn_wait_recovery RoomId={RoomId} TurnId={TurnId} Decision=Clarify Reason=continuation_timeout", room.Id,
                        StableTurnId(room.Id, held.Context.Value.ParticipantId, held.Context.Value.TrackId,
                            held.Context.Value.TrackGeneration, held.Context.Value.StartedAt));
                }
                if (turnBuffer.Expire(Now))
                {
                    deferredCompleteTurn = null;
                    logger.LogWarning("MeetingTrace Stage=completed_turn_expired RoomId={RoomId} Reason=input_did_not_drain", room.Id);
                }
                if (deferredCompleteTurn is { } deferred &&
                    turnBuffer.ReleaseComplete(Now, HasPendingInput()) is { } releasedText)
                {
                    // Usage is saved above before re-entering code that reloads the room.
                    // Recheck consent/track/ownership without retaining duplicate evidence.
                    deferredCompleteTurn = null;
                    await ProcessTranscriptAsync(deferred.Context, releasedText, deferred.RetainedId,
                        readiness: deferred.Decision);
                    continue; // Reload speech/floor after the newly accepted turn.
                }
                if (queued is not null && activeSpeech is null && groundedWork is null)
                    activeSpeech = SpeakInOwnScopeAsync(queued.Id, media, work, control,
                        control.InterruptionVersion, ct);
            }
            runtimeStop.Cancel(); segmenter.ClearAll();
            events.Writer.TryComplete();
            await AwaitQuietly(inputTask, providerTask);

            bool HasPendingInput() => semantic is null
                ? segmenter.HasActiveSpeech || transcriptCorrelation.PendingCount > 0
                : semantic.HasPendingSpeech || semantic.PendingCount > 0;
            async Task SaveRecoveryUsageAsync()
            {
                db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
                room.RecordAgentAudio(work.LeaseOwnerId, work.Generation,
                    Math.Max(0, (semantic?.ReceivedMilliseconds ?? segmenter.ReceivedMilliseconds) - lastReceived),
                    Math.Max(0, (semantic?.DetectedSpeechMilliseconds ?? segmenter.DetectedSpeechMilliseconds) - lastDetected),
                    Math.Max(0, (semantic?.ForwardedMilliseconds ?? segmenter.ForwardedMilliseconds) - lastForwarded),
                    pendingProviderAudio, pendingInputTokens, pendingOutputTokens);
                await db.SaveChangesAsync(ct);
            }
            bool IsCurrentTrack(Guid participant, string track, long generation) => semantic is null
                ? segmenter.IsCurrentTrack(participant, track, generation)
                : semantic.IsCurrentTrack(participant, track, generation);
            Task ClearSemanticBufferAsync() => pcm.SendClientEventAsync(providerSession,
                JsonSerializer.Serialize(new { event_id = "clear_" + Guid.NewGuid().ToString("N"), type = "input_audio_buffer.clear" }), ct);
            async Task SendSemanticAudioAsync(SalesRoomSemanticAudio audio)
            {
                if (audio.ClearBuffer) await ClearSemanticBufferAsync();
                foreach (var chunk in PcmChunks(audio.Samples))
                    await pcm.SendInputAudioAsync(providerSession, chunk, ct);
            }

            async Task CancelActiveSpeechAsync(long? confirmedResponseGeneration = null)
            {
                var stopStarted = Stopwatch.GetTimestamp();
                control.CancelResponse(confirmedResponseGeneration);
                await media.CancelSpeechAsync(CancellationToken.None);
                SalesRoomBenchmarkTelemetry.RecordLatency("cancellation_to_transport_stopped", Stopwatch.GetElapsedTime(stopStarted));
                if (activeSpeech is null) return;
                await activeSpeech;
                activeSpeech = null;
                db.ChangeTracker.Clear();
                room = await RoomAsync(work, CancellationToken.None);
            }

            async Task ProcessTranscriptAsync(UtteranceContext context, string text, Guid? previouslyRetained = null,
                string? routedAction = null, SalesRoomTurnReadiness? readiness = null)
            {
                // An empty final result is noise, not a provisional interrupt. The provider's
                // completion is the only confirmation signal; local VAD never stops playback.
                if (string.IsNullOrWhiteSpace(text)) return;
                if (groundedWork is { IsCompleted: false })
                {
                    // Keep audio/provider pumps running and preserve fragment order. Every
                    // queued input passes fresh consent/track/generation checks on drain.
                    if (inputsAfterLookup.Count >= 8)
                    {
                        logger.LogWarning("MeetingTrace Stage=input_deferred_overflow RoomId={RoomId} Code=repeat_input", room.Id);
                        return;
                    }
                    inputsAfterLookup.Enqueue((context, text, previouslyRetained, routedAction, readiness));
                    logger.LogInformation("MeetingTrace Stage=input_deferred_during_lookup RoomId={RoomId} Pending={Pending}", room.Id, inputsAfterLookup.Count);
                    return;
                }
                deferredCompleteTurn = null;
                pendingTool = null; // Any newer confirmed utterance invalidates an older model proposal.
                pendingDialogue = null;
                toolResultAwaitingResponseEnd = null;
                await using var captureScope = captureScopes.CreateAsyncScope();
                var retained = previouslyRetained ?? await captureScope.ServiceProvider.GetRequiredService<ISalesRoomCaptureService>().RetainAsync(
                    new(room.CompanyId, room.Id, context.Value.ParticipantId, context.ConsentVersion, context.Retain,
                        work.Generation, work.LeaseOwnerId, context.Value.TrackId, context.Value.TrackGeneration,
                        context.Value.StartedAt.UtcDateTime, context.Value.EndedAt.UtcDateTime, context.Value.Overlapped, text), ct);
                var current = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == context.Value.ParticipantId, ct);
                // Persistent question/AI-run content can only derive from committed, permitted evidence.
                var fence = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                    x.CompanyId == room.CompanyId && x.Id == room.Id, ct);
                if (!CanReceiveSpeech(current, media.IsParticipantConnected(context.Value.ParticipantId), Now) ||
                    current!.Version != context.ConsentVersion || current.Generation != context.ParticipantGeneration ||
                    current.ExpiresUtc <= Now || !media.IsParticipantConnected(current.Id) ||
                    !IsCurrentTrack(current.Id, context.Value.TrackId, context.Value.TrackGeneration) ||
                    fence.State != SalesBrowserRoomStates.Live || fence.AgentTurnGeneration != context.TurnGeneration ||
                    !fence.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
                    context.Retain && !retained.HasValue)
                {
                    turnBuffer.Clear();
                    return;
                }

                if (readiness is null && routedAction is null && Options.HybridConversationEnabled && retained.HasValue && !context.Value.Overlapped)
                {
                    var scope = new AgentSpeechTurnScope(room.CompanyId, room.Id, current.Id,
                        current.Generation, current.Version, context.Value.TrackId, context.Value.TrackGeneration,
                        work.Generation, context.TurnGeneration);
                    var combined = turnBuffer.Take(scope, text, Now);
                    if (combined is null) return;
                    var completionFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                    var completionContext = new SalesRoomConversationContext(room.CompanyId, agentId, room.Id,
                        sessionId, current.Id, StableTurnId(room.Id, current.Id, context.Value.TrackId,
                            context.Value.TrackGeneration, context.Value.StartedAt), "", "", false,
                        completionFloor.SlideNumber, completionFloor.TalkingPointIndex, completionFloor.ControlMode);
                    readiness = await RunWithLeaseRenewalAsync(work,
                        token => conversationReasoner.JudgeTurnAsync(completionContext, combined, token), ct);
                    if (readiness == SalesRoomTurnReadiness.Wait || HasPendingInput())
                    {
                        turnBuffer.Hold(scope, combined, Now, complete: readiness != SalesRoomTurnReadiness.Wait);
                        deferredCompleteTurn = (context, retained.Value, readiness.Value);
                        logger.LogInformation("MeetingTrace Stage=turn_held RoomId={RoomId} TurnId={TurnId} Decision={Decision} ActiveSpeech={ActiveSpeech} PendingTranscripts={PendingTranscripts}",
                            room.Id, completionContext.AnswerId, readiness, semantic?.HasPendingSpeech ?? segmenter.HasActiveSpeech,
                            semantic?.PendingCount ?? transcriptCorrelation.PendingCount);
                        return;
                    }
                    text = combined;
                }
                else turnBuffer.Clear();
                var clarificationRequested = readiness == SalesRoomTurnReadiness.Clarify;

                var interruptedAgent = activeSpeech is { IsCompleted: false };
                var questionDuringPresentation = interruptedAgent || context.BeganDuringPresentation;
                var agentName = room.AgentId is Guid selectedAgentId
                    ? await db.Agents.IgnoreQueryFilters().AsNoTracking().Where(x =>
                        x.CompanyId == room.CompanyId && x.Id == selectedAgentId)
                        .Select(x => x.DisplayName).SingleOrDefaultAsync(ct) ?? "Alex"
                    : "Alex";
                var explicitlyAddressed = IsAddressedQuestion(text, agentName);
                var connectedHumans = 0;
                if (!explicitlyAddressed && !context.Value.Overlapped)
                {
                    var admitted = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().Where(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                        x.State == SalesRoomParticipantStates.Admitted).Select(x => x.Id).ToListAsync(ct);
                    connectedHumans = CountConnectedHumans(admitted, current.Id, media.IsParticipantConnected);
                }
                var addressed = explicitlyAddressed || ShouldTreatInterruptedSpeechAsAddressedQuestion(
                    text, questionDuringPresentation, connectedHumans, context.Value.Overlapped);
                var inputFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                var contextualReply = ShouldRouteContextualReply(Options.HybridConversationEnabled,
                    IsAddressedToAgent(text, agentName), inputFloor?.ControlMode == "autonomous", context.Value.Overlapped,
                    connectedHumans, text) && context.Retain && retained.HasValue && providerSession is not null && routedAction is null;
                if (!addressed && !contextualReply && routedAction is null) return;
                var confirmedQuestion = routedAction == SalesRoomConversationTools.Question;
                FreshConversationInput? freshInput = null;
                if (!clarificationRequested && routedAction is null && Options.HybridConversationEnabled && addressed && !contextualReply && retained.HasValue)
                {
                    confirmedQuestion = await IsSubstantiveQuestionAsync(room, current.Id, text, work, ct, completenessConfirmed: true);
                    if (!confirmedQuestion) return;
                }
                // Reserve the confirmed turn before asking Realtime to choose a tool. Otherwise
                // autonomous narration can advance the checkpoint while the model is deciding,
                // and the subsequent proposal is correctly (but needlessly) fenced as stale.
                // Candidate audio and still-waiting fragments never reach this transition.
                // The current floor is authoritative even if input began before narration or
                // the last speech task finished during readiness judgment. Between segments
                // there may be queued narration but no active task to signal interruption.
                logger.LogDebug("MeetingTrace Stage=turn_reservation_candidate RoomId={RoomId} FloorState={FloorState} BeganDuringPresentation={BeganDuringPresentation} FloorMatches={FloorMatches} Routed={Routed}",
                    room.Id, inputFloor?.State, context.BeganDuringPresentation,
                    MatchesSpeechFloor(inputFloor, context.TurnGeneration, context.ResponseGeneration), routedAction is not null);
                if (routedAction is null && inputFloor?.State == SalesRoomFloorStates.Agent &&
                    MatchesSpeechFloor(inputFloor, context.TurnGeneration, context.ResponseGeneration))
                {
                    var interruptedResponse = inputFloor.ResponseGeneration;
                    await CancelActiveSpeechAsync(interruptedResponse);
                    var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
                    current = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.Id == context.Value.ParticipantId, ct);
                    logger.LogDebug("MeetingTrace Stage=turn_reservation_recheck RoomId={RoomId} InputVersion={InputVersion} FloorVersion={FloorVersion} FloorState={FloorState} InputResponse={InputResponse} FloorResponse={FloorResponse} RoomTurn={RoomTurn} InputTurn={InputTurn} TrackCurrent={TrackCurrent} Handoff={Handoff}",
                        room.Id, inputFloor.Version, floor?.Version, floor?.State, interruptedResponse,
                        floor?.ResponseGeneration, room.AgentTurnGeneration, context.TurnGeneration,
                        IsCurrentTrack(context.Value.ParticipantId, context.Value.TrackId, context.Value.TrackGeneration),
                        floor is not null && control.AcceptsConfirmedHandoff(interruptedResponse, floor));
                    logger.LogDebug("MeetingTrace Stage=turn_reservation_authority RoomId={RoomId} ParticipantAllowed={ParticipantAllowed} ConsentVersionMatches={ConsentVersionMatches} ParticipantGenerationMatches={ParticipantGenerationMatches} ParticipantCurrent={ParticipantCurrent} RoomLive={RoomLive} OwnerCurrent={OwnerCurrent}",
                        room.Id, CanReceiveSpeech(current, media.IsParticipantConnected(context.Value.ParticipantId), Now),
                        current?.Version == context.ConsentVersion, current?.Generation == context.ParticipantGeneration,
                        current is not null && current.ExpiresUtc > Now && media.IsParticipantConnected(current.Id),
                        room.State == SalesBrowserRoomStates.Live, room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now));
                    if (!CanReceiveSpeech(current, media.IsParticipantConnected(context.Value.ParticipantId), Now) ||
                        current!.Version != context.ConsentVersion || current.Generation != context.ParticipantGeneration ||
                        current.ExpiresUtc <= Now || !media.IsParticipantConnected(current.Id) ||
                        !IsCurrentTrack(current.Id, context.Value.TrackId, context.Value.TrackGeneration) ||
                        room.State != SalesBrowserRoomStates.Live ||
                        !room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
                        room.AgentTurnGeneration != context.TurnGeneration ||
                        floor is null || !(control.AcceptsConfirmedHandoff(interruptedResponse, floor) ||
                            floor.State == SalesRoomFloorStates.Agent && floor.ResponseGeneration == interruptedResponse &&
                            floor.Version == inputFloor.Version)) return;

                    var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                        x.CompanyId == room.CompanyId && x.Id == sessionId, ct);
                    if (meeting.ConcurrencyVersion != floor.PresentationVersion || meeting.CurrentSlideIndex != floor.SlideNumber)
                        return;

                    room.PreemptAgent(work.LeaseOwnerId, work.Generation,
                        "Confirmed participant speech interrupted the agent presentation.");
                    var obsoleteSpeech = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().Where(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                        x.TurnGeneration == context.TurnGeneration &&
                        (x.Status == SalesRoomAgentSpeechStates.Queued ||
                         x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct);
                    foreach (var item in obsoleteSpeech)
                        item.Fail(SalesRoomAgentSpeechStates.Interrupted, "human_speaking",
                            "Confirmed participant speech stopped the agent turn.", Now);
                    floor.HumanStarted(current.Id, current.Generation, false, room.AgentTurnGeneration,
                        floor.PresentationVersion, floor.SlideNumber,
                        floor.TalkingPointIndex, floor.ResumeMarker, Now);
                    var stopId = Guid.NewGuid();
                    var required = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                        x.State == SalesRoomParticipantStates.Admitted && x.Connected, ct);
                    floor.RequestPlaybackStop(stopId, required,
                        Now.AddMilliseconds(Options.PlaybackStopAcknowledgementTimeoutMilliseconds), Now);
                    var stopRequest = new SalesRoomPlaybackStopRequest(room.Id, stopId,
                        floor.ResponseGeneration, floor.PlaybackStopDeadlineUtc!.Value);
                    await db.SaveChangesAsync(ct);
                    conductor.Preempt(room.CompanyId, sessionId, room.AgentTurnGeneration);
                    await floorEvents.RequestPlaybackStopAsync(room.CompanyId, sessionId, stopRequest, ct);
                    // This input was authenticated against the old turn above. Carry only this
                    // confirmed input across our own preemption; all later authority checks use
                    // the new generation and the reserved floor checkpoint.
                    context = context with { TurnGeneration = room.AgentTurnGeneration,
                        ResponseGeneration = floor.ResponseGeneration };
                    questionDuringPresentation = true;
                    logger.LogInformation("MeetingTrace Stage=turn_reserved RoomId={RoomId} TurnId={TurnId} TurnGeneration={TurnGeneration} ResponseGeneration={ResponseGeneration} Slide={Slide} Point={Point} OffsetMs={OffsetMs}",
                        room.Id, StableTurnId(room.Id, current.Id, context.Value.TrackId,
                            context.Value.TrackGeneration, context.Value.StartedAt), room.AgentTurnGeneration,
                        floor.ResponseGeneration, floor.SlideNumber, floor.TalkingPointIndex, floor.ResumeOffsetMilliseconds);
                    var stopLatency = Now - context.Value.StartedAt.UtcDateTime;
                    if (stopLatency >= TimeSpan.Zero && stopLatency <= TimeSpan.FromMinutes(1))
                        SalesRoomBenchmarkTelemetry.RecordLatency("candidate_to_playback_stop_request", stopLatency);
                }
                if (clarificationRequested) routedAction = SalesRoomDialoguePolicy.Clarify;
                if (contextualReply && !clarificationRequested)
                {
                    var inputId = StableTurnId(room.Id, current.Id, context.Value.TrackId,
                        context.Value.TrackGeneration, context.Value.StartedAt);
                    freshInput = await PrepareConversationInputAsync(conversationSession, work, current.Id,
                        inputId, text, providerSession!, ct, modelSelectsTool: semantic is not null);
                    if (freshInput is null) return;
                    if (semantic is not null)
                    {
                        var dialogueTurn = new SalesRoomDialogueTurn(freshInput.Binding, inputId, context.ConsentVersion, Now.AddSeconds(15));
                        var request = await DialogueRequestAsync(dialogueTurn, ct);
                        pendingDialogue = (dialogueTurn, context, text, retained!.Value, request.AvailableTools!);
                        logger.LogInformation("MeetingTrace Stage=route_requested RoomId={RoomId} TurnId={TurnId} Mode={Mode} OfferedTools={OfferedTools}",
                            room.Id, inputId, dialogueTurn.Binding.Mode, string.Join(",", request.AvailableTools!));
                        await realtimeConversation.RequestResponseAsync(providerSession!, request, ct);
                        return;
                    }
                    confirmedQuestion = freshInput.Intent == AgentConversationIntent.Question;
                    // Initial social/command turns enter provider context, without inventing
                    // factual questions or granting the speech/tools added in later prompts.
                    if (!confirmedQuestion && freshInput.PlayedSpeechId is null) return;
                }
                control.Timing.Begin("confirmed_input_to_first_answer_frame", StableTurnId(room.Id, current.Id,
                    context.Value.TrackId, context.Value.TrackGeneration, context.Value.StartedAt));
                if (semantic is not null) SalesRoomBenchmarkTelemetry.RecordSemanticInput("routing");
                if (addressed && !contextualReply) awaitingContextualReplyUntil = null;
                if (Options.HybridConversationEnabled && context.Retain && retained.HasValue &&
                    providerSession is not null && text.Length <= 2000)
                {
                    var turnId = StableTurnId(room.Id, current.Id, context.Value.TrackId,
                        context.Value.TrackGeneration, context.Value.StartedAt);
                    var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId,
                        room.Id, current.Id, Now, ct);
                    if (!conversationSession.HasHeard(turnId) && authority is not null &&
                        conversationSession.BeginTurn(authority, turnId, Now) is not null)
                    {
                        await realtimeConversation.AddConfirmedTurnAsync(providerSession, turnId, text, ct);
                    }
                }
                if (SalesRoomDialoguePolicy.IsPlayback(routedAction))
                {
                    var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId, room.Id, current.Id, Now, ct);
                    if (authority is null || activeSpeech is not null) return;
                    var turn = new SalesRoomDialogueTurn(authority.Binding, StableTurnId(room.Id, current.Id,
                        context.Value.TrackId, context.Value.TrackGeneration, context.Value.StartedAt), context.ConsentVersion, Now.AddSeconds(15));
                    playbackToolResult = await ExecuteDialoguePlaybackCommandAsync(turn, routedAction!, ct);
                    db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                    return;
                }
                if (routedAction is SalesRoomDialoguePolicy.Social or SalesRoomDialoguePolicy.General or SalesRoomDialoguePolicy.Clarify)
                {
                    var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId, room.Id, current.Id, Now, ct);
                    if (authority is null || activeSpeech is not null) return;
                    var turn = new SalesRoomDialogueTurn(authority.Binding, StableTurnId(room.Id, current.Id,
                        context.Value.TrackId, context.Value.TrackGeneration, context.Value.StartedAt), context.ConsentVersion, Now.AddSeconds(60));
                    activeSpeech = SpeakDialogueInOwnScopeAsync(turn, text, routedAction, providerSession!, media, work,
                        control, control.InterruptionVersion, ct);
                    return;
                }
                if (contextualReply)
                {
                    var intent = freshInput!.Intent;
                    var heardTurn = StableTurnId(room.Id, current.Id, context.Value.TrackId,
                        context.Value.TrackGeneration, context.Value.StartedAt);
                    if (!conversationSession.HasHeard(heardTurn) || providerSession is null) return;
                    // The shared reasoner already classified this complete turn. A second model
                    // must not veto factual retrieval by proposing a different tool. Execution
                    // still rechecks current tenant, consent, floor and release policy below.
                    if (intent == AgentConversationIntent.Question)
                    {
                        awaitingContextualReplyUntil = null;
                        db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                        await HandleTranscriptAsync(room, current, context.Value, text, false, retained.HasValue,
                            media, work, floorEvents, questions, ct, semanticQuestion: true);
                        return;
                    }
                    // The existing continuation/bridge routes retain their narrower action
                    // eligibility until the later tool/release prompts replace those routes.
                    if (freshInput.PlayedSpeechId is not Guid playedId) return;
                    var turn = new SalesRoomConversationTurn(freshInput.Binding, heardTurn, playedId,
                        freshInput.FloorVersion, Now.AddSeconds(15), intent);
                    pendingTool = (turn, context, text, retained.HasValue, awaitingContextualReplyUntil);
                    awaitingContextualReplyUntil = null;
                    await realtimeConversation.RequestResponseAsync(providerSession,
                        new(heardTurn, false, 512, KeepProviderContext: true,
                            RequiredToolName: SalesRoomConversationTools.ForIntent(intent)), ct);
                    db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                    return;
                }
                if (routedAction == SalesRoomConversationTools.Question && semantic is not null && providerSession is not null)
                {
                    groundedWork = GroundedQuestionInOwnScopeAsync(context, text, retained.HasValue,
                        questionDuringPresentation, providerSession, media, work, control, runtimeStop.Token);
                    return;
                }
                await HandleTranscriptAsync(room, current, context.Value, text,
                    questionDuringPresentation, retained.HasValue, media, work, floorEvents, questions, ct, confirmedQuestion);
            }
        }

        finally

        {
            conversationSession?.Stop();
            control.CancelResponse();
            if (runtimeStop is not null)
            {
                await runtimeStop.CancelAsync();
                if (inputTask is not null && providerTask is not null) await AwaitQuietly(inputTask, providerTask);
                runtimeStop.Dispose();
            }
            benchmarkSession?.Dispose();
            events.Writer.TryComplete();
            control.CancelResponse();
            if (activeSpeech is not null) await AwaitQuietly(activeSpeech);
            if (groundedWork is not null) await AwaitQuietly(groundedWork);
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
        ChannelWriter<RuntimeEvent> writer, CancellationToken ct, bool semanticProfile = false)
    {
        try
        {
            await foreach (var frame in media.ReceiveAsync(ct))
            {
                if (semanticProfile)
                {
                    await writer.WriteAsync(new(RuntimeEventKind.RawFrame, Frame: frame with { Samples = frame.Samples.ToArray() }), ct);
                    continue;
                }
                var result = segmenter.Push(frame);
                // Local onset is a candidate only. Provider-confirmed words decide whether
                // a presentation turn may be stopped.
                if (result.SpeechStarted)
                    await writer.WriteAsync(new(RuntimeEventKind.CandidateStarted, ParticipantId: frame.ParticipantId,
                        TrackId: frame.TrackId, TrackGeneration: frame.TrackGeneration), ct);
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
                        ProviderEventId: value.ProviderEventId, ProviderSequence: value.Sequence, ProviderTurnId: value.TurnId), ct);
            }
            // A closed/expired socket is not silence. Stop the AI lane so a new host-started
            // session cannot inherit old provider context, usage, or a stale answer.
            if (!ct.IsCancellationRequested) await writer.WriteAsync(new(RuntimeEventKind.ProviderFailure), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning("Room realtime stream ended. FailureType={FailureType} Code={Code}", ex.GetType().Name,
                ex is RealtimeAgentEventException provider ? provider.Code : "provider_stream_failed");
            writer.TryWrite(new(RuntimeEventKind.ProviderFailure));
        }
    }

    private async Task SpeakInOwnScopeAsync(Guid speechId, ISalesRoomMediaConnection media,
        SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, long interruptionVersion, CancellationToken ct)
    {
        await using var scope = captureScopes.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<SalesRoomAgentWorker>();
        await worker.SpeakQueuedAsync(speechId, media, work, control, interruptionVersion, ct);
    }

    internal async Task SpeakQueuedAsync(Guid speechId, ISalesRoomMediaConnection media,
        SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control, long interruptionVersion, CancellationToken ct)
    {
        using var companyScope = executionScopes.BeginScope(work.CompanyId);
        var room = await RoomAsync(work, ct);
        var item = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == speechId, ct);
        if (item is null || item.Status != SalesRoomAgentSpeechStates.Queued ||
            !room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
        logger.LogInformation("MeetingTrace Stage=queued_speech_start RoomId={RoomId} SpeechId={SpeechId} TurnId={TurnId} Kind={Kind} TurnGeneration={TurnGeneration} ResponseGeneration={ResponseGeneration}",
            room.Id, item.Id, item.CommandId, item.Kind, item.TurnGeneration, item.ResponseGeneration);
        await SpeakAsync(room, item, media, work, control, interruptionVersion, ct);
        var persistedStatus = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == speechId).Select(x => x.Status).SingleAsync(ct);
        logger.LogInformation("MeetingTrace Stage=queued_speech_finished RoomId={RoomId} SpeechId={SpeechId} Kind={Kind} Status={Status}",
            room.Id, item.Id, item.Kind, persistedStatus);
        await db.SaveChangesAsync(ct);
    }

    private async Task SpeakAsync(SalesBrowserRoom room, SalesRoomAgentSpeech item,
        ISalesRoomMediaConnection media, SalesRoomAgentWorkItem work, SalesRoomAgentRunControl control,
        long interruptionVersion, CancellationToken ct)
    {
        using var responseStop = await control.BeginResponseAsync(ct, interruptionVersion);
        var responseCt = responseStop.Token;
        if (responseCt.IsCancellationRequested) { control.EndResponse(responseStop); return; }
        byte[] bytes; string? text = null, evidence = null, response = null;
        var inputTokens = 0; var outputTokens = 0;
        var generatedMilliseconds = 0;
        var published = 0;
        var deliveryStart = 0;
        var playbackCompleted = false;
        AgentConversation? conversation = null;
        ValidatedConversationBridge? proposedBridge = null;
        SalesMeetingQuestion? answeredForBridge = null;
        long? releasedVersion = null;
        try
        {
            item.Claim(Now); await db.SaveChangesAsync(responseCt);
            if (db.Entry(room).State == EntityState.Detached)
                room = await RoomAsync(work, responseCt);
            else
                await db.Entry(room).ReloadAsync(responseCt);
            if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) || item.TurnGeneration != room.AgentTurnGeneration)
                throw new WithheldSpeech("turn_fenced", "The requested speech belongs to an obsolete room turn.");
            if (item.Kind == SalesRoomAgentSpeechKinds.Bridge && !Options.HybridConversationEnabled)
                throw new WithheldSpeech("conversation_disabled", "Realtime conversation was turned off before the follow-up could play.");
            await EnsureFloorAsync(room, item, responseCt);
            await EnsureConsentAsync(room, responseCt);
            if (Options.HybridConversationEnabled)
            {
                var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id, responseCt);
                var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId, room.Id,
                    floor.PendingParticipantId ?? floor.HostParticipantId, Now, responseCt)
                    ?? throw new WithheldSpeech("conversation_unavailable", "Conversation access changed before playback.");
                conversation = new(authority.Binding, item.Kind switch
                {
                    SalesRoomAgentSpeechKinds.Answer or SalesRoomAgentSpeechKinds.Limitation => AgentConversationPhase.SpeakingAnswer,
                    SalesRoomAgentSpeechKinds.Bridge => AgentConversationPhase.WaitingForReply,
                    _ => AgentConversationPhase.Presenting
                });
                await EnsureConversationAsync(conversation, room, item, responseCt);
            }
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
                var audienceReady = await AudienceReadyAsync(room, plan.Snapshot.Stage.Version, responseCt);
                if (!audienceReady && await db.SalesRoomOperations.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.CommandId == item.CommandId &&
                    (x.Action == "dialogue_start_presentation" || x.Action == "dialogue_resume_presentation"), responseCt))
                {
                    // Stage publication precedes real browser render acknowledgements. Keep the
                    // durable queue alive briefly, without weakening readiness or microphone input.
                    audienceReady = await RunWithLeaseRenewalAsync(work, async token =>
                    {
                        for (var retry = 0; retry < 20; retry++)
                        {
                            await Task.Delay(250, token);
                            await EnsureFloorAsync(room, item, token); await EnsureConsentAsync(room, token);
                            if (await AudienceReadyAsync(room, plan.Snapshot.Stage.Version, token)) return true;
                        }
                        return false;
                    }, responseCt);
                }
                if (!audienceReady)
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
            else if (item.Kind is SalesRoomAgentSpeechKinds.Answer or SalesRoomAgentSpeechKinds.Limitation)
            {
                var question = await ReleasedQuestionAsync(room, item, responseCt);
                await EnsureCurrentEvidenceAsync(room, question, responseCt);
                releasedVersion = question.ConcurrencyVersion;
                if (item.Kind == SalesRoomAgentSpeechKinds.Answer) answeredForBridge = question;
                text = item.Kind == SalesRoomAgentSpeechKinds.Limitation
                    ? question.AnswerText!
                    : Options.HybridConversationEnabled ? question.AnswerText! : QuestionAcknowledgement + question.AnswerText!;
                evidence = JsonSerializer.Serialize(question.Evidence.Select(x => new { x.SourceId, x.SourceType, x.SourceTitle }).Distinct());
                var profile = await speech.GetProfileAsync(responseCt);
                if (!profile.Available) throw new WithheldSpeech("voice_unavailable", "Approved-text speech is unavailable.");
                var language = await db.SalesNarrationRevisions.AsNoTracking().Where(x => x.CompanyId == room.CompanyId &&
                        x.SessionId == room.MeetingSessionId && x.ApprovedUtc != null && x.RevokedUtc == null && x.RetainUntilUtc > Now)
                    .OrderByDescending(x => x.ApprovedUtc).Select(x => x.Language).FirstOrDefaultAsync(ct) ?? "en";
                var generated = await GenerateReleasedSpeechAsync(new(room.CompanyId, item.RequestedByUserId, item.AgentId,
                    text, language, profile.Voice, profile.ConfigurationVersion, item.Id.ToString("N")), responseCt);
                if (!generated.ContentMatches) throw new WithheldSpeech(generated.FailureCode ?? "speech_content_mismatch",
                    generated.FailureCode == "speech_incomplete" ? "The speech provider did not finish the approved answer. Retry Speak approved answer." :
                    generated.FailureCode == "speech_empty" ? "The speech provider returned no audio. Retry Speak approved answer." :
                    "Generated audio did not match the released answer.");
                db.ChangeTracker.Clear(); room = await RoomAsync(work, ct);
                var currentQuestion = await ReleasedQuestionAsync(room, item, responseCt);
                if (currentQuestion.ConcurrencyVersion != question.ConcurrencyVersion || currentQuestion.AnswerText != question.AnswerText)
                    throw new WithheldSpeech("answer_changed", "The approved answer changed while audio was being generated. Review and approve the current answer.");
                await EnsureCurrentEvidenceAsync(room, currentQuestion, responseCt);
                await EnsureFloorAsync(room, item, responseCt);
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) || item.TurnGeneration != room.AgentTurnGeneration)
                    throw new WithheldSpeech("turn_fenced", "The room turn changed before answer publication.");
                bytes = generated.Pcm; response = generated.ProviderResponseId;
                inputTokens = generated.InputTokens; outputTokens = generated.OutputTokens;
            }
            else if (item.Kind == SalesRoomAgentSpeechKinds.Bridge)
            {
                var question = await ReleasedQuestionAsync(room, item, responseCt);
                await EnsureCurrentEvidenceAsync(room, question, responseCt);
                releasedVersion = question.ConcurrencyVersion;
                var validation = ReadBridgeEvidence(item);
                if (validation is null || validation.AnswerVersion != question.ConcurrencyVersion ||
                    validation.AnswerHash != HashAnswer(question.AnswerText!) ||
                    !ConversationBridgePolicy.IsStructurallySafe(item.ReleasedText))
                    throw new WithheldSpeech("bridge_release_changed", "The conversational follow-up is no longer valid.");
                text = item.ReleasedText;
                evidence = item.EvidenceJson;
                var profile = await speech.GetProfileAsync(responseCt);
                if (!profile.Available) throw new WithheldSpeech("voice_unavailable", "Conversational speech is unavailable.");
                var generated = await GenerateReleasedSpeechAsync(new(room.CompanyId, item.RequestedByUserId, item.AgentId,
                    text!, "en", profile.Voice, profile.ConfigurationVersion, item.Id.ToString("N")), responseCt);
                if (!generated.ContentMatches)
                    throw new WithheldSpeech(generated.FailureCode ?? "bridge_speech_mismatch",
                        "Generated audio did not match the validated conversational follow-up.");
                db.ChangeTracker.Clear(); room = await RoomAsync(work, responseCt);
                question = await ReleasedQuestionAsync(room, item, responseCt);
                if (validation.AnswerVersion != question.ConcurrencyVersion ||
                    validation.AnswerHash != HashAnswer(question.AnswerText!))
                    throw new WithheldSpeech("bridge_release_changed", "The grounded answer changed before follow-up playback.");
                await EnsureCurrentEvidenceAsync(room, question, responseCt);
                await EnsureFloorAsync(room, item, responseCt);
                bytes = generated.Pcm; response = generated.ProviderResponseId;
                inputTokens = generated.InputTokens; outputTokens = generated.OutputTokens;
            }
            else throw new WithheldSpeech("speech_kind_unknown", "This speech lane is not authorized.");
            if (bytes.Length == 0 || bytes.Length % 2 != 0 || bytes.Length > 24_000 * 2 * 120)
                throw new WithheldSpeech("invalid_approved_audio", "The approved audio was empty or outside the room limit.");
            var samples = MemoryMarshal.Cast<byte, short>(bytes).ToArray();
            generatedMilliseconds = checked((int)(samples.Length * 1000L / 24_000));
            // Object I/O and speech generation can outlive a lease renewal in the input worker.
            // Never publish using the room (or detached speech item) loaded before that wait.
            for (var attempt = 0; ; attempt++)
            {
                db.ChangeTracker.Clear(); room = await RoomAsync(work, responseCt);
                item = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync(x =>
                    x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == item.Id, responseCt);
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
                    room.AgentTurnGeneration != item.TurnGeneration || item.Status != SalesRoomAgentSpeechStates.Processing)
                    throw new WithheldSpeech("turn_fenced", "The requested speech belongs to an obsolete room turn.");
                await EnsureFloorAsync(room, item, responseCt);
                await EnsureConsentAsync(room, responseCt);
                room.RecordAgentAudio(work.LeaseOwnerId, work.Generation, 0, 0, 0, 0, inputTokens, outputTokens);
                room.AgentSpeaking(work.LeaseOwnerId, work.Generation);
                try { await db.SaveChangesAsync(responseCt); }
                catch (DbUpdateConcurrencyException) when (attempt < 3) { continue; }
                break;
            }
            await AlignOutputGenerationAsync(media, item.TurnGeneration, responseCt);
            deliveryStart = media.DeliveredMilliseconds(item.TurnGeneration) ?? 0;
            for (var offset = 0; offset < samples.Length; offset += 480)
            {
                // Recheck the durable floor/consent while streaming, not just after a long
                // generation or at end-of-playback. Never let mode downgrade drain old audio.
                if (offset % 4800 == 0)
                {
                    await EnsureFloorAsync(room, item, responseCt);
                    await EnsureConsentAsync(room, responseCt);
                    if (releasedVersion.HasValue &&
                        (await ReleasedQuestionAsync(room, item, responseCt)).ConcurrencyVersion != releasedVersion)
                        throw new WithheldSpeech("answer_changed", "The released answer changed during playback.");
                    if (conversation is not null) await EnsureConversationAsync(conversation, room, item, responseCt);
                }
                var length = Math.Min(480, samples.Length - offset);
                ReadOnlyMemory<short> frame = samples.AsMemory(offset, length);
                if (length < 480)
                {
                    var padded = new short[480]; frame.Span.CopyTo(padded); frame = padded;
                }
                if (!await media.SendAsync(item.TurnGeneration, 24_000, frame, responseCt))
                {
                    responseCt.ThrowIfCancellationRequested();
                    throw new WithheldSpeech("speech_interrupted", "Agent speech was stopped before completion.");
                }
                published += length;
                if (offset == 0 && item.Kind is SalesRoomAgentSpeechKinds.Answer or SalesRoomAgentSpeechKinds.Limitation)
                    control.Timing.Complete("confirmed_input_to_first_answer_frame", item.CommandId);
            }
            if (conversation is not null)
            {
                await EnsureFloorAsync(room, item, responseCt);
                await EnsureConversationAsync(conversation, room, item, responseCt);
            }
            if (!await media.CompleteSpeechAsync(item.TurnGeneration, responseCt))
            {
                responseCt.ThrowIfCancellationRequested();
                throw new WithheldSpeech("speech_interrupted", "Agent speech was stopped before completion.");
            }
            playbackCompleted = true;
            if (item.Kind == SalesRoomAgentSpeechKinds.Answer && item.QuestionId is Guid timingQuestion)
                control.Timing.Begin("answer_complete_to_listening", timingQuestion);
            var duration = checked((int)(published * 1000L / 24_000));
            SalesRoomBenchmarkTelemetry.RecordOutput(item.Kind, generatedMilliseconds, duration, 0);
            if (item.Kind == SalesRoomAgentSpeechKinds.Answer && Options.HybridConversationEnabled &&
                answeredForBridge is not null)
            {
                try
                {
                    var bridgeFloor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                        x.CompanyId == room.CompanyId && x.RoomId == room.Id, responseCt);
                    if (bridgeFloor is { State: SalesRoomFloorStates.Agent,
                            ControlMode: SalesPresentationControlModes.Autonomous } &&
                        bridgeFloor.ResponseGeneration == item.ResponseGeneration &&
                        bridgeFloor.PendingParticipantId is Guid bridgeParticipant)
                    {
                        var bridgeContext = new SalesRoomConversationContext(room.CompanyId, item.AgentId,
                            room.Id, item.SessionId, bridgeParticipant, answeredForBridge.Id,
                            answeredForBridge.QuestionText, answeredForBridge.AnswerText!,
                            answeredForBridge.Status == SalesMeetingQuestionStatus.PartiallySupported,
                            bridgeFloor.SlideNumber, bridgeFloor.TalkingPointIndex, bridgeFloor.ControlMode);
                        using var optionalFollowUp = CancellationTokenSource.CreateLinkedTokenSource(responseCt);
                        optionalFollowUp.CancelAfter(TimeSpan.FromSeconds(3));
                        try { proposedBridge = await conversationReasoner.ProposeBridgeAsync(bridgeContext, optionalFollowUp.Token); }
                        catch (OperationCanceledException) when (!responseCt.IsCancellationRequested)
                        { SalesRoomBenchmarkTelemetry.RecordConversationFailure("optional_follow_up_timeout"); }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Conversational bridge generation failed safely for room {RoomId}.", room.Id);
                }
            }
            // Retry only persistence, never audio. The input loop must keep renewing ownership
            // while a long narration is playing, so its original room version is obsolete.
            for (var attempt = 0; ; attempt++)
            {
                db.ChangeTracker.Clear(); room = await RoomAsync(work, responseCt);
                item = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync(x =>
                    x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == item.Id, responseCt);
                if (item.Status != SalesRoomAgentSpeechStates.Processing) return;
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) || room.AgentTurnGeneration != item.TurnGeneration)
                    throw new WithheldSpeech("turn_fenced", "The room turn changed before playback completed.");
                await EnsureFloorAsync(room, item, responseCt);
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
                var queueBridge = proposedBridge is not null && floor is not null &&
                    floor.ResponseGeneration == item.ResponseGeneration && floor.State == SalesRoomFloorStates.Agent &&
                    floor.ControlMode == SalesPresentationControlModes.Autonomous &&
                    floor.PendingParticipantId is Guid bridgeParticipantId &&
                    item.Kind == SalesRoomAgentSpeechKinds.Answer && item.QuestionId is Guid questionId;
                if (queueBridge)
                {
                    var answer = await ReleasedQuestionAsync(room, item, responseCt);
                    var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId, room.Id,
                        floor!.PendingParticipantId!.Value, Now, responseCt);
                    var decision = authority is null ? new AgentConversationDecision(false, "conversation_unavailable") :
                        new AgentConversation(authority.Binding, AgentConversationPhase.WaitingForReply)
                            .Authorize(authority, AgentConversationAction.SpeakBridge, Now, bridgeValidated: true);
                    queueBridge = decision.Allowed && answeredForBridge?.ConcurrencyVersion == answer.ConcurrencyVersion &&
                        answeredForBridge.AnswerText == answer.AnswerText &&
                        ConversationBridgePolicy.IsStructurallySafe(proposedBridge!.Text);
                    if (queueBridge)
                    {
                        var followUp = new SalesRoomAgentSpeech(Guid.NewGuid(), room.CompanyId, room.Id,
                            item.SessionId, Guid.NewGuid(), item.AgentId, work.Generation, room.AgentTurnGeneration,
                            SalesRoomAgentSpeechKinds.Bridge, item.RequestedByUserId, Now, questionId: item.QuestionId!.Value,
                            responseGeneration: floor!.ResponseGeneration);
                        followUp.PrepareBridge(proposedBridge!.Text, JsonSerializer.Serialize(new BridgeEvidence(
                            proposedBridge.ProposalRunId, proposedBridge.ValidationRunId,
                            answer.ConcurrencyVersion, HashAnswer(answer.AnswerText!))), Now);
                        db.SalesRoomAgentSpeech.Add(followUp);
                    }
                }
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
                    else if (!continueDeck && !queueBridge)
                    {
                        floor.AgentCompleted(floor.HostParticipantId, Now, nextSegment?.TalkingPoint,
                            preserveNarrationCheckpoint: item.Kind != SalesRoomAgentSpeechKinds.Narration);
                    }
                }
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), room.CompanyId, AuditActorTypes.User, item.RequestedByUserId,
                    "sales.browser_room.agent_spoke", "sales_room_agent_speech", item.Id.ToString("D"), AuditEventOutcomes.Succeeded,
                    "The room agent published one release-checked speech item to its shared voice track.",
                    item.Kind switch
                    {
                        SalesRoomAgentSpeechKinds.Answer => ["released answer", "question evidence"],
                        SalesRoomAgentSpeechKinds.Bridge => ["validated bridge", "released answer binding"],
                        SalesRoomAgentSpeechKinds.Limitation => ["safe limitation", "no factual claims"],
                        _ => ["approved narration asset", "audience binding"]
                    },
                    new Dictionary<string, string?> { ["roomId"] = room.Id.ToString("D"), ["kind"] = item.Kind,
                        ["questionId"] = item.QuestionId?.ToString("D"), ["segmentId"] = item.NarrationSegmentId?.ToString("D") },
                    item.CommandId.ToString("N"), Now));
                try { await db.SaveChangesAsync(responseCt); }
                catch (DbUpdateConcurrencyException) when (attempt < 3) { continue; }
                if (continueCurrentSlide)
                    await floorEvents.AllowPlaybackAsync(room.CompanyId, item.SessionId,
                        new(room.Id, floor!.ResponseGeneration), responseCt);
                else if (continueDeck)
                    await ContinueAutonomousDeckAsync(room.CompanyId, room.Id, item, work, responseCt);
                break;
            }
        }
        catch (OperationCanceledException) when (responseCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            if (playbackCompleted)
            {
                await PersistCompletedPlaybackAsync(item, work, text, evidence, response,
                    checked((int)(published * 1000L / 24_000)));
                return;
            }
            var played = Math.Clamp((media.DeliveredMilliseconds(item.TurnGeneration) ?? deliveryStart) - deliveryStart,
                0, checked((int)(published * 1000L / 24_000)));
            SalesRoomBenchmarkTelemetry.RecordOutput(item.Kind, generatedMilliseconds, played,
                Math.Max(0, generatedMilliseconds - played));
            var duration = item.OffsetMilliseconds + played;
            db.ChangeTracker.Clear();
            room = await RoomAsync(work, CancellationToken.None);
            var currentItem = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == room.CompanyId && x.Id == item.Id, CancellationToken.None);
            if (currentItem is not null && currentItem.Status == SalesRoomAgentSpeechStates.Processing)
                currentItem.Fail(SalesRoomAgentSpeechStates.Interrupted, "speech_interrupted",
                    "Human speech or takeover stopped this response.", Now);
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id, CancellationToken.None);
            var handoff = floor is not null && floor.ResponseGeneration == item.ResponseGeneration &&
                floor.State == SalesRoomFloorStates.Agent && floor.TurnGeneration == item.TurnGeneration &&
                room.AgentTurnGeneration == item.TurnGeneration && room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now);
            if (handoff)
                floor!.PauseAt(item.Kind == SalesRoomAgentSpeechKinds.Narration ? duration : floor.ResumeOffsetMilliseconds,
                    room.AgentTurnGeneration, Now);
            await db.SaveChangesAsync(CancellationToken.None);
            if (handoff) control.RecordConfirmedHandoff(responseStop, item.ResponseGeneration, floor!.ResponseGeneration);
        }
        catch (Exception error) when (error is WithheldSpeech or SalesNarrationException)
        {
            var ex = error as WithheldSpeech ?? new WithheldSpeech("narration_release_invalid", error.Message);
            SalesRoomBenchmarkTelemetry.RecordConversationFailure(ex.Code);
            if (playbackCompleted)
            {
                await PersistCompletedPlaybackAsync(item, work, text, evidence, response,
                    checked((int)(published * 1000L / 24_000)));
                return;
            }
            var played = Math.Clamp((media.DeliveredMilliseconds(item.TurnGeneration) ?? deliveryStart) - deliveryStart,
                0, checked((int)(published * 1000L / 24_000)));
            SalesRoomBenchmarkTelemetry.RecordOutput(item.Kind, generatedMilliseconds, played,
                Math.Max(0, generatedMilliseconds - played));
            db.ChangeTracker.Clear(); room = await RoomAsync(work, CancellationToken.None);
            item = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == item.Id, CancellationToken.None);
            if (item.Status != SalesRoomAgentSpeechStates.Processing) return;
            item.Fail(ex.Code == "speech_interrupted" ? SalesRoomAgentSpeechStates.Interrupted : SalesRoomAgentSpeechStates.Withheld,
                ex.Code, ex.Message, Now);
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == work.CompanyId && x.RoomId == work.RoomId, CancellationToken.None);
            // An obsolete narration/answer may finish its failure handler after a new
            // question has claimed the floor. Withhold only that old item: pausing the
            // room or cancelling its shared track would fence the new response too.
            var ownsTurn = room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) &&
                item.AgentGeneration == room.AgentGeneration && item.TurnGeneration == room.AgentTurnGeneration;
            var ownsResponse = ownsTurn &&
                floor is not null && floor.TurnGeneration == item.TurnGeneration &&
                floor.ResponseGeneration == item.ResponseGeneration;
            // A mode downgrade returns this same turn to the host. It must discard
            // buffered audio even though it has fenced the old floor response.
            var returnedToHost = ownsTurn && floor is { State: SalesRoomFloorStates.Host } &&
                floor.ControlMode != SalesPresentationControlModes.Autonomous;
            if (conversation is not null && published > 0 && (ownsResponse || returnedToHost))
                await media.CancelSpeechAsync(CancellationToken.None);
            if (ownsResponse)
            {
                var recoverable = Options.HybridConversationEnabled && Options.SemanticConversationInputEnabled &&
                    item.Kind != SalesRoomAgentSpeechKinds.Narration &&
                    ex.Code is ("speech_content_mismatch" or "speech_incomplete" or "speech_empty" or
                        "bridge_speech_mismatch" or "voice_unavailable" or "speech_provider_unavailable" or
                        "answer_source_changed" or "answer_changed" or "evidence_check_unavailable");
                if (recoverable)
                    room.AgentTurnFailed(work.LeaseOwnerId, work.Generation, ex.Code,
                        "That answer could not be safely spoken. Alex is still listening; please ask again or retry after review.");
                else room.PauseAgent(work.LeaseOwnerId, work.Generation, ex.Code, ex.Message,
                    ex.Code == "voice_unavailable" ? "degraded" : room.AgentVoiceHealth);
                if (floor!.State == SalesRoomFloorStates.Agent)
                    floor.PauseAt(item.Kind == SalesRoomAgentSpeechKinds.Narration
                            ? item.OffsetMilliseconds + played : floor.ResumeOffsetMilliseconds,
                        room.AgentTurnGeneration, Now);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally { control.EndResponse(responseStop); }
    }

    private async Task PersistCompletedPlaybackAsync(SalesRoomAgentSpeech item, SalesRoomAgentWorkItem work,
        string? text, string? evidence, string? providerResponse, int duration)
    {
        // A bridge proposal can be cancelled after the answer has audibly finished. Do not
        // rewrite that released answer as withheld or interrupted when only the next lane
        // lost authority. The bridge remains unqueued.
        db.ChangeTracker.Clear();
        var completed = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.Id == item.Id, CancellationToken.None);
        if (completed is null || completed.Status != SalesRoomAgentSpeechStates.Processing) return;
        completed.Complete(text, evidence, providerResponse, duration, Now);
        var currentRoom = await RoomAsync(work, CancellationToken.None);
        if (currentRoom.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now))
            currentRoom.AgentSpeechCompleted(work.LeaseOwnerId, work.Generation, duration);
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == work.CompanyId && x.RoomId == work.RoomId, CancellationToken.None);
        if (floor is not null && floor.State == SalesRoomFloorStates.Agent &&
            floor.TurnGeneration == completed.TurnGeneration && floor.ResponseGeneration == completed.ResponseGeneration)
            floor.AgentCompleted(floor.HostParticipantId, Now,
                preserveNarrationCheckpoint: completed.Kind != SalesRoomAgentSpeechKinds.Narration);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task EnsureFloorAsync(SalesBrowserRoom room, SalesRoomAgentSpeech item, CancellationToken ct)
    {
        var floor = await db.SalesRoomFloors.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
        if (floor is null || floor.State != SalesRoomFloorStates.Agent ||
            floor.TurnGeneration != item.TurnGeneration || floor.ResponseGeneration != item.ResponseGeneration)
            throw new WithheldSpeech("floor_fenced", "The floor changed before this response could be published.");
        if ((!Options.HybridConversationEnabled || floor.ControlMode != "autonomous") &&
            await db.SalesRoomOperations.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.CommandId == item.CommandId &&
                x.Action == "conversation_resume", ct))
            throw new WithheldSpeech("host_approval_required", "Automatic continuation is paused. Use the host controls to resume.");
    }

    private async Task EnsureConversationAsync(AgentConversation conversation, SalesBrowserRoom room,
        SalesRoomAgentSpeech item, CancellationToken ct)
    {
        var current = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId, room.Id,
            conversation.Binding.ParticipantId, Now, ct);
        var decision = current is null ? new AgentConversationDecision(false, "conversation_unavailable") : conversation.Check(current, Now);
        if (decision.Allowed && item.Kind is SalesRoomAgentSpeechKinds.Answer or SalesRoomAgentSpeechKinds.Limitation)
        {
            var question = await ReleasedQuestionAsync(room, item, ct);
            decision = conversation.Authorize(current!, AgentConversationAction.SpeakAnswer, Now,
                SalesRoomConversationPolicy.Supported(question), SalesRoomConversationPolicy.Released(question));
        }
        if (decision.Allowed && item.Kind == SalesRoomAgentSpeechKinds.Bridge)
        {
            var question = await ReleasedQuestionAsync(room, item, ct);
            var validation = ReadBridgeEvidence(item);
            var validated = validation is not null && validation.AnswerVersion == question.ConcurrencyVersion &&
                validation.AnswerHash == HashAnswer(question.AnswerText!) &&
                ConversationBridgePolicy.IsStructurallySafe(item.ReleasedText) &&
                await db.SalesRoomAgentSpeech.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id && x.SessionId == item.SessionId &&
                    x.QuestionId == question.Id && x.Kind == SalesRoomAgentSpeechKinds.Answer &&
                    x.Status == SalesRoomAgentSpeechStates.Spoken && x.AgentGeneration == item.AgentGeneration &&
                    x.TurnGeneration == item.TurnGeneration && x.ResponseGeneration == item.ResponseGeneration &&
                    x.CompletedUtc <= item.CreatedUtc && x.ReleasedText != null, ct);
            decision = conversation.Authorize(current!, AgentConversationAction.SpeakBridge, Now,
                bridgeValidated: validated);
        }
        if (!decision.Allowed) throw new WithheldSpeech(decision.Code, "Conversation authorization changed. Review the room before speaking again.");
    }

    private sealed record BridgeEvidence(Guid ProposalRunId, Guid ValidationRunId, long AnswerVersion, string AnswerHash,
        Guid? ReplyTurnId = null);

    private static BridgeEvidence? ReadBridgeEvidence(SalesRoomAgentSpeech item)
    {
        if (item.Kind != SalesRoomAgentSpeechKinds.Bridge || string.IsNullOrWhiteSpace(item.EvidenceJson)) return null;
        try
        {
            var evidence = JsonSerializer.Deserialize<BridgeEvidence>(item.EvidenceJson);
            return evidence is not null && evidence.ProposalRunId != Guid.Empty && evidence.ValidationRunId != Guid.Empty &&
                evidence.AnswerVersion > 0 && evidence.AnswerHash is { Length: 64 } ? evidence : null;
        }
        catch (JsonException) { return null; }
    }

    private static string HashAnswer(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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

    internal async Task HandleTranscriptAsync(SalesBrowserRoom room, SalesRoomParticipant participant,
        SalesRoomDetectedUtterance utterance, string transcript, bool interruptedAgent, bool transcriptRetained,
        ISalesRoomMediaConnection media, SalesRoomAgentWorkItem work, ISalesRoomFloorEventPublisher publisher,
        ISalesMeetingQuestionAnsweringService answering, CancellationToken ct, bool semanticQuestion = false,
        Task<SalesMeetingQuestionDto?>? preparedAnswer = null)
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
        if (!explicitlyAddressed && !utterance.Overlapped)
        {
            var admitted = await db.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking().Where(x =>
                    x.CompanyId == room.CompanyId && x.RoomId == room.Id &&
                    x.State == SalesRoomParticipantStates.Admitted)
                .Select(x => x.Id).ToListAsync(ct);
            connectedHumans = CountConnectedHumans(admitted, participant.Id, media.IsParticipantConnected);
        }
        var addressed = semanticQuestion || explicitlyAddressed || ShouldTreatInterruptedSpeechAsAddressedQuestion(
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
        if (Options.HybridConversationEnabled && addressed && transcriptRetained && !semanticQuestion &&
            !await IsSubstantiveQuestionAsync(room, participant.Id, transcript, work, ct)) return;
        var commandId = StableTurnId(room.Id, participant.Id, utterance.TrackId, utterance.TrackGeneration, utterance.StartedAt);
        AgentConversation? conversation = null;
        if (addressed && !utterance.Overlapped && transcriptRetained && room.AgentId is Guid selectedAgent &&
            room.MeetingSessionId is Guid sessionId)
        {
            conversation = await SalesRoomConversationPolicy.BeginInputAsync(db, Options, room.CompanyId,
                room.Id, participant.Id, commandId, Now, ct);
            var sequence = (await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.CompanyId == room.CompanyId && x.SessionId == sessionId).MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
            SalesMeetingQuestionDto? answer;
            var lookupStarted = Stopwatch.GetTimestamp();
            if (preparedAnswer is null) logger.LogInformation("MeetingTrace Stage=source_lookup_start RoomId={RoomId} TurnId={TurnId} AgentId={AgentId} CharacterCount={CharacterCount}",
                room.Id, commandId, selectedAgent, transcript.Length);
            try
            {
                answer = preparedAnswer is not null ? await preparedAnswer : await RunWithLeaseRenewalAsync(work, answerToken => answering.AskAsync(room.CompanyId, room.OrganizerUserId, sessionId,
                    new(commandId, sequence, selectedAgent, transcript.Trim(), "customer", participant.DisplayName, "browser_room"),
                    commandId.ToString("N"), answerToken), ct);
            }
            catch (Exception ex) when (ex is SalesMeetingCaptureValidationException or SalesMeetingCaptureConflictException or UnauthorizedAccessException)
            {
                // A rejected question is not a failed media session. Withhold this turn,
                // retain the lease and microphone connection, and allow a fresh question.
                // Cancellation, storage failures and transport failures still propagate.
                logger.LogWarning("Question withheld without leaving room {RoomId}: {FailureType}.",
                    work.RoomId, ex.GetType().Name);
                db.ChangeTracker.Clear();
                room = await RoomAsync(work, ct);
                if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now) ||
                    room.State != SalesBrowserRoomStates.Live || !await HasAllConsentAsync(room, ct)) return;
                var failureCode = ex is UnauthorizedAccessException ||
                    ex is SalesMeetingCaptureConflictException { Code: SalesMeetingCaptureProblemCodes.AgentNotAuthorized }
                    ? "question_authorization_changed" : "question_rejected";
                room.AgentTurnFailed(work.LeaseOwnerId, work.Generation, failureCode,
                    "That question could not be processed. Alex is still connected; please ask again.");
                floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync(x =>
                    x.CompanyId == work.CompanyId && x.RoomId == work.RoomId, ct);
                floor.AgentCompleted(floor.HostParticipantId, Now, preserveNarrationCheckpoint: true);
                floor.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
                var pending = await db.SalesRoomAgentSpeech.IgnoreQueryFilters().Where(x =>
                    x.CompanyId == work.CompanyId && x.RoomId == work.RoomId && x.AgentGeneration == work.Generation &&
                    (x.Status == SalesRoomAgentSpeechStates.Queued || x.Status == SalesRoomAgentSpeechStates.Processing)).ToListAsync(ct);
                foreach (var item in pending)
                    item.Fail(SalesRoomAgentSpeechStates.Interrupted, "question_rejected", "Question processing was rejected; speech was withheld.", Now);
                await db.SaveChangesAsync(ct);
                await media.CancelSpeechAsync(ct);
                return;
            }
            questionId = answer?.Id;
            if (preparedAnswer is null) logger.LogInformation("MeetingTrace Stage=source_lookup_complete RoomId={RoomId} TurnId={TurnId} QuestionId={QuestionId} ElapsedMs={ElapsedMs}",
                room.Id, commandId, questionId, Stopwatch.GetElapsedTime(lookupStarted).TotalMilliseconds);
            db.ChangeTracker.Clear();
            room = await RoomAsync(work, ct);
            if (!room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
            floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id, ct);
        }
        if (conversation is not null)
        {
            var decision = await SalesRoomConversationPolicy.RecheckAsync(db, Options, conversation, Now, ct);
            if (!decision.Allowed)
            {
                logger.LogInformation("Conversation release withheld for room {RoomId}: {Reason}.", room.Id, decision.Code);
                return;
            }
        }
        floor.ProposeTurn(participant.Id, participant.Generation, addressed, utterance.Overlapped, questionId, Now);
        await db.SaveChangesAsync(ct);
        if (floor.PendingTurnState != SalesRoomPendingTurnStates.Authorized || questionId is null ||
            room.AgentId is not Guid agent || room.MeetingSessionId is not Guid meetingId ||
            room.AgentLeaseOwnerId is not Guid owner) return;
        var question = await db.SalesMeetingQuestions.IgnoreQueryFilters().Include(x => x.Evidence).SingleAsync(x =>
            x.CompanyId == room.CompanyId && x.Id == questionId, ct);
        if (question.Status is SalesMeetingQuestionStatus.Failed or SalesMeetingQuestionStatus.Cancelled)
        {
            room.AgentTurnFailed(owner, work.Generation, question.FailureCode ?? "question_rejected",
                "That answer could not be prepared. Alex is still listening; please ask again.");
            floor.AgentCompleted(floor.HostParticipantId, Now, preserveNarrationCheckpoint: true);
            floor.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
            await db.SaveChangesAsync(ct);
            return;
        }
        if (!CanAutomaticallyRelease(question, floor.ControlMode))
        {
            logger.LogInformation("MeetingTrace Stage=answer_awaiting_release RoomId={RoomId} QuestionId={QuestionId} Mode={Mode} Status={Status} Visibility={Visibility}",
                room.Id, question.Id, floor.ControlMode, question.Status, question.Visibility);
            return;
        }
        if (Options.HybridConversationEnabled)
        {
            var authority = await SalesRoomConversationPolicy.LoadAsync(db, Options, room.CompanyId, room.Id, participant.Id, Now, ct);
            if (authority is null) return;
            var turn = new AgentConversation(authority.Binding, AgentConversationPhase.Retrieving);
            if (!turn.AnswerReady(turn.Version, authority, Now, SalesRoomConversationPolicy.Supported(question), false).Allowed) return;
        }
        if (question.Visibility != SalesMeetingAnswerVisibility.ApprovedForStage)
        {
            if (question.IsSafeNoEvidenceLimitation)
                question.ApproveSafeLimitationForStage(room.OrganizerUserId, question.ConcurrencyVersion, Now);
            else question.ApproveForStage(room.OrganizerUserId, question.ConcurrencyVersion, Now);
        }
        room.ResumeAgent(owner, work.Generation);
        floor.AuthorizeAgentResponse(floor.HostParticipantId, room.AgentTurnGeneration, Now);
        db.SalesRoomAgentSpeech.Add(new SalesRoomAgentSpeech(Guid.NewGuid(), room.CompanyId, room.Id, meetingId,
            commandId, agent, work.Generation, room.AgentTurnGeneration,
            question.IsSafeNoEvidenceLimitation ? SalesRoomAgentSpeechKinds.Limitation : SalesRoomAgentSpeechKinds.Answer,
            room.OrganizerUserId, Now, questionId: questionId, responseGeneration: floor.ResponseGeneration));
        await db.SaveChangesAsync(ct);
        await publisher.AllowPlaybackAsync(room.CompanyId, meetingId, new(room.Id, floor.ResponseGeneration), ct);
    }

    internal static DateTime? ReplyDeadlineAfterTool(bool accepted, string? tool, DateTime? originalDeadline, DateTime now) =>
        accepted && tool == SalesRoomConversationTools.Wait && originalDeadline > now ? originalDeadline : null;

    internal static bool IsAddressedQuestion(string text, string agentName)
        => IsAddressedToAgent(text, agentName) && IsQuestion(" " + text.Trim().ToLowerInvariant() + " ");

    internal static bool IsAddressedToAgent(string text, string agentName)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = " " + text.Trim().ToLowerInvariant() + " ";
        var firstName = agentName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        var addressed = !string.IsNullOrWhiteSpace(firstName) &&
                            Regex.IsMatch(value, $@"\b{Regex.Escape(firstName)}\b", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(value, @"\balex\b|\bsales agent\b|\bsäljagent\b", RegexOptions.CultureInvariant);
        return addressed;
    }

    internal static bool ShouldTreatInterruptedSpeechAsAddressedQuestion(string text, bool interruptedAgent,
        int connectedHumanCount, bool overlapped)
    {
        if (connectedHumanCount != 1 || overlapped || string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = " " + text.Trim().ToLowerInvariant() + " ";
        if (IsQuestion(normalized)) return true;
        if (!interruptedAgent) return false;

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

    internal static bool ShouldRouteContextualReply(bool enabled, bool explicitlyAddressed, bool autonomous,
        bool overlapped, int connectedHumanCount, string text) =>
        enabled && autonomous && !overlapped && (explicitlyAddressed || connectedHumanCount == 1) &&
        !string.IsNullOrWhiteSpace(text) && text.Length <= 2000;

    internal static bool CanReceiveSpeech(SalesRoomParticipant? participant, bool mediaConnected, DateTime now) =>
        participant is { State: SalesRoomParticipantStates.Admitted, AiProcessingAllowed: true } &&
        participant.ExpiresUtc > now && mediaConnected;

    internal static bool MatchesSpeechFloor(SalesRoomFloor? floor, long turn, long? response) =>
        floor is not null && floor.TurnGeneration == turn && response.HasValue &&
        (floor.ResponseGeneration == response ||
         floor.ResponseGeneration > response && floor.State == SalesRoomFloorStates.Agent &&
         floor.ControlMode == SalesPresentationControlModes.Autonomous && floor.PendingTurnId is null);

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

    internal static bool CanAutomaticallyRelease(SalesMeetingQuestion question, string controlMode) =>
        controlMode == SalesPresentationControlModes.Autonomous &&
        (question.IsSafeNoEvidenceLimitation ||
         question.Status is (SalesMeetingQuestionStatus.Completed or SalesMeetingQuestionStatus.PartiallySupported) &&
         !string.IsNullOrWhiteSpace(question.AnswerText) && question.Evidence.Count > 0);

    private async Task<SalesMeetingQuestion> ReleasedQuestionAsync(SalesBrowserRoom room, SalesRoomAgentSpeech item, CancellationToken ct)
    {
        var question = await db.SalesMeetingQuestions.IgnoreQueryFilters().AsNoTracking().Include(x => x.Evidence).SingleOrDefaultAsync(x =>
            x.CompanyId == room.CompanyId && x.SessionId == item.SessionId && x.Id == item.QuestionId && x.AgentId == item.AgentId, ct);
        if (question is null ||
            question.Visibility != SalesMeetingAnswerVisibility.ApprovedForStage || question.StageApprovedUtc is null ||
            (item.Kind == SalesRoomAgentSpeechKinds.Limitation
                ? !question.IsSafeNoEvidenceLimitation
                : question.Status is not (SalesMeetingQuestionStatus.Completed or SalesMeetingQuestionStatus.PartiallySupported) ||
                  string.IsNullOrWhiteSpace(question.AnswerText) || question.Evidence.Count == 0))
            throw new WithheldSpeech("answer_not_released", "The answer is no longer approved for customer-visible speech.");
        return question;
    }

    private async Task EnsureCurrentEvidenceAsync(SalesBrowserRoom room, SalesMeetingQuestion question, CancellationToken ct)
    {
        if (question.IsSafeNoEvidenceLimitation) return;
        try
        {
            if (!await questions.ValidateEvidenceAsync(room.CompanyId, room.OrganizerUserId, question.SessionId,
                    question.Id, question.ConcurrencyVersion, ct))
                throw new WithheldSpeech("answer_source_changed", "Approved evidence changed or is no longer accessible. Please ask the question again.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not WithheldSpeech)
        {
            logger.LogWarning(ex, "Answer evidence recheck failed safely for room {RoomId}.", room.Id);
            throw new WithheldSpeech("evidence_check_unavailable", "Approved evidence could not be rechecked. Alex is still listening.");
        }
    }

    private async Task<ApprovedSpeechResult> GenerateReleasedSpeechAsync(ApprovedSpeechRequest request, CancellationToken ct)
    {
        try { return await speech.GenerateAsync(request, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Approved answer speech generation failed safely.");
            throw new WithheldSpeech("speech_provider_unavailable", "The answer voice is temporarily unavailable. Alex is still listening.");
        }
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
    internal async Task<T> RunWithLeaseRenewalAsync<T>(SalesRoomAgentWorkItem work,
        Func<CancellationToken, Task<T>> providerOperation, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = providerOperation(operation.Token);
        try
        {
            while (!pending.IsCompleted)
            {
                var delay = Task.Delay(TimeSpan.FromSeconds(Options.RenewalSeconds), ct);
                if (await Task.WhenAny(pending, delay) == pending) break;

                await using var scope = captureScopes.CreateAsyncScope();
                var leaseDb = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
                var now = Now;
                var room = await leaseDb.SalesBrowserRooms.IgnoreQueryFilters().SingleAsync(x =>
                    x.CompanyId == work.CompanyId && x.Id == work.RoomId, ct);
                var consent = await leaseDb.SalesRoomParticipants.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == work.CompanyId && x.RoomId == work.RoomId &&
                        x.State == SalesRoomParticipantStates.Admitted)
                    .GroupBy(_ => 1).Select(x => new { Count = x.Count(), Allowed = x.Count(p => p.AiProcessingAllowed) })
                    .SingleOrDefaultAsync(ct);
                if (room.State != SalesBrowserRoomStates.Live || room.ExpiresUtc <= now)
                    throw new SalesRoomAgentException("room_expired", "The meeting ended while the agent was working.");
                if (consent is null || consent.Count != consent.Allowed)
                    throw new SalesRoomAgentException("consent_required", "Participant consent changed while the agent was working.");
                if (AdmissionBlocked() || SalesRoomOperationsPolicy.AudioLimitProblem(room, Options) is not null ||
                    room.AgentStartedUtc <= now.AddMinutes(-Options.MaximumSessionMinutes) ||
                    SalesRoomOperationsPolicy.EstimatedSpend(room, Options) >= Options.MaximumSpendPerCallUsd)
                    throw new SalesRoomAgentException("agent_policy_changed", "The agent's operating allowance or availability changed.");
                if (!room.RenewAgentLease(work.LeaseOwnerId, work.Generation,
                        now.AddSeconds(Options.LeaseSeconds), now))
                    throw new InvalidOperationException("The browser-room agent lease was lost during the provider operation.");
                await leaseDb.SaveChangesAsync(ct);
            }

            return await pending;
        }
        finally
        {
            await operation.CancelAsync();
            // Observe the operation before its DbContext scope can be disposed.
            if (!pending.IsCompleted)
            {
                try { await pending; }
                catch (Exception) when (operation.IsCancellationRequested) { }
            }
        }
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
                    code is "provider_recovery_exhausted" or "provider_recovery_denied"
                        ? "Voice reconnection stopped. Review consent, presenter access and room allowance, then use Start agent. Pending speech will not replay."
                        : "Room AI paused safely. The human call, manual slides, and typed questions remain available.");
                var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.CompanyId == work.CompanyId && x.RoomId == work.RoomId, CancellationToken.None);
                floor?.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not persist the safe pause for browser room {RoomId}.", work.RoomId); }
    }

    private bool AdmissionBlocked() => !configured.CurrentValue.Enabled || configured.CurrentValue.EmergencyDisabled ||
        configured.CurrentValue.DrainEnabled || !lifecycle.CurrentValue.Enabled || lifecycle.CurrentValue.DrainEnabled ||
        SalesRoomOperationsPolicy.ConfigurationProblem(configured.CurrentValue, Now) is not null;

    internal async Task StopForPolicyAsync(SalesBrowserRoom room, SalesRoomAgentWorkItem work, string code, string? summary = null)
    {
        if (room.CompanyId != work.CompanyId || room.Id != work.RoomId ||
            !room.IsAgentOwner(work.LeaseOwnerId, work.Generation, Now)) return;
        logger.LogInformation("MeetingTrace Stage=session_policy_stop RoomId={RoomId} OwnerGeneration={OwnerGeneration} Code={Code} InputAudioMs={InputAudioMs} OutputAudioMs={OutputAudioMs} InputTokens={InputTokens} OutputTokens={OutputTokens}",
            room.Id, work.Generation, code, room.AgentForwardedAudioMilliseconds, room.AgentOutputAudioMilliseconds, room.AgentInputTokens, room.AgentOutputTokens);
        room.StopAgent(code, summary ?? (code switch
        {
            "emergency_disabled" => "Room AI stopped immediately because the emergency disable was activated.",
            "duration_limit" => "Room AI stopped at the configured call-duration limit.",
            "spend_limit" => "Room AI stopped at the configured spend limit.",
            "conversation_disabled" => "Realtime conversation was turned off. Restart the agent to use the approved-answer mode; human calling and manual slides remain available.",
            _ => "Room AI stopped during an operational drain. Human calling and typed controls remain available."
        }), Now);
        {
            var floor = await db.SalesRoomFloors.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == room.CompanyId && x.RoomId == room.Id, CancellationToken.None);
            floor?.PauseAt(floor.ResumeOffsetMilliseconds, room.AgentTurnGeneration, Now);
        }
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
    private static async Task AwaitQuietly(params Task[] tasks)
    { try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static bool ShouldDeferHumanFloorTransition(bool interruptedAgent, string? floorState) =>
        !interruptedAgent && floorState == SalesRoomFloorStates.Agent;
    private enum RuntimeEventKind { RawFrame, CandidateStarted, Utterance, ProviderEvent, DetectorFailure, ProviderFailure, UnexpectedProviderAudio }
    private sealed record RuntimeEvent(RuntimeEventKind Kind, SalesRoomDetectedUtterance? Utterance = null,
        string? ProviderJson = null, string? ProviderEventId = null, long ProviderSequence = 0,
        Guid? ParticipantId = null, string? TrackId = null, long TrackGeneration = 0, Guid? ProviderTurnId = null,
        SalesRoomAudioFrame? Frame = null);
    private sealed record CandidateSnapshot(long ConsentVersion, long ParticipantGeneration,
        long TurnGeneration, long? ResponseGeneration, bool BeganDuringPresentation, DateTime StartedUtc);
    private sealed record UtteranceContext(SalesRoomDetectedUtterance Value, long ConsentVersion, bool Retain,
        long ParticipantGeneration, long TurnGeneration, long? ResponseGeneration, bool BeganDuringPresentation);
    private sealed class WithheldSpeech(string code, string message) : Exception(message) { public string Code { get; } = code; }
}
