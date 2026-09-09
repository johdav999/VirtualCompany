namespace VirtualCompany.Application.Sales;

public static class TeamsPresenterApplicationPermissions
{
    public const string JoinGroupCall = "Calls.JoinGroupCall.All";
    public const string AccessMedia = "Calls.AccessMedia.All";

    public static IReadOnlyList<string> RequiredFor(
        string mediaRoute,
        bool callControlEnabled,
        bool audioEnabled,
        bool visualMediaEnabled)
    {
        var permissions = new List<string>();
        if (callControlEnabled)
        {
            permissions.Add(JoinGroupCall);
        }

        if (string.Equals(mediaRoute, "teams_application_hosted", StringComparison.Ordinal) &&
            (audioEnabled || visualMediaEnabled))
        {
            permissions.Add(AccessMedia);
        }

        return permissions;
    }
}

public static class TeamsTenantRegistrationStates
{
    public const string PendingConsent = "pending_consent";
    public const string PendingPolicy = "pending_policy";
    public const string Ready = "ready";
    public const string Blocked = "blocked";
    public const string Disabled = "disabled";
    public const string Revoked = "revoked";
}

public static class TeamsTenantVerificationStates
{
    public const string Pending = "pending";
    public const string Verified = "verified";
    public const string Missing = "missing";
    public const string Excess = "excess";
    public const string Attested = "attested";
    public const string Revoked = "revoked";
}

public static class TeamsIdentityFailureCodes
{
    public const string CredentialUnavailable = "teams_identity.credential_unavailable";
    public const string TokenAcquisitionFailed = "teams_identity.token_acquisition_failed";
    public const string TokenInvalid = "teams_identity.token_invalid";
    public const string TokenTenantMismatch = "teams_identity.token_tenant_mismatch";
    public const string PermissionMissing = "teams_identity.permission_missing";
    public const string PermissionExcess = "teams_identity.permission_excess";
    public const string TenantNotAssociated = "teams_identity.tenant_not_associated";
    public const string TenantDisabled = "teams_identity.tenant_disabled";
    public const string CallbackUnauthorized = "teams_identity.callback_unauthorized";
    public const string ConsentStateInvalid = "teams_identity.consent_state_invalid";
    public const string ConsentStateExpired = "teams_identity.consent_state_expired";
    public const string ConsentStateReplayed = "teams_identity.consent_state_replayed";
    public const string ConsentTenantMismatch = "teams_identity.consent_tenant_mismatch";
    public const string ConsentDenied = "teams_identity.consent_denied";
    public const string TenantAlreadyAssociated = "teams_identity.tenant_already_associated";
}

public sealed record TeamsAppOnlyTokenRequest(
    Guid EntraTenantId,
    IReadOnlyCollection<string> RequiredPermissions,
    bool ForceRefresh = false);

public sealed record TeamsAppOnlyAccessToken(
    string AccessToken,
    DateTime ExpiresUtc,
    Guid EntraTenantId,
    IReadOnlyList<string> GrantedPermissions);

public interface ITeamsAppOnlyTokenProvider
{
    Task<TeamsAppOnlyAccessToken> AcquireAsync(
        TeamsAppOnlyTokenRequest request,
        CancellationToken cancellationToken);
}

public sealed record TeamsTenantRegistrationDto(
    Guid Id,
    Guid CompanyId,
    string Status,
    string ConsentStatus,
    string PermissionStatus,
    string PolicyStatus,
    string ApprovedMediaRoute,
    IReadOnlyList<string> RequiredPermissions,
    IReadOnlyList<string> GrantedPermissions,
    string? FailureCode,
    DateTime? ConsentVerifiedUtc,
    DateTime? PermissionsVerifiedUtc,
    DateTime? PolicyApprovedUtc,
    DateTime UpdatedUtc,
    long Version,
    Guid? EntraTenantId = null,
    Guid? TeamsAppId = null,
    Guid? BotApplicationId = null);

public sealed record AssociateTeamsTenantCommand(
    Guid CompanyId,
    Guid EntraTenantId,
    string ApprovedMediaRoute,
    Guid ActorUserId,
    string? CorrelationId);

public sealed record StartTeamsAdminConsentCommand(
    Guid CompanyId,
    Guid ActorUserId,
    string? CorrelationId);

public sealed record TeamsAdminConsentStartResult(
    Uri AuthorizationUrl,
    DateTime ExpiresUtc);

public sealed record CompleteTeamsAdminConsentCommand(
    string State,
    string? Tenant,
    string? AdminConsent,
    string? Error,
    Guid ActorUserId,
    string? CorrelationId);

public sealed record AttestTeamsTenantPolicyCommand(
    Guid CompanyId,
    bool Approved,
    Guid ActorUserId,
    string? CorrelationId);

public sealed record DisableTeamsTenantCommand(
    Guid CompanyId,
    bool ConsentRevoked,
    Guid ActorUserId,
    string? CorrelationId);

public interface ITeamsTenantRegistrationService
{
    Task<TeamsTenantRegistrationDto?> GetAsync(Guid companyId, CancellationToken cancellationToken);
    Task<TeamsTenantRegistrationDto> AssociateAsync(AssociateTeamsTenantCommand command, CancellationToken cancellationToken);
    Task<TeamsAdminConsentStartResult> StartAdminConsentAsync(StartTeamsAdminConsentCommand command, CancellationToken cancellationToken);
    Task<TeamsTenantRegistrationDto> CompleteAdminConsentAsync(CompleteTeamsAdminConsentCommand command, CancellationToken cancellationToken);
    Task<TeamsTenantRegistrationDto> AttestPolicyAsync(AttestTeamsTenantPolicyCommand command, CancellationToken cancellationToken);
    Task<TeamsTenantRegistrationDto> DisableAsync(DisableTeamsTenantCommand command, CancellationToken cancellationToken);
}

public sealed record TeamsCallbackIdentity(
    Guid CompanyId,
    Guid RegistrationId,
    Guid EntraTenantId);

public interface ITeamsCallbackAuthenticator
{
    Task<TeamsCallbackIdentity> AuthenticateAsync(string? authorizationHeader, CancellationToken cancellationToken);
}

public sealed class TeamsIdentityException : Exception
{
    public TeamsIdentityException(string code, string message, bool retryable = false, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }
    public bool Retryable { get; }
}
