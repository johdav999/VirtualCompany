using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class BankFeedCheckpoint : ICompanyOwnedEntity
{
    private BankFeedCheckpoint() { }

    public BankFeedCheckpoint(Guid id, Guid companyId, Guid connectionId, Guid discoveredAccountId,
        Guid accountMappingId, int accountMappingVersion, Guid companyBankAccountId, string providerKey,
        string stableProviderAccountId, string providerAccountAccessReference, DateTime nowUtc)
    {
        Id = BankFeedText.Id(id, nameof(id));
        CompanyId = BankFeedText.Id(companyId, nameof(companyId));
        ConnectionId = BankFeedText.Id(connectionId, nameof(connectionId));
        DiscoveredAccountId = BankFeedText.Id(discoveredAccountId, nameof(discoveredAccountId));
        AccountMappingId = BankFeedText.Id(accountMappingId, nameof(accountMappingId));
        AccountMappingVersion = accountMappingVersion > 0 ? accountMappingVersion : throw new ArgumentOutOfRangeException(nameof(accountMappingVersion));
        CompanyBankAccountId = BankFeedText.Id(companyBankAccountId, nameof(companyBankAccountId));
        ProviderKey = BankFeedText.Required(providerKey, 64, nameof(providerKey)).ToLowerInvariant();
        StableProviderAccountId = BankFeedText.Required(stableProviderAccountId, 512, nameof(stableProviderAccountId));
        ProviderAccountAccessReference = BankFeedText.Required(providerAccountAccessReference, 256, nameof(providerAccountAccessReference));
        Status = BankFeedCheckpointStatuses.Ready;
        Phase = BankFeedSynchronizationPhases.Booked;
        Version = 1;
        NextAttemptUtc = BankFeedText.Utc(nowUtc);
        CreatedUtc = UpdatedUtc = NextAttemptUtc.Value;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public Guid DiscoveredAccountId { get; private set; }
    public Guid AccountMappingId { get; private set; }
    public int AccountMappingVersion { get; private set; }
    public Guid CompanyBankAccountId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string StableProviderAccountId { get; private set; } = null!;
    public string ProviderAccountAccessReference { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string Phase { get; private set; } = null!;
    public string? ReasonCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateOnly? CoverageFrom { get; private set; }
    public DateOnly? CoverageThrough { get; private set; }
    public DateOnly? WindowFrom { get; private set; }
    public DateOnly? WindowTo { get; private set; }
    public Guid? RecoveryGapId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public string? CorrelationId { get; private set; }
    public Guid? SynchronizationRunId { get; private set; }
    public string? ContinuationTokenEnvelope { get; private set; }
    public string? ContinuationTokenHash { get; private set; }
    public int PageNumber { get; private set; }
    public int AttemptCount { get; private set; }
    public int ImportedBookedCount { get; private set; }
    public int ObservedPendingCount { get; private set; }
    public DateTime? LastAttemptUtc { get; private set; }
    public DateTime? LastSuccessfulSyncUtc { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void ApplyMapping(Guid mappingId, int mappingVersion, Guid companyBankAccountId,
        string accessReference, DateTime nowUtc)
    {
        AccountMappingId = BankFeedText.Id(mappingId, nameof(mappingId));
        AccountMappingVersion = mappingVersion > 0 ? mappingVersion : throw new ArgumentOutOfRangeException(nameof(mappingVersion));
        CompanyBankAccountId = BankFeedText.Id(companyBankAccountId, nameof(companyBankAccountId));
        ProviderAccountAccessReference = BankFeedText.Required(accessReference, 256, nameof(accessReference));
        Touch(nowUtc);
    }

    public void Queue(DateOnly from, DateOnly to, Guid? actorUserId, Guid? recoveryGapId,
        string? correlationId, DateTime nowUtc)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to));
        var now = BankFeedText.Utc(nowUtc);
        if (Status == BankFeedCheckpointStatuses.Running && LeaseExpiresUtc > now)
            throw new InvalidOperationException("The bank feed is already synchronizing.");
        if (actorUserId == Guid.Empty || recoveryGapId == Guid.Empty) throw new ArgumentException("Identifiers cannot be empty.");
        WindowFrom = from;
        WindowTo = to;
        RequestedByUserId = actorUserId;
        RecoveryGapId = recoveryGapId;
        CorrelationId = BankFeedText.Optional(correlationId, 128);
        SynchronizationRunId = Guid.NewGuid();
        Phase = BankFeedSynchronizationPhases.Booked;
        ContinuationTokenEnvelope = ContinuationTokenHash = null;
        PageNumber = AttemptCount = ImportedBookedCount = ObservedPendingCount = 0;
        ReasonCode = FailureSummary = LeaseOwner = null;
        LeaseExpiresUtc = null;
        Status = BankFeedCheckpointStatuses.Queued;
        NextAttemptUtc = now;
        Touch(now);
    }

    public bool TryClaim(string owner, DateTime nowUtc, TimeSpan leaseDuration)
    {
        var now = BankFeedText.Utc(nowUtc);
        if (Status == BankFeedCheckpointStatuses.AttentionRequired || Status == BankFeedCheckpointStatuses.Paused ||
            NextAttemptUtc > now || LeaseExpiresUtc > now && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return false;
        LeaseOwner = BankFeedText.Required(owner, 128, nameof(owner));
        LeaseExpiresUtc = now.Add(leaseDuration);
        Status = BankFeedCheckpointStatuses.Running;
        LastAttemptUtc = now;
        AttemptCount++;
        NextAttemptUtc = null;
        Touch(now);
        return true;
    }

    public bool IsClaimedBy(string owner, DateTime nowUtc) =>
        Status == BankFeedCheckpointStatuses.Running && string.Equals(LeaseOwner, owner, StringComparison.Ordinal) &&
        LeaseExpiresUtc >= BankFeedText.Utc(nowUtc);

    public void ContinuePage(string owner, string? continuationEnvelope, string? continuationHash,
        int bookedCount, int pendingCount, DateTime nowUtc)
    {
        RequireLease(owner, nowUtc);
        ContinuationTokenEnvelope = BankFeedText.Optional(continuationEnvelope, 8000);
        ContinuationTokenHash = BankFeedText.Optional(continuationHash, 64);
        ImportedBookedCount += Math.Max(0, bookedCount);
        ObservedPendingCount += Math.Max(0, pendingCount);
        PageNumber++;
        Touch(nowUtc);
    }

    public void BeginPendingPhase(string owner, DateTime nowUtc)
    {
        RequireLease(owner, nowUtc);
        Phase = BankFeedSynchronizationPhases.Pending;
        ContinuationTokenEnvelope = ContinuationTokenHash = null;
        PageNumber = 0;
        Touch(nowUtc);
    }

    public void Complete(string owner, DateTime nowUtc, TimeSpan nextInterval)
    {
        RequireLease(owner, nowUtc);
        if (!WindowFrom.HasValue || !WindowTo.HasValue) throw new InvalidOperationException("The synchronization window is missing.");
        CoverageFrom = CoverageFrom.HasValue && CoverageFrom.Value < WindowFrom.Value ? CoverageFrom : WindowFrom;
        CoverageThrough = CoverageThrough.HasValue && CoverageThrough.Value > WindowTo.Value ? CoverageThrough : WindowTo;
        LastSuccessfulSyncUtc = BankFeedText.Utc(nowUtc);
        Status = BankFeedCheckpointStatuses.Ready;
        ReasonCode = FailureSummary = LeaseOwner = ContinuationTokenEnvelope = ContinuationTokenHash = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = LastSuccessfulSyncUtc.Value.Add(nextInterval);
        WindowFrom = WindowTo = null;
        RequestedByUserId = null;
        CorrelationId = null;
        SynchronizationRunId = null;
        RecoveryGapId = null;
        Phase = BankFeedSynchronizationPhases.Booked;
        PageNumber = AttemptCount = 0;
        Touch(nowUtc);
    }

    public void Retry(string owner, string reasonCode, string summary, DateTime nowUtc, TimeSpan delay)
    {
        RequireLease(owner, nowUtc);
        Status = BankFeedCheckpointStatuses.Failed;
        ReasonCode = BankFeedText.Required(reasonCode, 96, nameof(reasonCode));
        FailureSummary = BankFeedText.Required(summary, 1000, nameof(summary));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = BankFeedText.Utc(nowUtc).Add(delay);
        Touch(nowUtc);
    }

    public void RequireAttention(string? owner, string reasonCode, string summary, DateTime nowUtc)
    {
        if (owner is not null) RequireLease(owner, nowUtc);
        Status = BankFeedCheckpointStatuses.AttentionRequired;
        ReasonCode = BankFeedText.Required(reasonCode, 96, nameof(reasonCode));
        FailureSummary = BankFeedText.Required(summary, 1000, nameof(summary));
        LeaseOwner = null;
        LeaseExpiresUtc = NextAttemptUtc = null;
        Touch(nowUtc);
    }

    public void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The bank feed changed after it was loaded.");
    }

    private void RequireLease(string owner, DateTime nowUtc)
    {
        if (!IsClaimedBy(owner, nowUtc)) throw new InvalidOperationException("The bank feed synchronization lease is no longer current.");
    }

    private void Touch(DateTime nowUtc) { UpdatedUtc = BankFeedText.Utc(nowUtc); Version++; }
}

