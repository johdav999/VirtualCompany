using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace VirtualCompany.Web.Services;

public enum SalesPresentationConnectionState
{
    Offline,
    Connecting,
    Connected,
    Reconnecting,
    Degraded,
    Disconnected
}

public enum SalesPresentationSurface
{
    Stage,
    Private
}

public sealed class SalesPresentationRuntimeClient(
    ICompanyApiTransport transport,
    HttpClient httpClient,
    bool useOfflineMode) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HubConnection? _connection;
    private Guid _companyId;
    private Guid _sessionId;
    private SalesPresentationSurface _surface;
    private Func<SalesPresentationStageSnapshotViewModel, Task>? _onStage;
    private Func<SalesPresentationPrivateSnapshotViewModel, Task>? _onPrivate;
    private Func<SalesBrowserPresentationReadinessViewModel, Task>? _onReadiness;
    private Guid _roomId;
    private string? _browserCredential;
    private bool _browserHost;

    public SalesPresentationConnectionState State { get; private set; } =
        useOfflineMode ? SalesPresentationConnectionState.Offline : SalesPresentationConnectionState.Disconnected;

    public event Action<SalesPresentationConnectionState>? StateChanged;
    public event Action<SalesPresentationRenderAcknowledgementViewModel>? StageRenderAcknowledged;
    public event Func<SalesRoomPlaybackStopRequestViewModel, Task>? AgentPlaybackStopRequested;
    public event Func<SalesRoomPlaybackStartRequestViewModel, Task>? AgentPlaybackStarted;

    public async Task<SalesPresentationAuthoritativeSnapshotViewModel?> GetCurrentAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (useOfflineMode)
        {
            SetState(SalesPresentationConnectionState.Offline);
            return null;
        }
        return await SendAsync<SalesPresentationAuthoritativeSnapshotViewModel>(
            companyId, HttpMethod.Get, BasePath(sessionId) + "/current-slide", null, true, cancellationToken);
    }

    public async Task<IReadOnlyList<SalesPresentationSearchResultViewModel>> SearchSlidesAsync(
        Guid companyId, Guid sessionId, string query, CancellationToken cancellationToken = default)
    {
        if (useOfflineMode)
        {
            SetState(SalesPresentationConnectionState.Offline);
            return [];
        }
        var encoded = Uri.EscapeDataString(query ?? string.Empty);
        return await SendAsync<List<SalesPresentationSearchResultViewModel>>(
            companyId, HttpMethod.Get, BasePath(sessionId) + $"/slides/search?query={encoded}", null, false, cancellationToken) ?? [];
    }

    public async Task<SalesPresentationStageAccessGrantViewModel> IssueStageAccessAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        await SendAsync<SalesPresentationStageAccessGrantViewModel>(companyId, HttpMethod.Post,
            $"api/sales/meeting-sessions/{sessionId:D}/stage-access", JsonContent.Create(new { }), false, cancellationToken)
        ?? throw new SalesPresentationRuntimeClientException("The stage access API returned an empty response.");

    public async Task<SalesPresentationStageSnapshotViewModel> GetStageSnapshotAsync(
        Guid sessionId, string stageToken, CancellationToken cancellationToken = default) =>
        await GetCapabilityAsync<SalesPresentationStageSnapshotViewModel>(
            $"api/sales/meeting-stage/{sessionId:D}/snapshot?accessToken={Uri.EscapeDataString(stageToken)}", cancellationToken)
        ?? throw new SalesPresentationRuntimeClientException("The stage snapshot API returned an empty response.");

    public async Task<SalesPresentationSlideImageViewModel> GetStageSlideImageAsync(
        Guid sessionId, string stageToken, SalesPresentationStageSnapshotViewModel snapshot,
        CancellationToken cancellationToken = default)
    {
        var uri = $"api/sales/meeting-stage/{sessionId:D}/decks/{snapshot.DeckId:D}/versions/{snapshot.DeckVersion}/slides/{snapshot.SlideNumber}/image?accessToken={Uri.EscapeDataString(stageToken)}";
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new SalesPresentationRuntimeClientException("The authorized slide image could not be loaded.", response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/svg+xml";
        return new($"data:{contentType};base64,{Convert.ToBase64String(bytes)}",
            snapshot.ImageWidthPixels, snapshot.ImageHeightPixels);
    }

    public async Task<SalesPresentationControlModeViewModel> SetControlModeAsync(
        Guid companyId, Guid sessionId, string mode, long expectedVersion,
        CancellationToken cancellationToken = default) =>
        await SendAsync<SalesPresentationControlModeViewModel>(companyId, HttpMethod.Put,
            BasePath(sessionId) + "/control-mode", JsonContent.Create(new { mode, expectedVersion }, options: Json),
            false, cancellationToken)
        ?? throw new SalesPresentationRuntimeClientException("The control-mode API returned an empty response.");

    public async Task<SalesPresentationCommandResultViewModel> ExecuteAsync(
        Guid companyId, Guid sessionId, string toolName, SalesPresentationCommandViewModel command,
        CancellationToken cancellationToken = default)
    {
        if (useOfflineMode)
        {
            SetState(SalesPresentationConnectionState.Offline);
            throw new SalesPresentationRuntimeClientException("Live presentation commands are unavailable offline.");
        }

        if (_connection is { State: HubConnectionState.Connected } &&
            _companyId == companyId && _sessionId == sessionId && _surface == SalesPresentationSurface.Private)
        {
            try
            {
                return await _connection.InvokeAsync<SalesPresentationCommandResultViewModel>(
                    "ExecutePresentationCommand", sessionId, toolName, command, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetState(SalesPresentationConnectionState.Degraded);
            }
        }

        return await SendAsync<SalesPresentationCommandResultViewModel>(
            companyId, HttpMethod.Post,
            BasePath(sessionId) + $"/commands/{Uri.EscapeDataString(toolName)}",
            JsonContent.Create(command, options: Json), false, cancellationToken)
            ?? throw new SalesPresentationRuntimeClientException("The presentation API returned an empty response.");
    }

    public async Task ConnectAsync(
        Guid companyId,
        Guid sessionId,
        SalesPresentationSurface surface,
        Func<SalesPresentationStageSnapshotViewModel, Task>? onStage = null,
        Func<SalesPresentationPrivateSnapshotViewModel, Task>? onPrivate = null,
        CancellationToken cancellationToken = default)
    {
        if (useOfflineMode)
        {
            SetState(SalesPresentationConnectionState.Offline);
            return;
        }
        if (surface == SalesPresentationSurface.Stage && onStage is null)
            throw new ArgumentNullException(nameof(onStage));
        if (surface == SalesPresentationSurface.Private && onPrivate is null)
            throw new ArgumentNullException(nameof(onPrivate));

        await DisconnectAsync();
        _companyId = companyId;
        _sessionId = sessionId;
        _surface = surface;
        _onStage = onStage;
        _onPrivate = onPrivate;
        SetState(SalesPresentationConnectionState.Connecting);

        _connection = BuildConnection(HubUri(companyId));
        RegisterConnectionHandlers();

        try
        {
            await _connection.StartAsync(cancellationToken);
            await JoinAndDispatchAsync(cancellationToken);
            SetState(SalesPresentationConnectionState.Connected);
        }
        catch
        {
            SetState(SalesPresentationConnectionState.Degraded);
            throw;
        }
    }

    public async Task ConnectStageAsync(
        Guid sessionId,
        string stageToken,
        Func<SalesPresentationStageSnapshotViewModel, Task> onStage,
        CancellationToken cancellationToken = default)
    {
        if (useOfflineMode)
            throw new SalesPresentationRuntimeClientException("The meeting stage is unavailable offline.");

        await DisconnectAsync();
        _companyId = Guid.Empty;
        _sessionId = sessionId;
        _surface = SalesPresentationSurface.Stage;
        _onStage = onStage;
        _onPrivate = null;
        SetState(SalesPresentationConnectionState.Connecting);

        _connection = BuildConnection(HubUri(sessionId, stageToken));
        RegisterConnectionHandlers();
        try
        {
            await _connection.StartAsync(cancellationToken);
            await JoinAndDispatchAsync(cancellationToken);
            SetState(SalesPresentationConnectionState.Connected);
        }
        catch
        {
            SetState(SalesPresentationConnectionState.Degraded);
            throw;
        }
    }

    public async Task ConnectBrowserAsync(
        Guid companyId,
        Guid roomId,
        Guid sessionId,
        string? guestCredential,
        Func<SalesPresentationStageSnapshotViewModel, Task> onStage,
        Func<SalesPresentationPrivateSnapshotViewModel, Task>? onPrivate = null,
        Func<SalesBrowserPresentationReadinessViewModel, Task>? onReadiness = null,
        CancellationToken cancellationToken = default)
    {
        if (useOfflineMode)
            throw new SalesPresentationRuntimeClientException("The browser presentation is unavailable offline.");
        await DisconnectAsync();
        _companyId = companyId;
        _roomId = roomId;
        _sessionId = sessionId;
        _surface = SalesPresentationSurface.Stage;
        _browserCredential = guestCredential;
        _browserHost = companyId != Guid.Empty;
        _onStage = onStage;
        _onPrivate = onPrivate;
        _onReadiness = onReadiness;
        SetState(SalesPresentationConnectionState.Connecting);
        _connection = BuildConnection(HubUriForBrowser(companyId, roomId), guestCredential);
        RegisterConnectionHandlers();
        try
        {
            await _connection.StartAsync(cancellationToken);
            await JoinAndDispatchAsync(cancellationToken);
            SetState(SalesPresentationConnectionState.Connected);
        }
        catch
        {
            SetState(SalesPresentationConnectionState.Degraded);
            throw;
        }
    }

    public async Task AcknowledgeStageRenderAsync(
        SalesPresentationStageSnapshotViewModel snapshot,
        DateTime renderedUtc,
        CancellationToken cancellationToken = default)
    {
        if (_connection is not { State: HubConnectionState.Connected } || _surface != SalesPresentationSurface.Stage)
            throw new SalesPresentationRuntimeClientException("An active stage connection is required before acknowledging a render.");

        await _connection.InvokeAsync("AcknowledgeStageRender", snapshot.SessionId, snapshot.DeckId,
            snapshot.DeckVersion, snapshot.SlideNumber, snapshot.Sequence, snapshot.Version, renderedUtc, cancellationToken);
    }

    private HubConnection BuildConnection(Uri uri, string? browserCredential = null) => new HubConnectionBuilder()
        .WithUrl(uri, options =>
        {
            foreach (var header in httpClient.DefaultRequestHeaders)
                options.Headers[header.Key] = string.Join(",", header.Value);
            if (!string.IsNullOrWhiteSpace(browserCredential))
                options.Headers["X-Sales-Room-Session"] = browserCredential;
        })
        .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)])
        .Build();

    private void RegisterConnectionHandlers()
    {
        if (_connection is null) return;
        _connection.On<SalesPresentationStageSnapshotViewModel>("StageStateChanged", snapshot =>
            _onStage?.Invoke(snapshot) ?? Task.CompletedTask);
        _connection.On<SalesPresentationPrivateSnapshotViewModel>("PrivateStateChanged", snapshot =>
            _onPrivate?.Invoke(snapshot) ?? Task.CompletedTask);
        _connection.On<SalesPresentationRenderAcknowledgementViewModel>("StageRenderAcknowledged", acknowledgement =>
        {
            StageRenderAcknowledged?.Invoke(acknowledgement);
        });
        _connection.On<SalesBrowserPresentationReadinessViewModel>("BrowserAudienceChanged", readiness =>
            _onReadiness?.Invoke(readiness) ?? Task.CompletedTask);
        _connection.On<SalesRoomPlaybackStopRequestViewModel>("AgentPlaybackStopRequested", async request =>
        {
            if (_roomId == Guid.Empty || request.RoomId != _roomId || AgentPlaybackStopRequested is null) return;
            await AgentPlaybackStopRequested(request);
            if (_connection is { State: HubConnectionState.Connected })
                await _connection.InvokeAsync("AcknowledgeAgentPlaybackStopped", request.StopId,
                    request.ResponseGeneration, CancellationToken.None);
        });
        _connection.On<SalesRoomPlaybackStartRequestViewModel>("AgentPlaybackStarted", request =>
            _roomId != Guid.Empty && request.RoomId == _roomId && AgentPlaybackStarted is not null
                ? AgentPlaybackStarted(request) : Task.CompletedTask);
        _connection.Reconnecting += _ => { SetState(SalesPresentationConnectionState.Reconnecting); return Task.CompletedTask; };
        _connection.Closed += _ => { SetState(SalesPresentationConnectionState.Degraded); return Task.CompletedTask; };
        _connection.Reconnected += async _ =>
        {
            try
            {
                await JoinAndDispatchAsync(CancellationToken.None);
                SetState(SalesPresentationConnectionState.Connected);
            }
            catch
            {
                SetState(SalesPresentationConnectionState.Degraded);
            }
        };
    }

    public async Task DisconnectAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        _roomId = Guid.Empty;
        _browserCredential = null;
        _browserHost = false;
        _onReadiness = null;
        SetState(useOfflineMode ? SalesPresentationConnectionState.Offline : SalesPresentationConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();

    private async Task JoinAndDispatchAsync(CancellationToken cancellationToken)
    {
        if (_connection is null) return;
        if (_roomId != Guid.Empty)
        {
            if (_browserHost && _onPrivate is not null)
            {
                var privateSnapshot = await _connection.InvokeAsync<SalesPresentationPrivateSnapshotViewModel>(
                    "JoinPrivate", _sessionId, cancellationToken);
                await _onPrivate(privateSnapshot);
            }
            var browserSnapshot = await _connection.InvokeAsync<SalesBrowserPresentationPublicViewModel>(
                "JoinBrowserStage", _roomId, cancellationToken);
            if (_onStage is not null) await _onStage(browserSnapshot.Stage);
            return;
        }
        if (_surface == SalesPresentationSurface.Stage)
        {
            var snapshot = await _connection.InvokeAsync<SalesPresentationStageSnapshotViewModel>("JoinStage", _sessionId, cancellationToken);
            if (_onStage is not null) await _onStage(snapshot);
        }
        else
        {
            var snapshot = await _connection.InvokeAsync<SalesPresentationPrivateSnapshotViewModel>("JoinPrivate", _sessionId, cancellationToken);
            if (_onPrivate is not null) await _onPrivate(snapshot);
        }
    }

    private async Task<T?> SendAsync<T>(
        Guid companyId, HttpMethod method, string uri, HttpContent? content,
        bool allowNotFound, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.Content.Headers.ContentLength is > 0
                    ? await response.Content.ReadAsStringAsync(cancellationToken)
                    : null;
                throw new SalesPresentationRuntimeClientException(
                    string.IsNullOrWhiteSpace(detail) ? "The presentation request failed." : detail,
                    response.StatusCode);
            }
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            SetState(SalesPresentationConnectionState.Degraded);
            throw new SalesPresentationRuntimeClientException(
                "The live presentation connection is unavailable; commands can be retried when the backend recovers.", null, exception);
        }
    }

    private async Task<T?> GetCapabilityAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new SalesPresentationRuntimeClientException("The protected meeting stage request failed.", response.StatusCode);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new SalesPresentationRuntimeClientException("The meeting stage is unavailable.", null, exception);
        }
    }

    private Uri HubUri(Guid companyId) => new(
        transport.BaseAddress ?? httpClient.BaseAddress ?? new Uri("http://localhost:5301/"),
        $"hubs/sales-meeting?companyId={companyId:D}");

    private Uri HubUri(Guid sessionId, string stageToken) => new(
        transport.BaseAddress ?? httpClient.BaseAddress ?? new Uri("http://localhost:5301/"),
        $"hubs/sales-meeting?sessionId={sessionId:D}&stageToken={Uri.EscapeDataString(stageToken)}");

    private Uri HubUriForBrowser(Guid companyId, Guid roomId) => new(
        transport.BaseAddress ?? httpClient.BaseAddress ?? new Uri("http://localhost:5301/"),
        companyId == Guid.Empty
            ? $"hubs/sales-meeting?roomId={roomId:D}"
            : $"hubs/sales-meeting?companyId={companyId:D}&roomId={roomId:D}");

    private static string BasePath(Guid sessionId) =>
        $"api/sales/meeting-sessions/{sessionId:D}/presentation";

    private void SetState(SalesPresentationConnectionState value)
    {
        if (State == value) return;
        State = value;
        StateChanged?.Invoke(value);
    }
}

