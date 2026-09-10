using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class SalesPresentationNarrationPreemption : ISalesPresentationNarrationPreemption, IDisposable
{
    private readonly ConcurrentDictionary<(Guid CompanyId, Guid SessionId), Entry> _entries = new();

    public CancellationToken Bind(Guid companyId, Guid sessionId, long presentationVersion)
    {
        var key = (companyId, sessionId);
        while (true)
        {
            if (_entries.TryGetValue(key, out var existing) && existing.Version == presentationVersion)
                return existing.Source.Token;
            var replacement = new Entry(presentationVersion, new CancellationTokenSource());
            if (_entries.TryAdd(key, replacement)) return replacement.Source.Token;
            if (_entries.TryUpdate(key, replacement, existing!))
            {
                existing!.Source.Cancel();
                existing.Source.Dispose();
                return replacement.Source.Token;
            }
            replacement.Source.Dispose();
        }
    }

    public void Preempt(Guid companyId, Guid sessionId, long authoritativePresentationVersion)
    {
        var key = (companyId, sessionId);
        if (!_entries.TryRemove(key, out var entry)) return;
        entry.Source.Cancel();
        entry.Source.Dispose();
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) { entry.Source.Cancel(); entry.Source.Dispose(); }
        _entries.Clear();
    }

    private sealed record Entry(long Version, CancellationTokenSource Source);
}

internal sealed class SalesPresentationStagePresenceService : ISalesPresentationStagePresenceService
{
    private readonly ConcurrentDictionary<(Guid CompanyId, Guid SessionId, string ConnectionId), SalesPresentationStagePresence> _presence = new();
    private readonly ConcurrentDictionary<RenderKey, SalesPresentationRenderAcknowledgement> _acknowledgements = new();
    private readonly ConcurrentDictionary<RenderKey, TaskCompletionSource<SalesPresentationRenderAcknowledgement>> _waiters = new();

    public Task RegisterAsync(SalesPresentationStagePresence presence, CancellationToken cancellationToken)
    {
        Validate(presence.CompanyId, presence.SessionId, presence.DeckId);
        if (string.IsNullOrWhiteSpace(presence.ConnectionId) || presence.ConnectionId.Length > 200)
            throw new ArgumentException("A bounded stage connection id is required.", nameof(presence));
        _presence[(presence.CompanyId, presence.SessionId, presence.ConnectionId)] = presence with { Active = true };
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(Guid companyId, Guid sessionId, string connectionId, CancellationToken cancellationToken)
    {
        _presence.TryRemove((companyId, sessionId, connectionId), out _);
        return Task.CompletedTask;
    }

    public Task AcknowledgeAsync(SalesPresentationRenderAcknowledgement acknowledgement, CancellationToken cancellationToken)
    {
        Validate(acknowledgement.CompanyId, acknowledgement.SessionId, acknowledgement.DeckId);
        if (!_presence.TryGetValue((acknowledgement.CompanyId, acknowledgement.SessionId,
                acknowledgement.ConnectionId), out var presence) || !presence.Active ||
            presence.DeckId != acknowledgement.DeckId || presence.DeckVersion != acknowledgement.DeckVersion)
            throw new InvalidOperationException("An active stage connection with the exact deck binding is required.");
        var key = Key(acknowledgement);
        _acknowledgements.TryAdd(key, acknowledgement);
        if (_waiters.TryRemove(key, out var waiter)) waiter.TrySetResult(acknowledgement);
        return Task.CompletedTask;
    }

    public async Task<SalesPresentationRenderWaitResult> WaitForRenderAsync(
        SalesPresentationStageSnapshotDto expected, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await IsConnectedAsync(Guid.Empty, expected.SessionId, expected.DeckId, expected.DeckVersion, cancellationToken))
        {
            var matching = _presence.Values.Any(x => x.SessionId == expected.SessionId && x.DeckId == expected.DeckId &&
                x.DeckVersion == expected.DeckVersion && x.Active);
            if (!matching) return new(false, "stage_disconnected");
        }
        var presence = _presence.Values.FirstOrDefault(x => x.SessionId == expected.SessionId && x.DeckId == expected.DeckId &&
            x.DeckVersion == expected.DeckVersion && x.Active);
        if (presence is null) return new(false, "stage_disconnected");
        var key = new RenderKey(presence.CompanyId, expected.SessionId, expected.DeckId, expected.DeckVersion,
            expected.SlideNumber, expected.Sequence, expected.Version);
        if (_acknowledgements.TryGetValue(key, out var existing)) return new(true, "rendered", existing);
        var waiter = _waiters.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            var acknowledgement = await waiter.Task.WaitAsync(timeout, cancellationToken);
            return new(true, "rendered", acknowledgement);
        }
        catch (TimeoutException)
        {
            _waiters.TryRemove(key, out _);
            return new(false, "render_timeout");
        }
    }

    public Task<bool> IsConnectedAsync(Guid companyId, Guid sessionId, Guid deckId, int deckVersion, CancellationToken cancellationToken)
    {
        var result = companyId == Guid.Empty
            ? _presence.Values.Any(x => x.SessionId == sessionId && x.DeckId == deckId && x.DeckVersion == deckVersion && x.Active)
            : _presence.Values.Any(value => value.CompanyId == companyId && value.SessionId == sessionId &&
                value.Active && value.DeckId == deckId && value.DeckVersion == deckVersion);
        return Task.FromResult(result);
    }

    private static RenderKey Key(SalesPresentationRenderAcknowledgement value) =>
        new(value.CompanyId, value.SessionId, value.DeckId, value.DeckVersion, value.SlideNumber,
            value.PresentationSequence, value.PresentationVersion);
    private static void Validate(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Stage binding identifiers are required."); }
    private sealed record RenderKey(Guid CompanyId, Guid SessionId, Guid DeckId, int DeckVersion, int SlideNumber, long Sequence, long Version);
}

