using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class TeamsCallControlApiClient(ICompanyApiTransport transport, bool useOfflineMode)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<TeamsMeetingCallViewModel?> GetAsync(Guid companyId, Guid sessionId, CancellationToken ct = default) =>
        SendAsync(companyId, HttpMethod.Get, $"api/sales/meeting-sessions/{sessionId:D}/teams-call", null, true, ct);
    public Task<TeamsOrganizerPresenterStateViewModel?> GetOrganizerStateAsync(Guid companyId, Guid sessionId, CancellationToken ct = default) =>
        SendAsync<TeamsOrganizerPresenterStateViewModel>(companyId, HttpMethod.Get,
            $"api/sales/meeting-sessions/{sessionId:D}/teams-presenter/state", null, false, ct);
    public Task<TeamsMeetingCallViewModel?> JoinAsync(Guid companyId, Guid sessionId, long? expectedVersion = null, CancellationToken ct = default) =>
        SendAsync(companyId, HttpMethod.Post, $"api/sales/meeting-sessions/{sessionId:D}/teams-call/join", JsonContent.Create(new { expectedVersion }, options: Json), false, ct);
    public Task<TeamsMeetingCallViewModel?> LeaveAsync(Guid companyId, Guid sessionId, long expectedVersion, string? reason = null, CancellationToken ct = default) =>
        SendAsync(companyId, HttpMethod.Post, $"api/sales/meeting-sessions/{sessionId:D}/teams-call/leave", JsonContent.Create(new { expectedVersion, reason }, options: Json), false, ct);
    public Task<TeamsMeetingCallViewModel?> ReconcileAsync(Guid companyId, Guid sessionId, long expectedVersion, CancellationToken ct = default) =>
        SendAsync(companyId, HttpMethod.Post, $"api/sales/meeting-sessions/{sessionId:D}/teams-call/reconcile", JsonContent.Create(new { expectedVersion }, options: Json), false, ct);
    public Task<TeamsMeetingCallViewModel?> StartAudioAsync(Guid companyId, Guid sessionId, long expectedVersion, CancellationToken ct = default) =>
        SendAsync(companyId, HttpMethod.Post, $"api/sales/meeting-sessions/{sessionId:D}/teams-call/audio/start", JsonContent.Create(new { expectedVersion }, options: Json), false, ct);
    public Task<TeamsMeetingCallViewModel?> StopAudioAsync(Guid companyId, Guid sessionId, long expectedVersion, string reason = "organizer_stopped_audio", CancellationToken ct = default) =>
        SendAsync(companyId, HttpMethod.Post, $"api/sales/meeting-sessions/{sessionId:D}/teams-call/audio/stop", JsonContent.Create(new { expectedVersion, reason }, options: Json), false, ct);
    public Task<TeamsMeetingCallViewModel?> RevokeConsentAsync(Guid companyId, Guid sessionId, long expectedVersion, CancellationToken ct = default) =>
        SendAsync(companyId, HttpMethod.Post, $"api/sales/meeting-sessions/{sessionId:D}/teams-call/consent/revoke", JsonContent.Create(new { expectedVersion }, options: Json), false, ct);

    public Task<TeamsMeetingPresenterViewModel?> GetPresenterAsync(Guid companyId, Guid sessionId, CancellationToken ct = default) =>
        SendAsync<TeamsMeetingPresenterViewModel>(companyId, HttpMethod.Get, $"api/sales/meeting-sessions/{sessionId:D}/presenter", null, false, ct);
    public Task<TeamsMeetingPresenterViewModel?> SelectPresenterAsync(Guid companyId, Guid sessionId, Guid agentId, long expectedVersion, CancellationToken ct = default) =>
        SendAsync<TeamsMeetingPresenterViewModel>(companyId, HttpMethod.Put, $"api/sales/meeting-sessions/{sessionId:D}/presenter", JsonContent.Create(new { agentId, expectedVersion }), false, ct);

    private Task<TeamsMeetingCallViewModel?> SendAsync(Guid companyId, HttpMethod method, string uri, HttpContent? content, bool missingAllowed, CancellationToken ct) =>
        SendAsync<TeamsMeetingCallViewModel>(companyId, method, uri, content, missingAllowed, ct);

    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content, bool missingAllowed, CancellationToken ct)
    {
        if (useOfflineMode) throw new InvalidOperationException("Teams call control requires the backend API.");
        using var response = await transport.SendAsync(companyId, method, uri, content, ct);
        if (missingAllowed && response.StatusCode == HttpStatusCode.NotFound) return default;
        if (!response.IsSuccessStatusCode)
        {
            var problem = response.Content.Headers.ContentType?.MediaType is "application/json" or "application/problem+json"
                ? await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, ct) : null;
            throw new TeamsCallControlApiException(problem?.Detail ?? problem?.Title ??
                $"Teams call control failed with status code {(int)response.StatusCode}.", response.StatusCode,
                problem?.Code);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }
}

public sealed record TeamsMeetingCallViewModel(Guid Id, Guid MeetingSessionId, string State, string? ProviderState,
    string? SafeProviderReference, string Action, long ActionVersion, string MediaHostInstanceId,
    string? FailureCode, string? FailureSummary, DateTime RequestedUtc, DateTime? AdmittedUtc,
    DateTime? ConnectedUtc, DateTime? EndingUtc, DateTime? EndedUtc, DateTime UpdatedUtc,
    long Version, bool WaitingForOrganizerAdmission, bool MediaStartAuthorized = false,
    Guid? MediaStartAuthorizedByUserId = null, DateTime? MediaStartAuthorizedUtc = null);

public sealed record TeamsOrganizerGuidanceViewModel(string Code, string Title, string Message,
    IReadOnlyList<string> Steps, bool RequiresNativeTeamsAction);
public sealed record TeamsOrganizerPresenterStateViewModel(Guid CompanyId, Guid MeetingSessionId, bool IsOrganizer,
    string ConsentStatus, DateTime? ConsentRecordedUtc, DateTime RetentionUntilUtc,
    string PresentationControlMode, Guid ControlModeUpdatedByUserId, DateTime ControlModeUpdatedUtc,
    string? MeetingJoinUrl, TeamsMeetingCallViewModel? Call, TeamsPresenterReadinessViewModel Readiness,
    TeamsPresenterRolloutViewModel Rollout, TeamsOrganizerGuidanceViewModel Guidance,
    string AttendeeDisclosure, string ConsentDisclosure);

public sealed class TeamsCallControlApiException(string message, HttpStatusCode statusCode, string? reasonCode)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ReasonCode { get; } = reasonCode;
}

public sealed record TeamsPresenterChoiceViewModel(Guid Id, string Name, string Department);
public sealed record TeamsMeetingPresenterViewModel(Guid? AgentId, long Version, IReadOnlyList<TeamsPresenterChoiceViewModel> Choices);