public sealed class SalesPresentationRuntimeClientException(
    string message, HttpStatusCode? statusCode = null, Exception? innerException = null) : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record SalesPresentationCommandViewModel(
    Guid CommandId, long Sequence, long ExpectedVersion,
    int? SlideNumber = null, int? TalkingPointIndex = null, string? ResumeMarker = null,
    string ActorType = "human", Guid? DeckId = null, Guid? ActorId = null,
    int? DeckVersion = null, long? ActorGeneration = null);

public sealed record SalesPresentationStageSnapshotViewModel(
    Guid SessionId, string SessionStatus, long Sequence, long Version,
    Guid DeckId, int DeckVersion, int SlideNumber, int SlideCount,
    string? SlideTitle, string SlideText, string? ImageStorageUrl,
    int ImageWidthPixels, int ImageHeightPixels);

public sealed record SalesPresentationPrivateSnapshotViewModel(
    SalesPresentationStageSnapshotViewModel Stage, string ControlMode, int TalkingPointIndex, string? ResumeMarker,
    string? SpeakerNotes, string Objective, int ExpectedDurationSeconds, string TransitionText,
    IReadOnlyList<SalesPresentationArtifactViewModel> PlanArtifacts,
    Guid? ControlModeUpdatedByUserId = null, DateTime? ControlModeUpdatedUtc = null);

