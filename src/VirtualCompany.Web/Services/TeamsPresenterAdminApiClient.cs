using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class TeamsPresenterAdminApiClient(HttpClient httpClient, bool useOfflineMode)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<TeamsPresenterReadinessViewModel> GetReadinessAsync(Guid companyId, CancellationToken ct = default) =>
        SendAsync<TeamsPresenterReadinessViewModel>(HttpMethod.Get,
            $"api/platform/teams-presenter/readiness?companyId={companyId:D}", null, ct);

    public Task<TeamsTenantRegistrationViewModel> AssociateAsync(Guid companyId, Guid tenantId,
        string mediaRoute, CancellationToken ct = default) =>
        SendAsync<TeamsTenantRegistrationViewModel>(HttpMethod.Put,
            $"api/platform/teams-presenter/companies/{companyId:D}",
            JsonContent.Create(new { entraTenantId = tenantId, approvedMediaRoute = mediaRoute }), ct);

    public Task<TeamsAdminConsentStartViewModel> StartConsentAsync(Guid companyId, CancellationToken ct = default) =>
        SendAsync<TeamsAdminConsentStartViewModel>(HttpMethod.Post,
            $"api/platform/teams-presenter/companies/{companyId:D}/admin-consent", JsonContent.Create(new { }), ct);

    public Task<TeamsTenantRegistrationViewModel> AttestPolicyAsync(Guid companyId, bool approved, CancellationToken ct = default) =>
        SendAsync<TeamsTenantRegistrationViewModel>(HttpMethod.Post,
            $"api/platform/teams-presenter/companies/{companyId:D}/policy-attestation",
            JsonContent.Create(new { approved }), ct);

    public Task<TeamsTenantRegistrationViewModel> DisableAsync(Guid companyId, bool revokeConsent, CancellationToken ct = default) =>
        SendAsync<TeamsTenantRegistrationViewModel>(HttpMethod.Post,
            $"api/platform/teams-presenter/companies/{companyId:D}/disable",
            JsonContent.Create(new { consentRevoked = revokeConsent }), ct);

    public Task<FirstTeamsUatViewModel> GetFirstUatAsync(Guid companyId, CancellationToken ct = default) =>
        SendAsync<FirstTeamsUatViewModel>(HttpMethod.Get, $"api/platform/teams-presenter/companies/{companyId:D}/first-uat", null, ct);
    public Task<FirstTeamsUatViewModel> AuthorizeFirstUatAsync(Guid companyId, Guid organizerUserId, Guid meetingSessionId, int durationMinutes, string reason, long expectedVersion, CancellationToken ct = default) =>
        SendAsync<FirstTeamsUatViewModel>(HttpMethod.Post, $"api/platform/teams-presenter/companies/{companyId:D}/first-uat", JsonContent.Create(new { organizerUserId, meetingSessionId, durationMinutes, reason, expectedVersion }), ct);
    public Task<FirstTeamsUatViewModel> RevokeFirstUatAsync(Guid companyId, long expectedVersion, CancellationToken ct = default) =>
        SendAsync<FirstTeamsUatViewModel>(HttpMethod.Post, $"api/platform/teams-presenter/companies/{companyId:D}/first-uat/revoke", JsonContent.Create(new { expectedVersion }), ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        if (useOfflineMode) throw new TeamsPresenterAdminApiException("Teams presenter administration requires the backend API.");
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var problem = response.Content.Headers.ContentType?.MediaType is "application/json" or "application/problem+json"
                ? await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, ct) : null;
            throw new TeamsPresenterAdminApiException(problem?.Detail ?? problem?.Title ??
                $"The Teams presenter request failed with status code {(int)response.StatusCode}.", response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new TeamsPresenterAdminApiException("The Teams presenter API returned an empty response.");
    }
}

public sealed class TeamsPresenterAdminApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record TeamsAdminConsentStartViewModel(Uri AuthorizationUrl, DateTime ExpiresUtc);
public sealed record TeamsPresenterCapabilityViewModel(bool CallControl, bool Audio, bool SharedStage, bool VisualMedia);
public sealed record TeamsPresenterReadinessCheckViewModel(string Name, bool Ready, string State, string ReasonCode, string Message, int? RetryAfterSeconds);
public sealed record TeamsTenantRegistrationViewModel(Guid Id, Guid CompanyId, string Status, string ConsentStatus,
    string PermissionStatus, string PolicyStatus, string ApprovedMediaRoute, IReadOnlyList<string> RequiredPermissions,
    IReadOnlyList<string> GrantedPermissions, string? FailureCode, DateTime? ConsentVerifiedUtc,
    DateTime? PermissionsVerifiedUtc, DateTime? PolicyApprovedUtc, DateTime UpdatedUtc, long Version,
    Guid? EntraTenantId, Guid? TeamsAppId, Guid? BotApplicationId);
public sealed record TeamsPresenterRolloutViewModel(bool Allowed, string ReasonCode, string Message,
    bool ProductionEnabled, bool PilotEnabled, bool EmergencyDisabled, bool CompanyAllowed, bool UserAllowed,
    bool TenantAllowed, bool PackageCompatible, bool AppInstallationAttested, bool AutomatedEvidenceApproved,
    bool LiveUatApproved, string PackageVersion, string MinimumPackageVersion, string? InstalledPackageVersion,
    int ActiveCalls, int MaximumActiveCalls, decimal MonthlyCostUsed, decimal MonthlyCostLimit, string Currency, string FallbackMode,
    string? LiveUatOwner, DateTime? LiveUatCompletedUtc, string? LiveUatEvidenceReference,
    DateTime? CertificateExpiresUtc, string? InstallUrl);
public sealed record TeamsPresenterReadinessViewModel(DateTime EvaluatedUtc, bool Enabled, bool PackageReady,
    bool LiveCallingReady, string MediaRoute, string PackageVersion, string TenantMode,
    TeamsPresenterCapabilityViewModel ConfiguredCapabilities, TeamsPresenterCapabilityViewModel EffectiveCapabilities,
    IReadOnlyList<TeamsPresenterReadinessCheckViewModel> Checks, TeamsTenantRegistrationViewModel? TenantRegistration,
    TeamsPresenterRolloutViewModel? Rollout);

public sealed record FirstTeamsUatViewModel(Guid? OrganizerUserId, Guid? MeetingSessionId, DateTime? ExpiresUtc, string? Reason, long Version);