internal sealed class SalesMeetingPresentationConductor(
    ISalesPresentationRuntimeService runtime,
    ISalesPresentationStagePresenceService stage,
    ISalesPresentationNarrationPreemption preemption,
    IOptions<SalesPresentationConductorOptions> configured) : ISalesMeetingPresentationConductor
{
    private readonly ConcurrentDictionary<(Guid CompanyId, Guid SessionId), TransitionWindow> _windows = new();

    public async Task<SalesPresentationNarrationPlan?> PrepareAsync(
        SalesPresentationNarrationRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await runtime.GetCurrentAsync(request.CompanyId, request.OrganizerUserId, request.SessionId, cancellationToken);
        if (snapshot is null) return null;
        var mode = snapshot.Private.ControlMode;
        var tool = request.ToolName;

        if (!string.IsNullOrWhiteSpace(request.SearchQuery))
        {
            var matches = await runtime.SearchAsync(request.CompanyId, request.OrganizerUserId, request.SessionId,
                request.SearchQuery, cancellationToken);
            if (matches.Count != 1)
                return Plan(matches.Count == 0 ? "not_found" : "confirmation_required", "ambiguous_slide_search",
                    mode, snapshot, new(false, "not_waited"), false);
            tool = SalesPresentationToolNames.Goto;
            request = request with { SlideNumber = matches[0].SlideNumber };
        }

        if (!string.IsNullOrWhiteSpace(tool) && SalesPresentationToolNames.IsMutation(tool))
        {
            if (mode == SalesPresentationControlModes.Manual)
                return Plan("blocked", "manual_mode", mode, snapshot, new(false, "not_waited"), false);
            if (mode == SalesPresentationControlModes.Assisted)
                return Plan("recommended", "organizer_confirmation_required", mode, snapshot, new(false, "not_waited"), false);
            if (tool == SalesPresentationToolNames.Next && snapshot.Stage.SlideNumber >= snapshot.Stage.SlideCount)
                return Plan("discussion", "last_slide_reached", mode, snapshot, new(false, "not_waited"), false);
            EnforceTransitionLimits(request.CompanyId, request.SessionId, snapshot.Stage.Version);
            var result = await runtime.ExecuteAsync(request.CompanyId, request.OrganizerUserId, request.SessionId, tool,
                new SalesPresentationCommandRequest(Guid.NewGuid(), snapshot.Stage.Sequence + 1, snapshot.Stage.Version,
                    request.SlideNumber, request.TalkingPointIndex, request.ResumeMarker,
                    SalesPresentationCommandActorTypes.Agent, snapshot.Stage.DeckId, request.AgentId,
                    snapshot.Stage.DeckVersion), request.CorrelationId, cancellationToken);
            if (result is null) return null;
            snapshot = result.Snapshot;
        }

        var connected = await stage.IsConnectedAsync(request.CompanyId, request.SessionId, snapshot.Stage.DeckId,
            snapshot.Stage.DeckVersion, cancellationToken);
        if (!connected)
            return Plan("degraded", "stage_disconnected", mode, snapshot, new(false, "stage_disconnected"), false);
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(configured.Value.RenderTimeoutMilliseconds, 250, 10_000));
        var rendered = await stage.WaitForRenderAsync(snapshot.Stage, timeout, cancellationToken);
        return Plan(rendered.Acknowledged ? "ready" : "degraded",
            rendered.Acknowledged ? null : rendered.Status, mode, snapshot, rendered, rendered.Acknowledged);
    }

    public CancellationToken GetNarrationCancellation(Guid companyId, Guid sessionId, long presentationVersion) =>
        preemption.Bind(companyId, sessionId, presentationVersion);

    public void Preempt(Guid companyId, Guid sessionId, long authoritativePresentationVersion) =>
        preemption.Preempt(companyId, sessionId, authoritativePresentationVersion);

    private void EnforceTransitionLimits(Guid companyId, Guid sessionId, long version)
    {
        var now = DateTime.UtcNow;
        var options = configured.Value;
        var key = (companyId, sessionId);
        var prior = _windows.GetOrAdd(key, _ => new(0, DateTime.MinValue, version));
        if (prior.LastTransitionUtc != DateTime.MinValue && now - prior.LastTransitionUtc < TimeSpan.FromSeconds(options.MinimumSlideDwellSeconds))
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.QuotaExceeded, "The minimum autonomous slide dwell time has not elapsed.");
        if (prior.ConsecutiveTransitions >= options.MaximumConsecutiveSlideTransitions)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.QuotaExceeded, "The autonomous consecutive-transition limit was reached.");
        _windows[key] = new(prior.ConsecutiveTransitions + 1, now, version);
    }

    private static SalesPresentationNarrationPlan Plan(string disposition, string? reason, string mode,
        SalesPresentationAuthoritativeSnapshotDto snapshot, SalesPresentationRenderWaitResult render, bool mayNarrate)
    {
        var points = snapshot.Private.PlanArtifacts
            .Where(x => string.Equals(x.ArtifactType, "slide_talking_point", StringComparison.Ordinal))
            .OrderBy(x => x.Order).Select(x => x.Content).ToArray();
        return new(disposition, reason, mode, snapshot, render, snapshot.Private.Objective, points,
            snapshot.Private.TalkingPointIndex, snapshot.Private.ResumeMarker, snapshot.Private.TransitionText, mayNarrate);
    }

    private sealed record TransitionWindow(int ConsecutiveTransitions, DateTime LastTransitionUtc, long Version);
}
