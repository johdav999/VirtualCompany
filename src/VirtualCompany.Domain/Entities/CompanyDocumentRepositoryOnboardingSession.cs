namespace VirtualCompany.Domain.Entities;

public sealed class CompanyDocumentRepositoryOnboardingSession : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryOnboardingSession() { }
    public CompanyDocumentRepositoryOnboardingSession(Guid companyId, Guid initiatingUserId, string sessionHandleHash,
        string stateHash, string protectedSetupMaterial, string returnPath, string correlationId,
        DateTime createdUtc, DateTime expiresUtc)
    {
        if (companyId == Guid.Empty || initiatingUserId == Guid.Empty) throw new ArgumentException("Company and initiating user are required.");
        if (expiresUtc <= createdUtc) throw new ArgumentException("Expiry must be after creation.", nameof(expiresUtc));
        Id = Guid.NewGuid(); CompanyId = companyId; InitiatingUserId = initiatingUserId;
        SessionHandleHash = Require(sessionHandleHash, nameof(sessionHandleHash), 64);
        StateHash = Require(stateHash, nameof(stateHash), 64);
        ProtectedSetupMaterial = Require(protectedSetupMaterial, nameof(protectedSetupMaterial), 12000);
        ReturnPath = Require(returnPath, nameof(returnPath), 500);
        CorrelationId = Require(correlationId, nameof(correlationId), 100);
        Status = DocumentRepositoryOnboardingStatuses.AwaitingAuthorization;
        CreatedUtc = Utc(createdUtc); UpdatedUtc = CreatedUtc; ExpiresUtc = Utc(expiresUtc); ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InitiatingUserId { get; private set; }
    public string SessionHandleHash { get; private set; } = null!;
    public string StateHash { get; private set; } = null!;
    public string? ProtectedSetupMaterial { get; private set; }
    public string ReturnPath { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid? ProviderTenantId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime? CallbackReceivedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public DateTime? ExpiredUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public Company Company { get; private set; } = null!;
    public void CompleteAuthorization(Guid tenantId, string protectedDelegatedMaterial, DateTime nowUtc)
    {
        EnsureAwaiting(nowUtc);
        if (tenantId == Guid.Empty) throw new ArgumentException("Provider tenant is required.", nameof(tenantId));
        ProviderTenantId = tenantId; ProtectedSetupMaterial = Require(protectedDelegatedMaterial, nameof(protectedDelegatedMaterial), 12000);
        Status = DocumentRepositoryOnboardingStatuses.Authorized; CallbackReceivedUtc = Utc(nowUtc); CompletedUtc = CallbackReceivedUtc;
        FailureCode = null; FailureSummary = null; Touch(nowUtc);
    }
    public void MarkCallbackClaimed(DateTime nowUtc)
    {
        if (Status != DocumentRepositoryOnboardingStatuses.AwaitingAuthorization) throw new InvalidOperationException("The onboarding callback was already consumed.");
        Status = DocumentRepositoryOnboardingStatuses.Authorizing; CallbackReceivedUtc = Utc(nowUtc); Touch(nowUtc);
    }
    public void Fail(string code, string safeSummary, DateTime nowUtc)
    {
        if (IsTerminal) return;
        Status = DocumentRepositoryOnboardingStatuses.Failed; FailureCode = Require(code, nameof(code), 64);
        FailureSummary = Require(safeSummary, nameof(safeSummary), 500); ProtectedSetupMaterial = null;
        CallbackReceivedUtc = Utc(nowUtc); CompletedUtc = CallbackReceivedUtc; Touch(nowUtc);
    }
    public void Cancel(DateTime nowUtc)
    {
        if (IsTerminal) return;
        Status = DocumentRepositoryOnboardingStatuses.Cancelled; ProtectedSetupMaterial = null; CancelledUtc = Utc(nowUtc); CompletedUtc = CancelledUtc;
        FailureCode = DocumentRepositoryOnboardingFailureCodes.Cancelled; FailureSummary = "Microsoft 365 setup was cancelled. Start again to continue."; Touch(nowUtc);
    }
    public void Expire(DateTime nowUtc)
    {
        if (IsTerminal) return;
        Status = DocumentRepositoryOnboardingStatuses.Expired; ProtectedSetupMaterial = null; ExpiredUtc = Utc(nowUtc); CompletedUtc = ExpiredUtc;
        FailureCode = DocumentRepositoryOnboardingFailureCodes.ExpiredOrReplayedState; FailureSummary = "Microsoft 365 setup expired. Start again to continue."; Touch(nowUtc);
    }
    public void UpdateProtectedMaterial(string protectedMaterial, DateTime nowUtc)
    {
        if (Status is not (DocumentRepositoryOnboardingStatuses.Authorized or DocumentRepositoryOnboardingStatuses.Provisioning) || ExpiresUtc <= Utc(nowUtc)) throw new InvalidOperationException("The onboarding session is not available for setup.");
        ProtectedSetupMaterial = Require(protectedMaterial, nameof(protectedMaterial), 12000); Touch(nowUtc);
    }
    public void ConsumeDelegatedMaterial(DateTime nowUtc) { ProtectedSetupMaterial = null; Touch(nowUtc); }
    public void MarkProvisioning(DateTime nowUtc)
    {
        if (Status != DocumentRepositoryOnboardingStatuses.Authorized || ProtectedSetupMaterial is null) throw new InvalidOperationException("The onboarding session is not ready for provisioning.");
        Status = DocumentRepositoryOnboardingStatuses.Provisioning; Touch(nowUtc);
    }
    public void MarkConnected(DateTime nowUtc)
    {
        Status = DocumentRepositoryOnboardingStatuses.Connected; ProtectedSetupMaterial = null;
        CompletedUtc = Utc(nowUtc); FailureCode = null; FailureSummary = null; Touch(nowUtc);
    }
    public bool IsTerminal => Status is DocumentRepositoryOnboardingStatuses.Failed or DocumentRepositoryOnboardingStatuses.Cancelled or DocumentRepositoryOnboardingStatuses.Expired or DocumentRepositoryOnboardingStatuses.Connected;
    private void EnsureAwaiting(DateTime nowUtc)
    {
        if (ExpiresUtc <= Utc(nowUtc)) throw new InvalidOperationException("The onboarding session expired.");
        if (Status != DocumentRepositoryOnboardingStatuses.Authorizing) throw new InvalidOperationException("The onboarding callback was not claimed.");
    }
    private void Touch(DateTime value) { UpdatedUtc = Utc(value); ConcurrencyVersion++; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Require(string value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name); value = value.Trim(); if (value.Length > max) throw new ArgumentOutOfRangeException(name); return value; }
}

public static class DocumentRepositoryOnboardingStatuses
{
    public const string AwaitingAuthorization = "awaiting_authorization";
    public const string Authorizing = "authorizing";
    public const string Authorized = "authorized";
    public const string Provisioning = "provisioning";
    public const string Connected = "connected";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string AccessLost = "access_lost";
    public const string DiscoveryAccessDenied = "discovery_access_denied";
    public const string SourceUnavailable = "source_unavailable";
    public const string InvalidSelection = "invalid_selection";
    public const string Expired = "expired";
}

public static class DocumentRepositoryOnboardingFailureCodes
{
    public const string ConsentDenied = "consent_denied";
    public const string NonOrganizationalAccount = "non_organizational_account";
    public const string WrongTenant = "wrong_tenant";
    public const string InsufficientAdministratorAuthority = "insufficient_administrator_authority";
    public const string ExpiredOrReplayedState = "expired_or_replayed_state";
    public const string InvalidCallback = "invalid_callback";
    public const string ConfigurationUnavailable = "configuration_unavailable";
    public const string ProviderThrottled = "provider_throttled";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string Cancelled = "cancelled";
    public const string AccessLost = "access_lost";
    public const string DiscoveryAccessDenied = "discovery_access_denied";
    public const string SourceUnavailable = "source_unavailable";
    public const string InvalidSelection = "invalid_selection";
}
