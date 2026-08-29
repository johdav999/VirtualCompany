namespace VirtualCompany.Application.Finance;

public static class ConnectedBankingCapacityProfileKeys
{
    public const string Small = "small";
    public const string Medium = "medium";
}

public static class ConnectedBankingReadinessStatuses
{
    public const string Ready = "ready";
    public const string Attention = "attention";
    public const string Blocked = "blocked";
    public const string NotMeasured = "not_measured";
}

public static class ConnectedBankingReadinessCheckKeys
{
    public const string ConsentExpiry = "consent_expiry";
    public const string FeedGaps = "feed_gaps";
    public const string FeedLag = "feed_lag";
    public const string DuplicateIdentity = "duplicate_identity";
    public const string UnreconciledAging = "unreconciled_aging";
    public const string Suspense = "suspense";
    public const string StaleApprovals = "stale_approvals";
    public const string AmbiguousSubmissions = "ambiguous_submissions";
    public const string RejectedInstructions = "rejected_instructions";
    public const string UnsettledBatches = "unsettled_batches";
    public const string WorkerBacklog = "worker_backlog";
    public const string ControlAccountDifferences = "control_account_differences";
}

public static class ConnectedBankingCapacityResourceKeys
{
    public const string Connections = "bank_connections";
    public const string FeedAccounts = "feed_accounts";
    public const string FeedTransactions = "feed_transactions";
    public const string MatchingCandidates = "matching_candidates";
    public const string PaymentBatches = "payment_batches";
    public const string WebhookReceipts = "webhook_receipts";
    public const string OpenWorkerItems = "open_worker_items";
}

public sealed record ConnectedBankingSupportedVolumeDto(string Resource, long MaximumCount);

public sealed record ConnectedBankingCapacityProfileDto(
    string Key,
    string DisplayName,
    int ConcurrentUsers,
    int ConcurrentFeedWorkers,
    int ConcurrentPaymentWorkers,
    IReadOnlyList<ConnectedBankingSupportedVolumeDto> Volumes);

public sealed record ConnectedBankingServiceObjectiveDto(
    string Key,
    string DisplayName,
    string Unit,
    decimal Objective,
    decimal WarningThreshold,
    string MeasurementScope,
    string Remediation);

public sealed record ConnectedBankingVolumeMeasurementDto(
    string Resource,
    long CurrentCount,
    long SupportedCount,
    string Status);

public sealed record ConnectedBankingReadinessCheckDto(
    string Key,
    string Status,
    int Count,
    decimal? Value,
    string? Unit,
    decimal? Threshold,
    string Explanation,
    string OperatorAction,
    IReadOnlyList<Guid> SubjectIds);

public sealed record ConnectedBankingReadinessReadModel(
    Guid CompanyId,
    string Status,
    bool IsReady,
    string ProfileKey,
    DateTime EvaluatedUtc,
    IReadOnlyList<ConnectedBankingCapacityProfileDto> Profiles,
    IReadOnlyList<ConnectedBankingServiceObjectiveDto> Objectives,
    IReadOnlyList<ConnectedBankingVolumeMeasurementDto> Volumes,
    IReadOnlyList<ConnectedBankingReadinessCheckDto> Checks);

public sealed record GetConnectedBankingReadinessQuery(
    Guid CompanyId,
    string ProfileKey = ConnectedBankingCapacityProfileKeys.Small,
    DateTime? AsOfUtc = null);

public interface IConnectedBankingReadinessService
{
    Task<ConnectedBankingReadinessReadModel> GetAsync(
        GetConnectedBankingReadinessQuery query,
        CancellationToken cancellationToken);
}

public static class ConnectedBankingRecoveryReasonCodes
{
    public const string DuplicateFeedIdentity = "connected_banking_restore_duplicate_feed_identity";
    public const string DuplicateBankRowIdentity = "connected_banking_restore_duplicate_bank_row_identity";
    public const string DuplicatePaymentIdentity = "connected_banking_restore_duplicate_payment_identity";
    public const string DuplicateWebhookIdentity = "connected_banking_restore_duplicate_webhook_identity";
    public const string StatementObjectMissing = "connected_banking_restore_statement_object_missing";
    public const string StatementObjectHashMismatch = "connected_banking_restore_statement_object_hash_mismatch";
    public const string StatementObjectLengthMismatch = "connected_banking_restore_statement_object_length_mismatch";
}

public sealed record ConnectedBankingRecoveryIssueDto(
    string ReasonCode,
    string Explanation,
    string EntityType,
    string EntityId,
    bool IsBlocking);

public sealed record ConnectedBankingRecoveryVerificationDto(
    Guid CompanyId,
    bool ObjectContentVerified,
    int ConnectionCount,
    int FeedSourceObjectCount,
    int FeedTransactionCount,
    int StatementImportCount,
    int PaymentExecutionCount,
    int AcknowledgementCount,
    int WebhookReceiptCount,
    int SettlementCount,
    int ReconciliationResultCount,
    string EvidenceChecksum,
    bool IsValid,
    DateTime VerifiedUtc,
    IReadOnlyList<ConnectedBankingRecoveryIssueDto> Issues);

public sealed record VerifyConnectedBankingRecoveryCommand(
    Guid CompanyId,
    bool VerifyObjectContent,
    Guid ActorUserId,
    string? CorrelationId = null);

public interface IConnectedBankingRecoveryVerificationService
{
    Task<ConnectedBankingRecoveryVerificationDto> VerifyAsync(
        VerifyConnectedBankingRecoveryCommand command,
        CancellationToken cancellationToken);
}
