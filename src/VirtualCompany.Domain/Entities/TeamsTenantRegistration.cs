namespace VirtualCompany.Domain.Entities;

public sealed class TeamsTenantRegistration : ICompanyOwnedEntity
{
    private TeamsTenantRegistration() { }

    public TeamsTenantRegistration(
        Guid id,
        Guid companyId,
        Guid entraTenantId,
        Guid teamsAppId,
        Guid botApplicationId,
        string approvedMediaRoute,
        string requiredPermissions,
        Guid actorUserId,
        DateTime nowUtc)
    {
        EnsureId(companyId, nameof(companyId));
        EnsureId(entraTenantId, nameof(entraTenantId));
        EnsureId(teamsAppId, nameof(teamsAppId));
        EnsureId(botApplicationId, nameof(botApplicationId));
        EnsureId(actorUserId, nameof(actorUserId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        EntraTenantId = entraTenantId;
        TeamsAppId = teamsAppId;
        BotApplicationId = botApplicationId;
        ApprovedMediaRoute = Required(approvedMediaRoute, nameof(approvedMediaRoute), 64);
        RequiredPermissions = Required(requiredPermissions, nameof(requiredPermissions), 500);
        Status = "pending_consent";
        ConsentStatus = "pending";
        PermissionStatus = "pending";
        PolicyStatus = "pending";
        CreatedByUserId = actorUserId;
        CreatedUtc = Utc(nowUtc);
        UpdatedUtc = CreatedUtc;
        ConcurrencyVersion = 1;
    }

    public Guid? FirstUatMeetingId { get; private set; }
    public Guid? FirstUatOrganizerId { get; private set; }
    public Guid? FirstUatAuthorizedByUserId { get; private set; }
    public DateTime? FirstUatAuthorizedUtc { get; private set; }
    public DateTime? FirstUatExpiresUtc { get; private set; }
    public string? FirstUatReason { get; private set; }

    public void AuthorizeFirstUat(Guid meetingId, Guid organizerId, Guid actorId, DateTime now, int minutes, string reason)
    {
        EnsureId(meetingId, nameof(meetingId)); EnsureId(organizerId, nameof(organizerId)); EnsureId(actorId, nameof(actorId));
        if (Status != "ready") throw new InvalidOperationException("Verify tenant consent and policy before authorizing a test.");
        if (minutes is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (FirstUatExpiresUtc > Utc(now)) throw new InvalidOperationException("Revoke the existing test before authorizing another.");
        FirstUatReason = Required(reason, nameof(reason), 500);
        FirstUatMeetingId = meetingId; FirstUatOrganizerId = organizerId; FirstUatAuthorizedByUserId = actorId;
        FirstUatAuthorizedUtc = Utc(now); FirstUatExpiresUtc = Utc(now).AddMinutes(minutes); Touch(now);
    }
    public void RevokeFirstUat(DateTime now) { FirstUatExpiresUtc = Utc(now); Touch(now); }
    public bool AllowsFirstUat(Guid? meetingId, Guid? organizerId, DateTime now) =>
        Status == "ready" && meetingId.HasValue && organizerId.HasValue &&
        FirstUatMeetingId == meetingId && FirstUatOrganizerId == organizerId &&
        FirstUatAuthorizedUtc <= Utc(now) && FirstUatExpiresUtc > Utc(now);

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EntraTenantId { get; private set; }
    public Guid TeamsAppId { get; private set; }
    public Guid BotApplicationId { get; private set; }
    public string ApprovedMediaRoute { get; private set; } = null!;
    public string RequiredPermissions { get; private set; } = null!;
    public string GrantedPermissions { get; private set; } = string.Empty;
    public string Status { get; private set; } = null!;
    public string ConsentStatus { get; private set; } = null!;
    public string PermissionStatus { get; private set; } = null!;
    public string PolicyStatus { get; private set; } = null!;
    public string? FailureCode { get; private set; }
    public DateTime? ConsentVerifiedUtc { get; private set; }
    public Guid? ConsentVerifiedByUserId { get; private set; }
    public DateTime? PermissionsVerifiedUtc { get; private set; }
    public DateTime? PolicyApprovedUtc { get; private set; }
    public Guid? PolicyApprovedByUserId { get; private set; }
    public DateTime? DisabledUtc { get; private set; }
    public Guid? DisabledByUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public Company Company { get; private set; } = null!;

    public void RecordConsentVerification(
        string permissionStatus,
        string grantedPermissions,
        string? failureCode,
        Guid actorUserId,
        DateTime nowUtc)
    {
        EnsureId(actorUserId, nameof(actorUserId));
        ConsentStatus = "verified";
        PermissionStatus = Required(permissionStatus, nameof(permissionStatus), 32);
        GrantedPermissions = Optional(grantedPermissions, nameof(grantedPermissions), 500) ?? string.Empty;
        FailureCode = Optional(failureCode, nameof(failureCode), 120);
        ConsentVerifiedUtc = Utc(nowUtc);
        ConsentVerifiedByUserId = actorUserId;
        PermissionsVerifiedUtc = ConsentVerifiedUtc;
        DisabledUtc = null;
        DisabledByUserId = null;
        RecalculateStatus();
        Touch(nowUtc);
    }

    public void RecordConsentFailure(string failureCode, string permissionStatus, DateTime nowUtc)
    {
        FailureCode = Required(failureCode, nameof(failureCode), 120);
        PermissionStatus = Required(permissionStatus, nameof(permissionStatus), 32);
        Status = "blocked";
        if (FirstUatAuthorizedUtc.HasValue) FirstUatExpiresUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void AttestPolicy(bool approved, Guid actorUserId, DateTime nowUtc)
    {
        EnsureId(actorUserId, nameof(actorUserId));
        PolicyStatus = approved ? "attested" : "revoked";
        if (!approved && FirstUatAuthorizedUtc.HasValue) FirstUatExpiresUtc = Utc(nowUtc);
        PolicyApprovedUtc = approved ? Utc(nowUtc) : null;
        PolicyApprovedByUserId = approved ? actorUserId : null;
        FailureCode = approved ? FailureCode : "teams_identity.tenant_disabled";
        RecalculateStatus();
        Touch(nowUtc);
    }

    public void Disable(bool consentRevoked, Guid actorUserId, DateTime nowUtc)
    {
        EnsureId(actorUserId, nameof(actorUserId));
        Status = consentRevoked ? "revoked" : "disabled";
        if (FirstUatAuthorizedUtc.HasValue) FirstUatExpiresUtc = Utc(nowUtc);
        if (consentRevoked)
        {
            ConsentStatus = "revoked";
            PermissionStatus = "revoked";
            GrantedPermissions = string.Empty;
        }
        FailureCode = "teams_identity.tenant_disabled";
        DisabledUtc = Utc(nowUtc);
        DisabledByUserId = actorUserId;
        Touch(nowUtc);
    }

    private void RecalculateStatus()
    {
        if (DisabledUtc.HasValue)
        {
            return;
        }

        Status = ConsentStatus != "verified"
            ? "pending_consent"
            : PermissionStatus != "verified"
                ? "blocked"
                : PolicyStatus != "attested"
                    ? "pending_policy"
                    : "ready";
        if (Status == "ready") FailureCode = null;
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedUtc = Utc(nowUtc);
        ConcurrencyVersion++;
    }

    private static void EnsureId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException($"{name} is required.", name);
    }

    private static string Required(string? value, string name, int max) =>
        Optional(value, name, max) ?? throw new ArgumentException($"{name} is required.", name);

    private static string? Optional(string? value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > max) throw new ArgumentOutOfRangeException(name, $"{name} must be {max} characters or fewer.");
        return normalized;
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class TeamsAdminConsentSession : ICompanyOwnedEntity
{
    private TeamsAdminConsentSession() { }

    public TeamsAdminConsentSession(
        Guid id,
        Guid companyId,
        Guid registrationId,
        Guid actorUserId,
        string stateHash,
        DateTime expiresUtc,
        DateTime nowUtc)
    {
        if (companyId == Guid.Empty || registrationId == Guid.Empty || actorUserId == Guid.Empty)
            throw new ArgumentException("Company, registration, and actor identifiers are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RegistrationId = registrationId;
        ActorUserId = actorUserId;
        StateHash = string.IsNullOrWhiteSpace(stateHash) || stateHash.Length > 128
            ? throw new ArgumentException("A bounded state hash is required.", nameof(stateHash))
            : stateHash;
        CreatedUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
        ExpiresUtc = expiresUtc.Kind == DateTimeKind.Utc ? expiresUtc : expiresUtc.ToUniversalTime();
        if (ExpiresUtc <= CreatedUtc) throw new ArgumentOutOfRangeException(nameof(expiresUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RegistrationId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string StateHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConsumedUtc { get; private set; }
    public TeamsTenantRegistration Registration { get; private set; } = null!;

    public void Consume(DateTime nowUtc)
    {
        if (ConsumedUtc.HasValue) throw new InvalidOperationException("The consent state was already consumed.");
        ConsumedUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
    }
}
