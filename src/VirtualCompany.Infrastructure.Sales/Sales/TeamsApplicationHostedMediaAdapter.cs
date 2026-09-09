using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Skype.Bots.Media;
using Newtonsoft.Json;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal interface ITeamsAudioSocketPlatform
{
    string SdkVersion { get; }
    bool IsInitialized { get; }
    bool HasPreparedCall(Guid localCallId);
    void SetIngress(Func<Guid, long, DateTime, byte[], bool, string?, Task> ingress);
    Task<TeamsCallMediaPreparation> PrepareAsync(Guid localCallId, string mediaHostInstanceId, CancellationToken cancellationToken);
    Task BindProviderCallAsync(Guid localCallId, string providerCallId, CancellationToken cancellationToken);
    Task SendAsync(Guid localCallId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
    Task CancelAsync(Guid localCallId, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid localCallId, CancellationToken cancellationToken);
}

internal sealed class MicrosoftTeamsAudioSocketPlatform(
    IOptions<TeamsPresenterOptions> configured,
    ILogger<MicrosoftTeamsAudioSocketPlatform> logger) : ITeamsAudioSocketPlatform, ITeamsCallMediaPreparationProvider, IDisposable
{
    private readonly ConcurrentDictionary<Guid, SocketEntry> _sockets = new();
    private readonly object _initializationLock = new();
    private Func<Guid, long, DateTime, byte[], bool, string?, Task>? _ingress;
    private bool _initialized;
    public string SdkVersion => typeof(MediaPlatform).Assembly.GetName().Version?.ToString() ?? "unknown";
    public bool IsInitialized => _initialized;
    public bool HasPreparedCall(Guid localCallId) => _sockets.ContainsKey(localCallId);

    public void SetIngress(Func<Guid, long, DateTime, byte[], bool, string?, Task> ingress) => _ingress = ingress;

    public Task<TeamsCallMediaPreparation> PrepareAsync(Guid localCallId, string mediaHostInstanceId, CancellationToken cancellationToken)
    {
        var options = configured.Value;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Teams application-hosted media requires Windows Server.");
        if (!string.Equals(mediaHostInstanceId, options.CallControlHostId, StringComparison.Ordinal))
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.HostMismatch, "The call is pinned to another media host instance.");
        EnsureInitialized(options);
        var entry = _sockets.GetOrAdd(localCallId, id => CreateSocket(id));
        return Task.FromResult(new TeamsCallMediaPreparation("#microsoft.graph.appHostedMediaConfig", entry.ConfigurationBlob));
    }

    public Task BindProviderCallAsync(Guid localCallId, string providerCallId, CancellationToken cancellationToken)
    {
        if (!_sockets.TryGetValue(localCallId, out var entry))
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.SessionNotFound, "The pinned media socket was not prepared.");
        entry.ProviderCallId = string.IsNullOrWhiteSpace(providerCallId) ? throw new ArgumentException("Provider call id is required.") : providerCallId;
        return Task.CompletedTask;
    }

    public Task SendAsync(Guid localCallId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (!_sockets.TryGetValue(localCallId, out var entry))
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.SessionNotFound, "The pinned media socket is unavailable.");
        cancellationToken.ThrowIfCancellationRequested();
        entry.Socket.Send(new OwnedAudioMediaBuffer(data.Span, MediaPlatform.GetCurrentTimestamp()));
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid localCallId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReleaseAsync(Guid localCallId, CancellationToken cancellationToken)
    {
        if (_sockets.TryRemove(localCallId, out var entry)) entry.Dispose();
        return Task.CompletedTask;
    }

    private void EnsureInitialized(TeamsPresenterOptions options)
    {
        if (_initialized) return;
        lock (_initializationLock)
        {
            if (_initialized) return;
            if (!Guid.TryParse(options.BotApplicationId, out var applicationId) || applicationId == Guid.Empty ||
                !IPAddress.TryParse(options.MediaHostPublicIp, out var publicIp) ||
                string.IsNullOrWhiteSpace(options.MediaHostServiceFqdn) ||
                string.IsNullOrWhiteSpace(options.MediaCertificateThumbprint))
                throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.NotReady,
                    "The Windows media host IP, FQDN, certificate, and bot application id must be configured.");
            MediaPlatform.Initialize(new MediaPlatformSettings
            {
                ApplicationId = applicationId.ToString("D"),
                MediaPlatformInstanceSettings = new MediaPlatformInstanceSettings
                {
                    CertificateThumbprint = options.MediaCertificateThumbprint,
                    InstanceInternalPort = options.MediaHostInternalPort,
                    InstancePublicIPAddress = publicIp,
                    InstancePublicPort = options.MediaHostPublicPort,
                    ServiceFqdn = options.MediaHostServiceFqdn
                }
            });
            _initialized = true;
            logger.LogInformation("Initialized the approved Teams audio media platform on host {MediaHostId} with SDK {SdkVersion}.",
                options.CallControlHostId, SdkVersion);
        }
    }

    private SocketEntry CreateSocket(Guid localCallId)
    {
        var socket = new AudioSocket(new AudioSocketSettings
        {
            CallId = localCallId.ToString("D"),
            SupportedAudioFormat = AudioFormat.Pcm16K,
            StreamDirections = StreamDirection.Sendrecv,
            ReceiveUnmixedMeetingAudio = false
        });
        var entry = new SocketEntry(socket, MediaPlatform.CreateMediaConfiguration(socket).ToString(Formatting.None));
        socket.AudioMediaReceived += (_, args) => OnAudioReceived(localCallId, entry, args);
        socket.MediaStreamFailure += (_, _) => logger.LogWarning("The Teams audio stream failed for a pinned local call.");
        return entry;
    }

    private void OnAudioReceived(Guid localCallId, SocketEntry entry, AudioMediaReceivedEventArgs args)
    {
        var buffer = args.Buffer;
        try
        {
            if (buffer.Length is <= 0 or > 640 || buffer.Data == IntPtr.Zero) return;
            var data = new byte[buffer.Length];
            Marshal.Copy(buffer.Data, data, 0, data.Length);
            var sequence = Interlocked.Increment(ref entry.ReceiveSequence);
            var participant = buffer.ActiveSpeakers is { Length: 1 } ? $"media-source:{buffer.ActiveSpeakers[0]}" : null;
            var ingress = _ingress;
            if (ingress is not null) _ = ingress(localCallId, sequence, DateTime.UtcNow, data, buffer.IsSilence, participant);
        }
        finally { buffer.Dispose(); }
    }

    public void Dispose()
    {
        foreach (var entry in _sockets.Values) entry.Dispose();
        _sockets.Clear();
        if (_initialized) { MediaPlatform.Shutdown(); _initialized = false; }
    }

    private sealed class SocketEntry(AudioSocket socket, string configurationBlob) : IDisposable
    {
        public AudioSocket Socket { get; } = socket;
        public string ConfigurationBlob { get; } = configurationBlob;
        public string? ProviderCallId { get; set; }
        public long ReceiveSequence;
        public void Dispose() => Socket.Dispose();
    }

    private sealed class OwnedAudioMediaBuffer : AudioMediaBuffer
    {
        private bool _disposed;
        public OwnedAudioMediaBuffer(ReadOnlySpan<byte> data, long timestamp)
        {
            Data = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data.ToArray(), 0, Data, data.Length);
            Length = data.Length;
            Timestamp = timestamp;
            AudioFormat = AudioFormat.Pcm16K;
        }
        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            Marshal.FreeHGlobal(Data);
            Data = IntPtr.Zero;
            _disposed = true;
        }
    }
}