public sealed record SalesPresentationAuthoritativeSnapshotViewModel(
    SalesPresentationStageSnapshotViewModel Stage, SalesPresentationPrivateSnapshotViewModel Private);

public sealed record SalesPresentationSearchResultViewModel(int SlideNumber, string? Title, string Snippet);

public sealed record SalesPresentationCommandResultViewModel(
    string Disposition, string? ReasonCode, SalesPresentationAuthoritativeSnapshotViewModel Snapshot);

public sealed record SalesPresentationStageAccessGrantViewModel(
    string AccessToken, Guid SessionId, Guid DeckId, int DeckVersion, DateTime ExpiresUtc, string StagePath);

public sealed record SalesPresentationSlideImageViewModel(string DataUrl, int WidthPixels, int HeightPixels);

public sealed record SalesPresentationControlModeViewModel(
    Guid SessionId, string Mode, Guid UpdatedByUserId, DateTime UpdatedUtc, long Version);

public sealed record SalesPresentationRenderAcknowledgementViewModel(
    Guid CompanyId, Guid SessionId, Guid DeckId, int DeckVersion, int SlideNumber,
    long PresentationSequence, long PresentationVersion, string ConnectionId, DateTime RenderedUtc);

public sealed record SalesBrowserPresentationAudienceMemberViewModel(
    Guid ParticipantId, string DisplayName, bool Connected, string State, DateTime? RenderedUtc);
