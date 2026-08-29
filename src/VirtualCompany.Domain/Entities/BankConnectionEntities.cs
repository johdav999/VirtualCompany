using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class BankConnection : ICompanyOwnedEntity
{
    private BankConnection() { }

    public BankConnection(Guid id, Guid companyId, string providerKey, string institutionId,
        string institutionName, Guid connectedByUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        ProviderKey = Text(providerKey, 64, nameof(providerKey)).ToLowerInvariant();
        InstitutionId = Text(institutionId, 128, nameof(institutionId));
        InstitutionName = Text(institutionName, 200, nameof(institutionName));
        ConnectedByUserId = Required(connectedByUserId, nameof(connectedByUserId));
        Status = BankConnectionStatuses.PendingConsent;
        HealthStatus = BankConnectionHealthStatuses.Unknown;
        ReasonCode = BankConnectionReasonCodes.MissingConsent;
        ReasonSummary = "Bank consent must be completed before account access is available.";
        Version = 1;
        CreatedUtc = Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string InstitutionId { get; private set; } = null!;
    public string InstitutionName { get; private set; } = null!;
    public Guid ConnectedByUserId { get; private set; }
    public string Status { get; private set; } = null!;
    public string HealthStatus { get; private set; } = null!;
    public string? ReasonCode { get; private set; }
    public string? ReasonSummary { get; private set; }
    public DateTime? ConsentExpiresUtc { get; private set; }
    public DateTime? LastHealthCheckedUtc { get; private set; }
    public DateTime? SuspendedUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public DateTime? DisconnectedUtc { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User ConnectedByUser { get; private set; } = null!;
    public ICollection<BankConsentVersion> Consents { get; } = new List<BankConsentVersion>();
    public ICollection<BankDiscoveredAccount> DiscoveredAccounts { get; } = new List<BankDiscoveredAccount>();
    public ICollection<BankConnectionCapabilityGrant> CapabilityGrants { get; } = new List<BankConnectionCapabilityGrant>();

    public void Activate(DateTime? consentExpiresUtc, string healthStatus, DateTime nowUtc)
    {
        Status = BankConnectionStatuses.Active;
        HealthStatus = NormalizeHealth(healthStatus);
        ConsentExpiresUtc = consentExpiresUtc.HasValue ? Utc(consentExpiresUtc.Value) : null;
        ReasonCode = null;
        ReasonSummary = null;
        SuspendedUtc = null;
        RevokedUtc = null;
        DisconnectedUtc = null;
        Touch(nowUtc);
    }

    public void MarkAttention(string reasonCode, string reasonSummary, string healthStatus, DateTime nowUtc)
    {
        Status = BankConnectionStatuses.AttentionRequired;
        HealthStatus = NormalizeHealth(healthStatus);
        ReasonCode = Text(reasonCode, 96, nameof(reasonCode)).ToLowerInvariant();
        ReasonSummary = Text(reasonSummary, 1000, nameof(reasonSummary));
        Touch(nowUtc);
    }

    public void RecordHealth(string healthStatus, string? reasonCode, string? reasonSummary, DateTime checkedUtc)
    {
        HealthStatus = NormalizeHealth(healthStatus);
        LastHealthCheckedUtc = Utc(checkedUtc);
        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            Status = BankConnectionStatuses.AttentionRequired;
            ReasonCode = Text(reasonCode, 96, nameof(reasonCode)).ToLowerInvariant();
            ReasonSummary = Optional(reasonSummary, 1000);
        }
        else if (Status == BankConnectionStatuses.AttentionRequired)
        {
            Status = BankConnectionStatuses.Active;
            ReasonCode = null;
            ReasonSummary = null;
        }
        Touch(checkedUtc);
    }

    public void Suspend(string summary, DateTime nowUtc)
    {
        Status = BankConnectionStatuses.Suspended;
        ReasonCode = BankConnectionReasonCodes.Suspended;
        ReasonSummary = Text(summary, 1000, nameof(summary));
        SuspendedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void Revoke(string summary, DateTime nowUtc)
    {
        Status = BankConnectionStatuses.Revoked;
        ReasonCode = BankConnectionReasonCodes.Revoked;
        ReasonSummary = Text(summary, 1000, nameof(summary));
        RevokedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void Disconnect(string summary, DateTime nowUtc)
    {
        Status = BankConnectionStatuses.Disconnected;
        ReasonCode = BankConnectionReasonCodes.Disconnected;
        ReasonSummary = Text(summary, 1000, nameof(summary));
        DisconnectedUtc = Utc(nowUtc);
        Touch(nowUtc);
    }

    public void EnsureVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
            throw new InvalidOperationException("The bank connection changed after it was loaded.");
    }

    public void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedUtc = Utc(nowUtc);
    }

    private static string NormalizeHealth(string value) =>
        value is BankConnectionHealthStatuses.Healthy or BankConnectionHealthStatuses.Degraded or BankConnectionHealthStatuses.Outage
            ? value
            : BankConnectionHealthStatuses.Unknown;
    internal static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    internal static string Text(string value, int max, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
    internal static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
    internal static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class BankConsentSession : ICompanyOwnedEntity
{
    private BankConsentSession() { }
    public BankConsentSession(Guid id, Guid companyId, Guid? connectionId, string providerKey, string institutionId,
        Guid startedByUserId, string stateHash, string nonceHash, string? returnUri, bool isRenewal, DateTime expiresUtc, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = BankConnection.Required(companyId, nameof(companyId));
        ConnectionId = connectionId == Guid.Empty ? throw new ArgumentException("ConnectionId cannot be empty.", nameof(connectionId)) : connectionId;
        ProviderKey = BankConnection.Text(providerKey, 64, nameof(providerKey)).ToLowerInvariant();
        InstitutionId = BankConnection.Text(institutionId, 128, nameof(institutionId));
        StartedByUserId = BankConnection.Required(startedByUserId, nameof(startedByUserId));
        StateHash = BankConnection.Text(stateHash, 64, nameof(stateHash)).ToLowerInvariant();
        NonceHash = BankConnection.Text(nonceHash, 64, nameof(nonceHash)).ToLowerInvariant();
        ReturnUri = BankConnection.Optional(returnUri, 1000);
        IsRenewal = isRenewal;
        ExpiresUtc = BankConnection.Utc(expiresUtc);
        CreatedUtc = BankConnection.Utc(createdUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string InstitutionId { get; private set; } = null!;
    public Guid StartedByUserId { get; private set; }
    public string StateHash { get; private set; } = null!;
    public string NonceHash { get; private set; } = null!;
    public string? ProviderSessionReference { get; private set; }
    public string? ReturnUri { get; private set; }
    public bool IsRenewal { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConsumedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public BankConnection? Connection { get; private set; }
    public void SetProviderSession(string? reference) => ProviderSessionReference = BankConnection.Optional(reference, 256);
    public void AttachConnection(Guid connectionId) => ConnectionId = BankConnection.Required(connectionId, nameof(connectionId));
    public void Consume(DateTime nowUtc)
    {
        if (ConsumedUtc.HasValue) throw new InvalidOperationException("Bank consent callback state has already been used.");
        if (ExpiresUtc <= BankConnection.Utc(nowUtc)) throw new InvalidOperationException("Bank consent callback state has expired.");
        ConsumedUtc = BankConnection.Utc(nowUtc);
    }
}

public sealed class BankConsentVersion : ICompanyOwnedEntity
{
    private BankConsentVersion() { }
    public BankConsentVersion(Guid id, Guid companyId, Guid connectionId, int version, string providerConsentId,
        string status, DateTime effectiveUtc, DateTime? expiresUtc, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = BankConnection.Required(companyId, nameof(companyId));
        ConnectionId = BankConnection.Required(connectionId, nameof(connectionId)); Version = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version));
        ProviderConsentId = BankConnection.Text(providerConsentId, 256, nameof(providerConsentId)); Status = BankConnection.Text(status, 32, nameof(status));
        EffectiveUtc = BankConnection.Utc(effectiveUtc); ExpiresUtc = expiresUtc.HasValue ? BankConnection.Utc(expiresUtc.Value) : null; CreatedUtc = BankConnection.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ConnectionId { get; private set; }
    public int Version { get; private set; } public string ProviderConsentId { get; private set; } = null!; public string Status { get; private set; } = null!;
    public DateTime EffectiveUtc { get; private set; } public DateTime? ExpiresUtc { get; private set; } public DateTime? EndedUtc { get; private set; } public DateTime CreatedUtc { get; private set; }
    public BankConnection Connection { get; private set; } = null!;
    public void Supersede(DateTime nowUtc) { Status = BankConsentStatuses.Superseded; EndedUtc = BankConnection.Utc(nowUtc); }
    public void Expire(DateTime nowUtc) { Status = BankConsentStatuses.Expired; EndedUtc = BankConnection.Utc(nowUtc); }
    public void Revoke(DateTime nowUtc) { Status = BankConsentStatuses.Revoked; EndedUtc = BankConnection.Utc(nowUtc); }
}

public sealed class BankConnectionCapabilityGrant : ICompanyOwnedEntity
{
    private BankConnectionCapabilityGrant() { }
    public BankConnectionCapabilityGrant(Guid id, Guid companyId, Guid connectionId, Guid consentVersionId, string capability, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = BankConnection.Required(companyId, nameof(companyId)); ConnectionId = BankConnection.Required(connectionId, nameof(connectionId)); ConsentVersionId = BankConnection.Required(consentVersionId, nameof(consentVersionId)); Capability = BankConnection.Text(capability, 96, nameof(capability)).ToLowerInvariant(); CreatedUtc = BankConnection.Utc(createdUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ConnectionId { get; private set; } public Guid ConsentVersionId { get; private set; }
    public string Capability { get; private set; } = null!; public DateTime CreatedUtc { get; private set; } public BankConnection Connection { get; private set; } = null!; public BankConsentVersion ConsentVersion { get; private set; } = null!;
}

public sealed class BankDiscoveredAccount : ICompanyOwnedEntity
{
    private BankDiscoveredAccount() { }
    public BankDiscoveredAccount(Guid id, Guid companyId, Guid connectionId, string providerAccountId, string displayName,
        string maskedAccountNumber, string currency, string ownershipStatus, string? ownershipSummary, DateTime discoveredUtc,
        string? providerAccessReference = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = BankConnection.Required(companyId, nameof(companyId)); ConnectionId = BankConnection.Required(connectionId, nameof(connectionId));
        ProviderAccountId = BankConnection.Text(providerAccountId, 256, nameof(providerAccountId)); DisplayName = BankConnection.Text(displayName, 200, nameof(displayName));
        MaskedAccountNumber = BankConnection.Text(maskedAccountNumber, 64, nameof(maskedAccountNumber)); Currency = NormalizeCurrency(currency); OwnershipStatus = NormalizeOwnership(ownershipStatus);
        OwnershipSummary = BankConnection.Optional(ownershipSummary, 500); ProviderAccessReference = BankConnection.Optional(providerAccessReference, 256);
        IsAvailable = true; FirstDiscoveredUtc = BankConnection.Utc(discoveredUtc); LastSeenUtc = FirstDiscoveredUtc; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ConnectionId { get; private set; }
    public string ProviderAccountId { get; private set; } = null!; public string DisplayName { get; private set; } = null!; public string MaskedAccountNumber { get; private set; } = null!;
    public string? ProviderAccessReference { get; private set; }
    public string Currency { get; private set; } = null!; public string OwnershipStatus { get; private set; } = null!; public string? OwnershipSummary { get; private set; }
    public bool IsAvailable { get; private set; } public int Version { get; private set; } public DateTime FirstDiscoveredUtc { get; private set; } public DateTime LastSeenUtc { get; private set; }
    public BankConnection Connection { get; private set; } = null!; public ICollection<BankAccountMapping> Mappings { get; } = new List<BankAccountMapping>();
    public void Refresh(string displayName, string maskedAccountNumber, string currency, string ownershipStatus, string? ownershipSummary, DateTime nowUtc,
        string? providerAccessReference = null)
    { DisplayName = BankConnection.Text(displayName, 200, nameof(displayName)); MaskedAccountNumber = BankConnection.Text(maskedAccountNumber, 64, nameof(maskedAccountNumber)); Currency = NormalizeCurrency(currency); OwnershipStatus = NormalizeOwnership(ownershipStatus); OwnershipSummary = BankConnection.Optional(ownershipSummary, 500); ProviderAccessReference = BankConnection.Optional(providerAccessReference, 256) ?? ProviderAccessReference; IsAvailable = true; LastSeenUtc = BankConnection.Utc(nowUtc); Version++; }
    private static string NormalizeCurrency(string value) { var result = BankConnection.Text(value, 3, nameof(value)).ToUpperInvariant(); return result.Length == 3 && result.All(char.IsLetter) ? result : throw new ArgumentOutOfRangeException(nameof(value)); }
    private static string NormalizeOwnership(string value) => value is BankAccountOwnershipStatuses.Verified or BankAccountOwnershipStatuses.Mismatch ? value : BankAccountOwnershipStatuses.Unverified;
}

public sealed class BankAccountMapping : ICompanyOwnedEntity
{
    private BankAccountMapping() { }
    public BankAccountMapping(Guid id, Guid companyId, Guid discoveredAccountId, Guid companyBankAccountId, int version,
        Guid mappedByUserId, string reason, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = BankConnection.Required(companyId, nameof(companyId)); DiscoveredAccountId = BankConnection.Required(discoveredAccountId, nameof(discoveredAccountId)); CompanyBankAccountId = BankConnection.Required(companyBankAccountId, nameof(companyBankAccountId)); Version = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version)); MappedByUserId = BankConnection.Required(mappedByUserId, nameof(mappedByUserId)); Reason = BankConnection.Text(reason, 500, nameof(reason)); IsCurrent = true; CreatedUtc = BankConnection.Utc(createdUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid DiscoveredAccountId { get; private set; } public Guid CompanyBankAccountId { get; private set; }
    public int Version { get; private set; } public Guid MappedByUserId { get; private set; } public string Reason { get; private set; } = null!; public bool IsCurrent { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime? SupersededUtc { get; private set; } public BankDiscoveredAccount DiscoveredAccount { get; private set; } = null!; public CompanyBankAccount CompanyBankAccount { get; private set; } = null!;
    public void Supersede(DateTime nowUtc) { if (!IsCurrent) return; IsCurrent = false; SupersededUtc = BankConnection.Utc(nowUtc); }
}

public sealed class BankConnectionCredential : ICompanyOwnedEntity
{
    private BankConnectionCredential() { }
    public BankConnectionCredential(Guid id, Guid companyId, Guid connectionId, string encryptedEnvelope, string encryptionKeyId, DateTime? expiresUtc, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = BankConnection.Required(companyId, nameof(companyId)); ConnectionId = BankConnection.Required(connectionId, nameof(connectionId)); EncryptedEnvelope = BankConnection.Text(encryptedEnvelope, 16000, nameof(encryptedEnvelope)); EncryptionKeyId = BankConnection.Text(encryptionKeyId, 128, nameof(encryptionKeyId)); ExpiresUtc = expiresUtc.HasValue ? BankConnection.Utc(expiresUtc.Value) : null; CreatedUtc = BankConnection.Utc(createdUtc); UpdatedUtc = CreatedUtc; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ConnectionId { get; private set; } public string EncryptedEnvelope { get; private set; } = null!; public string EncryptionKeyId { get; private set; } = null!; public DateTime? ExpiresUtc { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public BankConnection Connection { get; private set; } = null!;
    public void Replace(string encryptedEnvelope, string encryptionKeyId, DateTime? expiresUtc, DateTime nowUtc) { EncryptedEnvelope = BankConnection.Text(encryptedEnvelope, 16000, nameof(encryptedEnvelope)); EncryptionKeyId = BankConnection.Text(encryptionKeyId, 128, nameof(encryptionKeyId)); ExpiresUtc = expiresUtc.HasValue ? BankConnection.Utc(expiresUtc.Value) : null; UpdatedUtc = BankConnection.Utc(nowUtc); }
}

public sealed class BankConnectionAuditEvent : ICompanyOwnedEntity
{
    private BankConnectionAuditEvent() { }
    public BankConnectionAuditEvent(Guid id, Guid companyId, Guid? connectionId, Guid actorUserId, string eventType, string outcome,
        string summary, string? reasonCode, string? correlationId, string? beforeState, string? afterState, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = BankConnection.Required(companyId, nameof(companyId)); ConnectionId = connectionId == Guid.Empty ? null : connectionId; ActorUserId = BankConnection.Required(actorUserId, nameof(actorUserId)); EventType = BankConnection.Text(eventType, 96, nameof(eventType)); Outcome = BankConnection.Text(outcome, 32, nameof(outcome)); Summary = BankConnection.Text(summary, 1000, nameof(summary)); ReasonCode = BankConnection.Optional(reasonCode, 96); CorrelationId = BankConnection.Optional(correlationId, 128); BeforeState = BankConnection.Optional(beforeState, 2000); AfterState = BankConnection.Optional(afterState, 2000); CreatedUtc = BankConnection.Utc(createdUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid? ConnectionId { get; private set; } public Guid ActorUserId { get; private set; }
    public string EventType { get; private set; } = null!; public string Outcome { get; private set; } = null!; public string Summary { get; private set; } = null!; public string? ReasonCode { get; private set; }
    public string? CorrelationId { get; private set; } public string? BeforeState { get; private set; } public string? AfterState { get; private set; } public DateTime CreatedUtc { get; private set; }
    public BankConnection? Connection { get; private set; }
}

public sealed class BankConsentRevocationTask : ICompanyOwnedEntity
{
    private BankConsentRevocationTask() { }
    public BankConsentRevocationTask(Guid id, Guid companyId, Guid connectionId, Guid consentVersionId, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = BankConnection.Required(companyId, nameof(companyId)); ConnectionId = BankConnection.Required(connectionId, nameof(connectionId)); ConsentVersionId = BankConnection.Required(consentVersionId, nameof(consentVersionId)); Status = "pending"; CreatedUtc = BankConnection.Utc(createdUtc); UpdatedUtc = CreatedUtc; NextAttemptUtc = CreatedUtc; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ConnectionId { get; private set; } public Guid ConsentVersionId { get; private set; }
    public string Status { get; private set; } = null!; public int AttemptCount { get; private set; } public DateTime NextAttemptUtc { get; private set; } public DateTime? LeaseExpiresUtc { get; private set; }
    public string? SafeFailureSummary { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public BankConnection Connection { get; private set; } = null!; public BankConsentVersion ConsentVersion { get; private set; } = null!;
    public void Claim(DateTime nowUtc) { Status = "running"; AttemptCount++; LeaseExpiresUtc = BankConnection.Utc(nowUtc).AddMinutes(2); UpdatedUtc = BankConnection.Utc(nowUtc); }
    public void Complete(DateTime nowUtc) { Status = "completed"; LeaseExpiresUtc = null; SafeFailureSummary = null; UpdatedUtc = BankConnection.Utc(nowUtc); }
    public void Retry(string safeSummary, DateTime nowUtc) { Status = AttemptCount >= 5 ? "failed" : "pending"; SafeFailureSummary = BankConnection.Optional(safeSummary, 1000); LeaseExpiresUtc = null; NextAttemptUtc = BankConnection.Utc(nowUtc).AddMinutes(Math.Min(60, Math.Pow(2, AttemptCount))); UpdatedUtc = BankConnection.Utc(nowUtc); }
}