internal sealed class TeamsMeetingMediaBindingAuthorizer(VirtualCompanyDbContext db, ITeamsPresenterRolloutPolicy rollout, ITeamsMeetingPresenterService presenters) : ITeamsMeetingMediaBindingAuthorizer
{
    public async Task AuthorizeAsync(TeamsMeetingMediaBinding binding, CancellationToken cancellationToken)
    {
        if (new[] { binding.CompanyId, binding.MeetingSessionId, binding.TeamsCallId, binding.VoiceSessionId, binding.AgentId }.Any(x => x == Guid.Empty))
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.BindingRejected, "The media binding is incomplete.");
        var call = await db.TeamsMeetingCalls.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == binding.CompanyId && x.Id == binding.TeamsCallId && x.MeetingSessionId == binding.MeetingSessionId,
            cancellationToken);
        var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == binding.CompanyId && x.Id == binding.MeetingSessionId, cancellationToken);
        var voice = await db.SalesMeetingVoiceSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == binding.CompanyId && x.Id == binding.VoiceSessionId && x.MeetingSessionId == binding.MeetingSessionId && x.AgentId == binding.AgentId,
            cancellationToken);
        if (call is null || meeting is null || voice is null ||
            call.ProviderCallId != binding.ProviderCallId || call.MediaHostInstanceId != binding.MediaHostInstanceId ||
            call.ConsentEvidenceVersion != binding.ConsentVersion ||
            call.State is not (TeamsMeetingCallStates.Admitted or TeamsMeetingCallStates.Connected) ||
            voice.EndedUtc.HasValue)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.BindingRejected, "The durable Teams media binding could not be proved.");
        if (!await db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == binding.CompanyId &&
            m.UserId == call.OrganizerUserId && m.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.BindingRejected, "The organizer membership is no longer active.");
        var decision = await rollout.EvaluateAsync(binding.CompanyId, call.OrganizerUserId, null, cancellationToken, binding.MeetingSessionId);
        if (!decision.Allowed || !call.MediaStartAuthorizedUtc.HasValue || call.PresenterAgentId != binding.AgentId ||
            (await presenters.ResolveAsync(binding.CompanyId, binding.MeetingSessionId, cancellationToken)).AgentId != binding.AgentId)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.BindingRejected, "Meeting audio authorization changed.");
        if (meeting.ConsentStatus != SalesMeetingConsentStatus.Granted || meeting.RetentionUntilUtc <= DateTime.UtcNow)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.ConsentRequired, "Meeting-media consent is not currently granted.");
    }
}