public sealed class BankFeedRawSourceObject : ICompanyOwnedEntity
{
    private BankFeedRawSourceObject() { }
    public BankFeedRawSourceObject(Guid id, Guid companyId, Guid checkpointId, Guid synchronizationRunId,
        string sourceIdentity, string sourceKind, string checksum, string encryptedPayload, string contentType,
        DateTime retentionExpiresUtc, DateTime createdUtc)
    {
        Id = BankFeedText.Id(id, nameof(id)); CompanyId = BankFeedText.Id(companyId, nameof(companyId));
        CheckpointId = BankFeedText.Id(checkpointId, nameof(checkpointId)); SynchronizationRunId = BankFeedText.Id(synchronizationRunId, nameof(synchronizationRunId));
        SourceIdentity = BankFeedText.Required(sourceIdentity, 256, nameof(sourceIdentity)); SourceKind = BankFeedText.Required(sourceKind, 32, nameof(sourceKind));
        Checksum = BankFeedText.Hash(checksum, nameof(checksum)); EncryptedPayload = BankFeedText.Required(encryptedPayload, 2_000_000, nameof(encryptedPayload));
        ContentType = BankFeedText.Required(contentType, 100, nameof(contentType)); RetentionExpiresUtc = BankFeedText.Utc(retentionExpiresUtc); CreatedUtc = BankFeedText.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CheckpointId { get; private set; }
    public Guid SynchronizationRunId { get; private set; } public string SourceIdentity { get; private set; } = null!; public string SourceKind { get; private set; } = null!;
    public string Checksum { get; private set; } = null!; public string? EncryptedPayload { get; private set; } public string ContentType { get; private set; } = null!;
    public DateTime RetentionExpiresUtc { get; private set; } public DateTime? PayloadPurgedUtc { get; private set; } public DateTime CreatedUtc { get; private set; }
    public void PurgePayload(DateTime nowUtc)
    {
        var now = BankFeedText.Utc(nowUtc);
        if (now < RetentionExpiresUtc || EncryptedPayload is null) return;
        EncryptedPayload = null;
        PayloadPurgedUtc = now;
    }
}

public sealed class BankFeedSourceTransaction : ICompanyOwnedEntity
{
    private BankFeedSourceTransaction() { }
    public BankFeedSourceTransaction(Guid id, Guid companyId, Guid checkpointId, string stableIdentity, string status,
        DateTime? bookingDateUtc, DateTime? valueDateUtc, DateTime transactionDateUtc, decimal amount, string currency,
        string referenceText, string counterparty, string? providerTransactionReference, string contentHash,
        Guid rawSourceObjectId, DateTime nowUtc)
    {
        Id = BankFeedText.Id(id, nameof(id)); CompanyId = BankFeedText.Id(companyId, nameof(companyId)); CheckpointId = BankFeedText.Id(checkpointId, nameof(checkpointId));
        StableIdentity = BankFeedText.Required(stableIdentity, 256, nameof(stableIdentity)); Status = NormalizeStatus(status);
        BookingDateUtc = bookingDateUtc.HasValue ? BankFeedText.Utc(bookingDateUtc.Value) : null; ValueDateUtc = valueDateUtc.HasValue ? BankFeedText.Utc(valueDateUtc.Value) : null;
        TransactionDateUtc = BankFeedText.Utc(transactionDateUtc); Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (Amount == 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Currency = BankFeedText.Required(currency, 3, nameof(currency)).ToUpperInvariant(); ReferenceText = BankFeedText.Required(referenceText, 240, nameof(referenceText));
        Counterparty = BankFeedText.Required(counterparty, 200, nameof(counterparty)); ProviderTransactionReference = BankFeedText.Optional(providerTransactionReference, 256);
        ContentHash = BankFeedText.Hash(contentHash, nameof(contentHash)); RawSourceObjectId = BankFeedText.Id(rawSourceObjectId, nameof(rawSourceObjectId));
        Version = 1; FirstSeenUtc = LastSeenUtc = BankFeedText.Utc(nowUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CheckpointId { get; private set; }
    public string StableIdentity { get; private set; } = null!; public string Status { get; private set; } = null!;
    public DateTime? BookingDateUtc { get; private set; } public DateTime? ValueDateUtc { get; private set; } public DateTime TransactionDateUtc { get; private set; }
    public decimal Amount { get; private set; } public string Currency { get; private set; } = null!; public string ReferenceText { get; private set; } = null!;
    public string Counterparty { get; private set; } = null!; public string? ProviderTransactionReference { get; private set; } public string ContentHash { get; private set; } = null!;
    public Guid RawSourceObjectId { get; private set; } public Guid? BankTransactionId { get; private set; } public long Version { get; private set; }
    public DateTime FirstSeenUtc { get; private set; } public DateTime LastSeenUtc { get; private set; }

    public void ObservePending(string contentHash, Guid rawSourceObjectId, DateTime nowUtc)
    {
        if (Status == BankFeedSourceTransactionStatuses.Booked) return;
        ContentHash = BankFeedText.Hash(contentHash, nameof(contentHash)); RawSourceObjectId = BankFeedText.Id(rawSourceObjectId, nameof(rawSourceObjectId));
        LastSeenUtc = BankFeedText.Utc(nowUtc); Version++;
    }

    public void PromoteToBooked(DateTime bookingDateUtc, DateTime valueDateUtc, DateTime transactionDateUtc,
        decimal amount, string currency, string referenceText, string counterparty, string? providerTransactionReference,
        string contentHash, Guid rawSourceObjectId, Guid bankTransactionId, DateTime nowUtc)
    {
        Status = BankFeedSourceTransactionStatuses.Booked; BookingDateUtc = BankFeedText.Utc(bookingDateUtc); ValueDateUtc = BankFeedText.Utc(valueDateUtc);
        TransactionDateUtc = BankFeedText.Utc(transactionDateUtc); Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = BankFeedText.Required(currency, 3, nameof(currency)).ToUpperInvariant(); ReferenceText = BankFeedText.Required(referenceText, 240, nameof(referenceText));
        Counterparty = BankFeedText.Required(counterparty, 200, nameof(counterparty)); ProviderTransactionReference = BankFeedText.Optional(providerTransactionReference, 256);
        ContentHash = BankFeedText.Hash(contentHash, nameof(contentHash)); RawSourceObjectId = BankFeedText.Id(rawSourceObjectId, nameof(rawSourceObjectId));
        BankTransactionId = BankFeedText.Id(bankTransactionId, nameof(bankTransactionId)); LastSeenUtc = BankFeedText.Utc(nowUtc); Version++;
    }

    private static string NormalizeStatus(string status) => status switch
    {
        BankFeedSourceTransactionStatuses.Pending => status,
        BankFeedSourceTransactionStatuses.Booked => status,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}

public sealed class BankFeedBalanceSnapshot : ICompanyOwnedEntity
{
    private BankFeedBalanceSnapshot() { }
    public BankFeedBalanceSnapshot(Guid id, Guid companyId, Guid checkpointId, Guid rawSourceObjectId,
        string balanceType, decimal amount, string currency, DateTime? observedUtc, DateOnly? referenceDate,
        string? lastCommittedTransactionIdentity, DateTime createdUtc)
    {
        Id = BankFeedText.Id(id, nameof(id)); CompanyId = BankFeedText.Id(companyId, nameof(companyId)); CheckpointId = BankFeedText.Id(checkpointId, nameof(checkpointId));
        RawSourceObjectId = BankFeedText.Id(rawSourceObjectId, nameof(rawSourceObjectId)); BalanceType = BankFeedText.Required(balanceType, 32, nameof(balanceType));
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero); Currency = BankFeedText.Required(currency, 3, nameof(currency)).ToUpperInvariant();
        ObservedUtc = observedUtc.HasValue ? BankFeedText.Utc(observedUtc.Value) : null; ReferenceDate = referenceDate;
        LastCommittedTransactionIdentity = BankFeedText.Optional(lastCommittedTransactionIdentity, 256); CreatedUtc = BankFeedText.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CheckpointId { get; private set; } public Guid RawSourceObjectId { get; private set; }
    public string BalanceType { get; private set; } = null!; public decimal Amount { get; private set; } public string Currency { get; private set; } = null!;
    public DateTime? ObservedUtc { get; private set; } public DateOnly? ReferenceDate { get; private set; } public string? LastCommittedTransactionIdentity { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class BankFeedCursorObservation : ICompanyOwnedEntity
{
    private BankFeedCursorObservation() { }
    public BankFeedCursorObservation(Guid id, Guid companyId, Guid checkpointId, Guid synchronizationRunId,
        string phase, string cursorHash, int pageNumber, DateTime observedUtc)
    {
        Id = BankFeedText.Id(id, nameof(id)); CompanyId = BankFeedText.Id(companyId, nameof(companyId)); CheckpointId = BankFeedText.Id(checkpointId, nameof(checkpointId));
        SynchronizationRunId = BankFeedText.Id(synchronizationRunId, nameof(synchronizationRunId)); Phase = BankFeedText.Required(phase, 16, nameof(phase));
        CursorHash = BankFeedText.Hash(cursorHash, nameof(cursorHash)); PageNumber = pageNumber >= 0 ? pageNumber : throw new ArgumentOutOfRangeException(nameof(pageNumber));
        ObservedUtc = BankFeedText.Utc(observedUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CheckpointId { get; private set; }
    public Guid SynchronizationRunId { get; private set; } public string Phase { get; private set; } = null!; public string CursorHash { get; private set; } = null!;
    public int PageNumber { get; private set; } public DateTime ObservedUtc { get; private set; }
}

public sealed class BankFeedGap : ICompanyOwnedEntity
{
    private BankFeedGap() { }
    public BankFeedGap(Guid id, Guid companyId, Guid checkpointId, string kind, DateOnly dateFrom, DateOnly dateTo,
        string reasonCode, string summary, DateTime detectedUtc)
    {
        if (dateTo < dateFrom) throw new ArgumentOutOfRangeException(nameof(dateTo));
        Id = BankFeedText.Id(id, nameof(id)); CompanyId = BankFeedText.Id(companyId, nameof(companyId)); CheckpointId = BankFeedText.Id(checkpointId, nameof(checkpointId));
        Kind = BankFeedText.Required(kind, 32, nameof(kind)); DateFrom = dateFrom; DateTo = dateTo; Status = BankFeedGapStatuses.Open;
        ReasonCode = BankFeedText.Required(reasonCode, 96, nameof(reasonCode)); Summary = BankFeedText.Required(summary, 1000, nameof(summary)); DetectedUtc = BankFeedText.Utc(detectedUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CheckpointId { get; private set; }
    public string Kind { get; private set; } = null!; public DateOnly DateFrom { get; private set; } public DateOnly DateTo { get; private set; }
    public string Status { get; private set; } = null!; public string ReasonCode { get; private set; } = null!; public string Summary { get; private set; } = null!;
    public DateTime DetectedUtc { get; private set; } public DateTime? ResolvedUtc { get; private set; } public Guid? ResolvedByUserId { get; private set; }
    public void Resolve(Guid? actorUserId, DateTime nowUtc) { if (actorUserId == Guid.Empty) throw new ArgumentException("Actor id cannot be empty."); Status = BankFeedGapStatuses.Resolved; ResolvedByUserId = actorUserId; ResolvedUtc = BankFeedText.Utc(nowUtc); }
}

internal static class BankFeedText
{
    public static Guid Id(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string value, int max, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length > max ? throw new ArgumentOutOfRangeException(name) : value.Trim();
    public static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? throw new ArgumentOutOfRangeException(nameof(value)) : value.Trim();
    public static string Hash(string value, string name) { var normalized = Required(value, 64, name).ToLowerInvariant(); return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : throw new ArgumentException($"{name} must be a SHA-256 hash.", name); }
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
