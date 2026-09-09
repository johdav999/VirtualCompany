using System.Net.Http.Headers;
using Microsoft.JSInterop;

namespace VirtualCompany.Web.Services;

public sealed class TeamsMeetingUiOptions
{
    public const string SectionName = "TeamsMeetingUi";
    public bool BrowserDiagnosticsEnabled { get; set; }
}

public sealed record TeamsMeetingContext(
    bool InTeams,
    string? FrameContext,
    string? TenantId,
    string? UserId,
    string? MeetingId,
    bool HasSsoToken,
    bool CanShareToStage,
    string? ShareReasonCode);

public sealed record TeamsStageShareResult(bool Succeeded, string? ReasonCode, string? Message);

public sealed class TeamsMeetingContextService(IJSRuntime jsRuntime, HttpClient httpClient) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private string? _accessToken;

    public TeamsMeetingContext? Current { get; private set; }

    public async Task<TeamsMeetingContext> InitializeAsync(CancellationToken cancellationToken = default)
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", cancellationToken, "./js/teams-meeting.js");
        var result = await _module.InvokeAsync<TeamsInitializationResult>("initialize", cancellationToken);
        _accessToken = result.AccessToken;
        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        Current = new TeamsMeetingContext(
            result.InTeams,
            result.FrameContext,
            result.TenantId,
            result.UserId,
            result.MeetingId,
            !string.IsNullOrWhiteSpace(_accessToken),
            result.CanShareToStage,
            result.ShareReasonCode);
        return Current;
    }

    public async Task<TeamsStageShareResult> ShareToMeetingAsync(
        string stageUrl,
        CancellationToken cancellationToken = default)
    {
        if (_module is null || Current?.InTeams != true)
        {
            return new(false, "not_in_teams", "Open this control inside the installed Teams meeting app to share it.");
        }

        if (!Current.CanShareToStage)
        {
            return new(false, Current.ShareReasonCode ?? "stage_share_unavailable",
                "Teams has not granted this participant permission to share the app to the meeting stage.");
        }

        return await _module.InvokeAsync<TeamsStageShareResult>("shareToMeeting", cancellationToken, stageUrl);
    }

    public async Task<TeamsStageShareResult> ConfigureTabAsync(
        string sidePanelUrl,
        string websiteUrl,
        CancellationToken cancellationToken = default)
    {
        if (_module is null || Current?.InTeams != true)
        {
            return new(false, "not_in_teams", "Open this page from Teams configuration.");
        }

        return await _module.InvokeAsync<TeamsStageShareResult>(
            "configureTab", cancellationToken, sidePanelUrl, websiteUrl);
    }

    public async ValueTask DisposeAsync()
    {
        _accessToken = null;
        httpClient.DefaultRequestHeaders.Authorization = null;
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }

    private sealed record TeamsInitializationResult(
        bool InTeams,
        string? FrameContext,
        string? TenantId,
        string? UserId,
        string? MeetingId,
        bool CanShareToStage,
        string? ShareReasonCode,
        string? AccessToken);
}
