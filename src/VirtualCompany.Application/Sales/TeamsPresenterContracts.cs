namespace VirtualCompany.Application.Sales;

public static class TeamsPresenterReadinessCheckNames
{
    public const string Configuration = "configuration";
    public const string Package = "package";
    public const string Identity = "identity";
    public const string Urls = "urls";
    public const string Domains = "domains";
    public const string MediaRoute = "media_route";
    public const string Credential = "credential";
    public const string Token = "token";
    public const string TenantApproval = "tenant_approval";
    public const string Permissions = "permissions";
    public const string TenantPolicy = "tenant_policy";
    public const string CallbackAuthentication = "callback_authentication";
    public const string CallControl = "call_control";
    public const string SharedStage = "shared_stage";
    public const string MediaHost = "media_host";
    public const string Rollout = "rollout";
}

public static class TeamsPresenterReadinessReasonCodes
{
    public const string Disabled = "teams_presenter.disabled";
    public const string ConfigurationInvalid = "teams_presenter.configuration_invalid";
    public const string PackageInvalid = "teams_presenter.package_invalid";
    public const string IdentityInvalid = "teams_presenter.identity_invalid";
    public const string UrlInvalid = "teams_presenter.url_invalid";
    public const string DomainMismatch = "teams_presenter.domain_mismatch";
    public const string MediaRouteNotApproved = "teams_presenter.media_route_not_approved";
    public const string CredentialMissing = "teams_presenter.credential_missing";
    public const string TokenNotVerified = "teams_presenter.token_not_verified";
    public const string TenantApprovalNotVerified = "teams_presenter.tenant_approval_not_verified";
    public const string PermissionsNotVerified = "teams_presenter.permissions_not_verified";
    public const string TenantPolicyNotVerified = "teams_presenter.tenant_policy_not_verified";
    public const string CallbackAuthenticationNotReady = "teams_presenter.callback_authentication_not_ready";
    public const string CallControlNotImplemented = "teams_presenter.call_control_not_implemented";
    public const string SharedStageNotImplemented = "teams_presenter.shared_stage_not_implemented";
    public const string MediaHostNotVerified = "teams_presenter.media_host_not_verified";
    public const string MediaHostDraining = "teams_presenter.media_host_draining";
    public const string ProductionDisabled = "teams_presenter.production_disabled";
    public const string PilotDisabled = "teams_presenter.pilot_disabled";
    public const string EmergencyDisabled = "teams_presenter.emergency_disabled";
    public const string DemoTenantBlocked = "teams_presenter.demo_tenant_blocked";
    public const string TenantMismatch = "teams_presenter.tenant_mismatch";
    public const string CompanyNotAllowed = "teams_presenter.company_not_allowed";
    public const string UserNotAllowed = "teams_presenter.user_not_allowed";
    public const string PackageIncompatible = "teams_presenter.package_incompatible";
    public const string LiveUatRequired = "teams_presenter.live_uat_required";
    public const string AutomatedEvidenceRequired = "teams_presenter.automated_evidence_required";
    public const string GlobalCapacityReached = "teams_presenter.global_capacity_reached";
    public const string CostLimitReached = "teams_presenter.cost_limit_reached";
    public const string Ready = "teams_presenter.ready";
}

public sealed record TeamsPresenterCapabilityStateDto(
    bool CallControl,
    bool Audio,
    bool SharedStage,
    bool VisualMedia);

public sealed record TeamsPresenterReadinessCheckDto(
    string Name,
    bool Ready,
    string State,
    string ReasonCode,
    string Message,
    int? RetryAfterSeconds = null);

public sealed record TeamsPresenterReadinessDto(
    DateTime EvaluatedUtc,
    bool Enabled,
    bool PackageReady,
    bool LiveCallingReady,
    string MediaRoute,
    string PackageVersion,
    string TenantMode,
    TeamsPresenterCapabilityStateDto ConfiguredCapabilities,
    TeamsPresenterCapabilityStateDto EffectiveCapabilities,
    IReadOnlyList<TeamsPresenterReadinessCheckDto> Checks,
    TeamsTenantRegistrationDto? TenantRegistration = null,
    TeamsPresenterRolloutDto? Rollout = null);

public sealed record TeamsPresenterRolloutDto(
    bool Allowed, string ReasonCode, string Message,
    bool ProductionEnabled, bool PilotEnabled, bool EmergencyDisabled,
    bool CompanyAllowed, bool UserAllowed, bool TenantAllowed, bool PackageCompatible,
    bool AppInstallationAttested, bool AutomatedEvidenceApproved, bool LiveUatApproved,
    string PackageVersion, string MinimumPackageVersion, string? InstalledPackageVersion,
    int ActiveCalls, int MaximumActiveCalls, decimal MonthlyCostUsed, decimal MonthlyCostLimit, string Currency,
    string FallbackMode, string? LiveUatOwner, DateTime? LiveUatCompletedUtc,
    string? LiveUatEvidenceReference, DateTime? CertificateExpiresUtc, string? InstallUrl);

public interface ITeamsPresenterRolloutPolicy
{
    Task<TeamsPresenterRolloutDto> EvaluateAsync(Guid companyId, Guid? actorUserId,
        Guid? requestEntraTenantId, CancellationToken cancellationToken, Guid? meetingSessionId = null);
}

public interface ITeamsPresenterReadinessService
{
    Task<TeamsPresenterReadinessDto> GetReadinessAsync(Guid? companyId, CancellationToken cancellationToken);
}

public sealed record TeamsPresenterPackageValidationIssue(string Code, string Message);

public sealed record TeamsPresenterPackageBuildResult(
    string ManifestVersion,
    string PackageVersion,
    string Sha256,
    int EntryCount,
    long Length);

public interface ITeamsPresenterPackageBuilder
{
    IReadOnlyList<TeamsPresenterPackageValidationIssue> Validate();
    Task<TeamsPresenterPackageBuildResult> BuildAsync(Stream output, CancellationToken cancellationToken);
}
