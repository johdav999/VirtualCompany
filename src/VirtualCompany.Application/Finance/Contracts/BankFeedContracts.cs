namespace VirtualCompany.Application.Finance;

public static class BankFeedProviderTransactionStatuses
{
    public const string Booked = "booked";
    public const string Pending = "pending";
}

public sealed record BankFeedProviderTransaction(
    string StableIdentity,
    string Status,
    DateTime? BookingDateUtc,
    DateTime? ValueDateUtc,
    DateTime TransactionDateUtc,
    decimal Amount,
    string Currency,
    string ReferenceText,
    string Counterparty,
    string? ProviderTransactionReference);

public sealed record BankFeedProviderPageRequest(
    string ProviderAccountAccessReference,
    DateOnly DateFrom,
    DateOnly DateTo,
    string TransactionStatus,
    string? ContinuationToken);

public sealed record BankFeedProviderPage(
    IReadOnlyList<BankFeedProviderTransaction> Transactions,
    string? NextContinuationToken,
    ReadOnlyMemory<byte> SourceEvidence,
    string ContentType,
    string? ProviderRequestId);

public sealed record BankFeedProviderBalance(
    string BalanceType,
    decimal Amount,
    string Currency,
    DateTime? ObservedUtc,
    DateOnly? ReferenceDate,
    string? LastCommittedTransactionIdentity);

public sealed record BankFeedProviderBalances(
    IReadOnlyList<BankFeedProviderBalance> Balances,
    ReadOnlyMemory<byte> SourceEvidence,
    string ContentType,
    string? ProviderRequestId);

public interface IBankFeedProvider
{
    string ProviderKey { get; }
    Task<BankFeedProviderBalances> GetBalancesAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, string providerAccountAccessReference,
        CancellationToken cancellationToken);
    Task<BankFeedProviderPage> GetTransactionsAsync(Guid companyId, string providerConsentId,
        BankProviderCredentialBundle credentials, BankFeedProviderPageRequest request,
        CancellationToken cancellationToken);
}

public interface IBankFeedProviderRegistry
{
    IBankFeedProvider GetRequired(string providerKey);
}

public sealed record RequestBankFeedSynchronizationCommand(
    Guid CompanyId,
    Guid? CheckpointId,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record RequestBankFeedBackfillCommand(
    Guid CompanyId,
    Guid CheckpointId,
    Guid GapId,
    DateOnly DateFrom,
    DateOnly DateTo,
    Guid ActorUserId,
    long ExpectedCheckpointVersion,
    string Reason,
    string? CorrelationId = null);

public sealed record BankFeedRequestResult(int QueuedAccountCount, string Status, string Explanation);

public sealed record BankFeedHealthResult(
    int HealthyCount,
    int AttentionCount,
    DateTime? LatestSuccessfulCoverageUtc,
    int MaximumLagMinutes,
    IReadOnlyList<BankFeedAccountHealthItem> Accounts);

public sealed record BankFeedAccountHealthItem(
    Guid CheckpointId,
    Guid ConnectionId,
    Guid DiscoveredAccountId,
    Guid CompanyBankAccountId,
    string InstitutionName,
    string AccountName,
    string MaskedAccountNumber,
    string Currency,
    string Status,
    string? ReasonCode,
    string? FailureSummary,
    DateOnly? CoverageFrom,
    DateOnly? CoverageThrough,
    DateTime? LastSuccessfulSyncUtc,
    DateTime? LastAttemptUtc,
    DateTime? NextAttemptUtc,
    int LagMinutes,
    long Version,
    IReadOnlyList<BankFeedGapItem> Gaps);

public sealed record BankFeedGapItem(
    Guid Id,
    string Kind,
    DateOnly DateFrom,
    DateOnly DateTo,
    string Status,
    string ReasonCode,
    string Summary,
    DateTime DetectedUtc,
    DateTime? ResolvedUtc);

public interface IBankFeedService
{
    Task<BankFeedHealthResult> GetHealthAsync(Guid companyId, CancellationToken cancellationToken);
    Task<BankFeedRequestResult> RequestSynchronizationAsync(RequestBankFeedSynchronizationCommand command,
        CancellationToken cancellationToken);
    Task<BankFeedRequestResult> RequestBackfillAsync(RequestBankFeedBackfillCommand command,
        CancellationToken cancellationToken);
}

public interface IBankFeedSynchronizationRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}