internal sealed class TeamsApplicationHostedMediaAdapter : ITeamsMeetingMediaAdapter, ITeamsMeetingMediaIngress
{
    private static readonly Meter Meter = new("VirtualCompany.Sales.TeamsMedia", "1.0.0");
    private static readonly Counter<long> FramesDropped = Meter.CreateCounter<long>("teams.media.frames.dropped");
    private static readonly Histogram<double> FrameJitter = Meter.CreateHistogram<double>("teams.media.frame.jitter", "ms");
    private static readonly Histogram<double> SetupLatency = Meter.CreateHistogram<double>("teams.media.setup.latency", "ms");
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();
    private readonly ConcurrentDictionary<Guid, Guid> _sessionByCall = new();
    private readonly ITeamsAudioSocketPlatform _platform;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<TeamsPresenterOptions> _configured;

    public TeamsApplicationHostedMediaAdapter(ITeamsAudioSocketPlatform platform, IServiceScopeFactory scopes,
        IOptions<TeamsPresenterOptions> configured)
    {
        _platform = platform; _scopes = scopes; _configured = configured;
        platform.SetIngress(OnProviderAudioAsync);
    }

    public Task<TeamsMeetingMediaHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        var o = _configured.Value;
        var route = o.MediaRoute?.Trim().ToLowerInvariant() ?? "disabled";
        var approved = o.MediaRouteApproved && route == "teams_application_hosted";
        var configured = OperatingSystem.IsWindows() && Guid.TryParse(o.BotApplicationId, out var appId) && appId != Guid.Empty &&
            IPAddress.TryParse(o.MediaHostPublicIp, out _) && !string.IsNullOrWhiteSpace(o.MediaHostServiceFqdn) &&
            !string.IsNullOrWhiteSpace(o.MediaCertificateThumbprint) && o.CallControlHostId != "unassigned";
        var available = o.Enabled && o.AudioEnabled && approved && configured;
        var reason = !o.Enabled || !o.AudioEnabled ? "audio_disabled" : !approved ? "media_route_not_approved" :
            !OperatingSystem.IsWindows() ? "unsupported_os" : !configured ? "media_host_not_configured" : null;
        return Task.FromResult(new TeamsMeetingMediaHealth(o.Enabled && o.AudioEnabled, approved, available, route,
            available ? "available" : "degraded", reason,
            available ? null : "Teams audio is unavailable; typed and manual presentation controls remain available.", _platform.SdkVersion));
    }

    public async Task<TeamsMeetingMediaSession> AttachAsync(TeamsMeetingMediaAttachRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var health = await GetHealthAsync(cancellationToken);
        if (!health.Available) throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.NotReady, health.Message ?? "Teams media is unavailable.");
        ValidateFormat(request.Format, ReadOnlyMemory<byte>.Empty, validateData: false);
        if (request.MaximumBufferedFrames is < 5 or > 500 || request.MaximumDuration <= TimeSpan.Zero)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.BindingRejected, "Media buffer and duration limits are invalid.");
        await AuthorizeAsync(request.Binding, cancellationToken);
        if (request.Binding.MediaHostInstanceId != _configured.Value.CallControlHostId)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.HostMismatch, "The media session belongs to another host.");
        var id = request.Binding.VoiceSessionId;
        var channel = Channel.CreateBounded<TeamsAudioFrame>(new BoundedChannelOptions(request.MaximumBufferedFrames)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
        var now = DateTime.UtcNow;
        var state = new SessionState(id, request.Binding, request.Format, channel, now,
            now.Add(request.MaximumDuration), TeamsMeetingMediaStates.Active);
        if (!_sessions.TryAdd(id, state))
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.BindingRejected, "The Teams media session is already attached.");
        _sessionByCall[request.Binding.TeamsCallId] = id;
        SetupLatency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return state.Snapshot(health.Route);
    }

    public async IAsyncEnumerable<TeamsAudioFrame> ReceiveAudioAsync(Guid mediaSessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = Get(mediaSessionId);
        await foreach (var frame in state.Incoming.Reader.ReadAllAsync(cancellationToken)) yield return frame;
    }

    public async Task<TeamsMediaWriteResult> SendAudioAsync(TeamsAudioFrame frame, CancellationToken cancellationToken)
    {
        var state = Get(frame.MediaSessionId);
        await AuthorizeAsync(state.Binding, cancellationToken);
        EnsureActive(state);
        ValidateFormat(frame.Format, frame.Data, validateData: true);
        await _platform.SendAsync(state.Binding.TeamsCallId, frame.Data, cancellationToken);
        Interlocked.Increment(ref state.SentFrames);
        return new(true, false);
    }

    public Task SuspendAsync(Guid mediaSessionId, string reason, CancellationToken cancellationToken)
    {
        var state = Get(mediaSessionId);
        state.State = TeamsMeetingMediaStates.Suspended;
        state.FailureSummary = reason;
        return Task.CompletedTask;
    }

    public async Task CancelResponseAsync(Guid mediaSessionId, CancellationToken cancellationToken)
    { var s = Get(mediaSessionId); await _platform.CancelAsync(s.Binding.TeamsCallId, cancellationToken); }

    public async Task<TeamsMeetingMediaSession> ReconnectAsync(Guid mediaSessionId, CancellationToken cancellationToken)
    {
        var state = Get(mediaSessionId);
        if (state.ReconnectCount >= _configured.Value.MaximumMediaReconnects)
        {
            state.State = TeamsMeetingMediaStates.Degraded;
            state.FailureCode = "reconnect_limit_exceeded";
            state.FailureSummary = "The bounded Teams media reconnect limit was reached.";
            return state.Snapshot(_configured.Value.MediaRoute);
        }
        await AuthorizeAsync(state.Binding, cancellationToken);
        state.ReconnectCount++; state.State = TeamsMeetingMediaStates.Active;
        state.FailureCode = state.FailureSummary = null;
        return state.Snapshot(_configured.Value.MediaRoute);
    }

    public Task<TeamsMeetingMediaSession?> GetStatusAsync(Guid mediaSessionId, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.TryGetValue(mediaSessionId, out var value) ? value.Snapshot(_configured.Value.MediaRoute) : null);

    public async Task TerminateAsync(Guid mediaSessionId, string reason, CancellationToken cancellationToken)
    {
        if (!_sessions.TryRemove(mediaSessionId, out var state)) return;
        state.State = TeamsMeetingMediaStates.Stopped; state.Incoming.Writer.TryComplete();
        _sessionByCall.TryRemove(state.Binding.TeamsCallId, out _);
        await _platform.ReleaseAsync(state.Binding.TeamsCallId, cancellationToken);
    }

    public async Task<TeamsMediaWriteResult> ReceiveProviderAudioAsync(TeamsAudioFrame frame, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(frame.MediaSessionId, out var state)) return new(false, false, TeamsMeetingMediaProblemCodes.SessionNotFound);
        try { await AuthorizeAsync(state.Binding, cancellationToken); }
        catch (TeamsMeetingMediaException e)
        {
            state.State = TeamsMeetingMediaStates.Degraded;
            state.FailureCode = e.Code;
            state.FailureSummary = e.Message;
            return new(false, false, e.Code);
        }
        EnsureActive(state); ValidateFormat(frame.Format, frame.Data, validateData: true);
        var previousSequence = Interlocked.Read(ref state.LastReceivedSequence);
        if (frame.Sequence <= previousSequence)
        {
            Interlocked.Increment(ref state.DroppedFrames); FramesDropped.Add(1,
                new KeyValuePair<string, object?>("reason", TeamsMeetingMediaProblemCodes.ReorderedFrame));
            return new(false, false, TeamsMeetingMediaProblemCodes.ReorderedFrame);
        }
        Interlocked.Exchange(ref state.LastReceivedSequence, frame.Sequence);
        var previousTicks = Interlocked.Exchange(ref state.LastReceivedTimestampTicks, frame.TimestampUtc.Ticks);
        if (previousTicks > 0)
        {
            var actualMilliseconds = TimeSpan.FromTicks(Math.Max(0, frame.TimestampUtc.Ticks - previousTicks)).TotalMilliseconds;
            FrameJitter.Record(Math.Abs(actualMilliseconds - state.Format.FrameDurationMilliseconds));
        }
        if (!state.Incoming.Writer.TryWrite(frame))
        {
            Interlocked.Increment(ref state.DroppedFrames); FramesDropped.Add(1);
            return new(false, true, TeamsMeetingMediaProblemCodes.Backpressure);
        }
        Interlocked.Increment(ref state.ReceivedFrames);
        return new(true, false);
    }

    private Task<TeamsMediaWriteResult> OnProviderAudioAsync(Guid callId, long sequence, DateTime timestamp,
        byte[] data, bool silence, string? participant)
    {
        if (!_sessionByCall.TryGetValue(callId, out var sessionId)) return Task.FromResult(new TeamsMediaWriteResult(false, false, TeamsMeetingMediaProblemCodes.SessionNotFound));
        return ReceiveProviderAudioAsync(new TeamsAudioFrame(sessionId, sequence, timestamp,
            TeamsAudioFormat.Pcm16KMono20Ms, data, silence, participant), CancellationToken.None);
    }

    private async Task AuthorizeAsync(TeamsMeetingMediaBinding binding, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITeamsMeetingMediaBindingAuthorizer>().AuthorizeAsync(binding, cancellationToken);
    }

    private SessionState Get(Guid id) => _sessions.TryGetValue(id, out var state) ? state :
        throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.SessionNotFound, "The Teams media session was not found.");
    private static void EnsureActive(SessionState state)
    {
        if (state.State != TeamsMeetingMediaStates.Active || state.ExpiresUtc <= DateTime.UtcNow)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.NotReady, "The Teams media session is not active.");
    }
    private static void ValidateFormat(TeamsAudioFormat format, ReadOnlyMemory<byte> data, bool validateData)
    {
        if (!format.IsSupported || validateData && data.Length != format.ExpectedFrameBytes)
            throw new TeamsMeetingMediaException(TeamsMeetingMediaProblemCodes.InvalidFrame,
                "Teams audio must be 20 ms PCM S16LE, 16 kHz, mono frames.");
    }

    private sealed class SessionState(Guid id, TeamsMeetingMediaBinding binding, TeamsAudioFormat format,
        Channel<TeamsAudioFrame> incoming, DateTime createdUtc, DateTime expiresUtc, string state)
    {
        public Guid Id { get; } = id; public TeamsMeetingMediaBinding Binding { get; } = binding;
        public TeamsAudioFormat Format { get; } = format; public Channel<TeamsAudioFrame> Incoming { get; } = incoming;
        public DateTime CreatedUtc { get; } = createdUtc; public DateTime ExpiresUtc { get; } = expiresUtc;
        public string State = state; public long ReceivedFrames; public long SentFrames; public long DroppedFrames;
        public long LastReceivedSequence; public long LastReceivedTimestampTicks;
        public int ReconnectCount; public string? FailureCode; public string? FailureSummary;
        public TeamsMeetingMediaSession Snapshot(string route) => new(Id, Binding, route, State, Format, CreatedUtc,
            ExpiresUtc, ReceivedFrames, SentFrames, DroppedFrames, ReconnectCount, FailureCode, FailureSummary);
    }
}
