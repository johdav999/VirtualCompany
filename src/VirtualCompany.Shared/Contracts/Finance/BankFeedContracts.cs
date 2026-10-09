
namespace VirtualCompany.Application.Finance;

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


public sealed record BankFeedRequestResult(int QueuedAccountCount, string Status, string Explanation);