public sealed record SalesBrowserPresentationReadinessViewModel(
    Guid RoomId, Guid DeckId, int DeckVersion, int SlideNumber, long PresentationSequence,
    long PresentationVersion, DateTime DeadlineUtc, bool OverrideApplied,
    IReadOnlyList<SalesBrowserPresentationAudienceMemberViewModel> Audience)
{
    public int RequiredCount => Audience.Count;
    public int RenderedCount => Audience.Count(x => x.State is "rendered" or "overridden");
    public bool Ready => RequiredCount == RenderedCount;
}
public sealed record SalesBrowserPresentationPublicViewModel(
    Guid RoomId, Guid ParticipantId, long ActorGeneration, SalesPresentationStageSnapshotViewModel Stage);
public sealed record SalesBrowserPresentationHostViewModel(
    Guid RoomId, Guid ParticipantId, long ActorGeneration,
    SalesPresentationAuthoritativeSnapshotViewModel Presentation,
    SalesBrowserPresentationReadinessViewModel Readiness);
public sealed record SalesRoomPlaybackStopRequestViewModel(Guid RoomId, Guid StopId, long ResponseGeneration, DateTime DeadlineUtc);
public sealed record SalesRoomPlaybackStartRequestViewModel(Guid RoomId, long ResponseGeneration);
