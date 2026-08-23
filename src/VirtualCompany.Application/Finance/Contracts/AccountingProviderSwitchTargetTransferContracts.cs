namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchTargetTransferReasonCodes
{
    public const string TargetMustBeExternal = "target_transfer_target_must_be_external";
    public const string AdapterUnavailable = "target_transfer_adapter_unavailable";
    public const string ConnectionMissing = "target_transfer_connection_missing";
    public const string ScopeMissing = "target_transfer_scope_missing";
    public const string CapabilityUnsupported = "target_transfer_capability_unsupported";
    public const string PlanNotApproved = "target_transfer_plan_not_approved";
    public const string PlanStale = "target_transfer_plan_stale";
    public const string StagingIncomplete = "target_transfer_staging_incomplete";
    public const string MappingStale = "target_transfer_mapping_stale";
    public const string ApprovalStale = "target_transfer_approval_stale";
    public const string BatchNotFound = "target_transfer_batch_not_found";
    public const string BatchNotReplayable = "target_transfer_batch_not_replayable";
    public const string ReconciliationRequired = "target_transfer_reconciliation_required";
    public const string ConcurrencyConflict = "target_transfer_concurrency_conflict";
    public const string Failed = "target_transfer_failed";
}

public static class AccountingProviderSwitchTargetOperationModes
{
    public const string PreviewOnly = "preview_only";
    public const string PreparatoryNonPosting = "preparatory_non_posting";
    public const string FinalAuthoritative = "final_authoritative";
}

public sealed record AccountingProviderSwitchTargetRecord(
    Guid StagedRecordId,
    string Dataset,
    string SourceIdentity,
    string SourceVersion,
    string SourceHash,
    string NormalizedHash,
    string NormalizedDataJson,
    string EvidenceJson,
    decimal FinancialAmount,
    string? Currency,
    string Disposition,
    int? MappingVersion);

public sealed record AccountingProviderSwitchTargetOperation(
    bool IsSupported,
    string Dataset,
    string OperationMode,
    string Action,
    string Explanation,
    IReadOnlyList<string> RequiredScopes,
    AccountingProviderCommand? ProviderCommand);

public sealed record AccountingProviderSwitchTargetMappingRequest(
    Guid CompanyId,
    Guid SwitchId,
    Guid PlanId,
    int PlanVersion,
    string PlanHash,
    string TargetProviderKey,
    AccountingProviderSwitchTargetRecord Record,
    string StableIdentity,
    string CorrelationId);

public interface IAccountingProviderSwitchTargetPreparationAdapter
{
    string ProviderKey { get; }
    AccountingProviderSwitchTargetOperation Map(AccountingProviderSwitchTargetMappingRequest request);
}

public sealed record StartAccountingProviderSwitchTargetTransferCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid PlanId,
    long ExpectedSwitchVersion,
    Guid ActorUserId,
    string IdempotencyKey,
    string CorrelationId);

public sealed record ReplayAccountingProviderSwitchTargetTransferCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid BatchId,
    long ExpectedBatchVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record GetAccountingProviderSwitchTargetTransferQuery(
    Guid CompanyId,
    Guid SwitchId,
    Guid? BatchId = null);

public sealed record ReconcileAccountingProviderSwitchTargetTransferItemCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid BatchId,
    Guid ItemId,
    bool ProviderConfirmedSuccess,
    string? ProviderExternalId,
    string Summary,
    long ExpectedItemVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record AccountingProviderSwitchTargetTransferAttemptDto(
    Guid Id,
    int AttemptNumber,
    string Outcome,
    string? FailureCategory,
    string? SafeSummary,
    bool ProviderAcceptedRequest,
    DateTime StartedUtc,
    DateTime? CompletedUtc);

public sealed record AccountingProviderSwitchTargetTransferItemDto(
    Guid Id,
    Guid StagedRecordId,
    string Dataset,
    string SourceIdentity,
    string SourceVersion,
    int? MappingVersion,
    string OperationMode,
    string Action,
    string StableIdentity,
    string Status,
    Guid? WriteRequestId,
    Guid? ApprovalRequestId,
    string? ProviderExternalId,
    string? FailureCategory,
    string? SafeSummary,
    bool ReconciliationNeeded,
    long Version,
    IReadOnlyList<AccountingProviderSwitchTargetTransferAttemptDto> Attempts);

public sealed record AccountingProviderSwitchTargetTransferBatchDto(
    Guid Id,
    Guid CompanyId,
    Guid SwitchId,
    Guid PlanId,
    int PlanVersion,
    string PlanHash,
    string TargetProviderKey,
    string PackageHash,
    string Status,
    int TotalItemCount,
    int PreviewItemCount,
    int PreparatoryItemCount,
    int FinalItemCount,
    int CompletedItemCount,
    int FailedItemCount,
    int ReconciliationItemCount,
    string? FailureCode,
    string? FailureSummary,
    DateTime RequestedUtc,
    DateTime? CompletedUtc,
    long Version,
    bool IsReadyForCutover,
    string ReadinessExplanation,
    IReadOnlyList<AccountingProviderSwitchTargetTransferItemDto> Items);

public interface IAccountingProviderSwitchTargetTransferService
{
    Task<AccountingProviderSwitchTargetTransferBatchDto> StartAsync(
        StartAccountingProviderSwitchTargetTransferCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchTargetTransferBatchDto> ReplayAsync(
        ReplayAccountingProviderSwitchTargetTransferCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchTargetTransferBatchDto> GetAsync(
        GetAccountingProviderSwitchTargetTransferQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchTargetTransferItemDto> ReconcileAsync(
        ReconcileAccountingProviderSwitchTargetTransferItemCommand command, CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchTargetTransferJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchTargetTransferExecutionTracker
{
    Task EnsureExecutionAllowedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken);
    Task MarkExecutionStartedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken);
    Task MarkExecutionSucceededAsync(Guid companyId, Guid writeRequestId, string? providerExternalId,
        string summary, CancellationToken cancellationToken);
    Task MarkExecutionFailedAsync(Guid companyId, Guid writeRequestId, Exception exception,
        bool providerAcceptedRequest, CancellationToken cancellationToken);
}
