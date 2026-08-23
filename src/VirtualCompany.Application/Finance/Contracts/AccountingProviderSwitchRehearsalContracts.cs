namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchReconciliationCheckKeys
{
    public const string DebitCreditEquality = "debit_credit_equality";
    public const string TrialBalanceByAccountAndCurrency = "trial_balance_by_account_and_currency";
    public const string ReceivableOpenItems = "receivable_open_items";
    public const string PayableOpenItems = "payable_open_items";
    public const string TaxControlDetail = "tax_control_detail";
    public const string BankReconciliation = "bank_reconciliation";
    public const string OpeningEquity = "opening_equity";
    public const string SourceDispositionCompleteness = "source_disposition_completeness";
    public const string DuplicateIdentities = "duplicate_identities";
    public const string UnresolvedProviderOutcomes = "unresolved_provider_outcomes";
    public const string EvidenceCoverage = "evidence_coverage";
    public const string SourceSnapshotFreshness = "source_snapshot_freshness";
}

public static class AccountingProviderSwitchRehearsalReasonCodes
{
    public const string NotReady = "accounting_provider_switch_rehearsal_not_ready";
    public const string NotFound = "accounting_provider_switch_rehearsal_not_found";
    public const string Stale = "accounting_provider_switch_rehearsal_stale";
    public const string BlockingCheck = "accounting_provider_switch_reconciliation_failed";
    public const string BlockingGap = "accounting_provider_switch_blocking_gap";
    public const string InvalidEvidence = "accounting_provider_switch_manual_evidence_invalid";
    public const string PlanNotReady = "accounting_provider_switch_plan_not_ready";
    public const string PlanStale = "accounting_provider_switch_plan_stale";
    public const string PlanApprovalPending = "accounting_provider_switch_plan_approval_pending";
}

public sealed record StartAccountingProviderSwitchRehearsalCommand(Guid CompanyId, Guid SwitchId,
    long ExpectedSwitchVersion, Guid ActorUserId, string CorrelationId, string IdempotencyKey);
public sealed record ReplayAccountingProviderSwitchRehearsalCommand(Guid CompanyId, Guid SwitchId,
    Guid RehearsalId, long ExpectedSwitchVersion, Guid ActorUserId, string CorrelationId, string IdempotencyKey);
public sealed record GetAccountingProviderSwitchRehearsalQuery(Guid CompanyId, Guid SwitchId, Guid? RehearsalId = null);
public sealed record RecordAccountingProviderSwitchManualEvidenceCommand(Guid CompanyId, Guid SwitchId,
    Guid RehearsalId, Guid CheckId, string Explanation, string EvidenceReference, DateTime? ExpiresUtc,
    Guid ActorUserId, string CorrelationId);
public sealed record GenerateAccountingProviderSwitchCutoverPlanCommand(Guid CompanyId, Guid SwitchId,
    Guid RehearsalId, long ExpectedSwitchVersion, DateTime FreezeStartsUtc, DateTime FreezeEndsUtc,
    string RecoveryBoundary, IReadOnlyList<Guid> ParticipantUserIds, Guid ActorUserId, string CorrelationId);
public sealed record RequestAccountingProviderSwitchPlanApprovalCommand(Guid CompanyId, Guid SwitchId,
    Guid PlanId, long ExpectedSwitchVersion, Guid ActorUserId, string CorrelationId);
public sealed record GetAccountingProviderSwitchPlanReadinessQuery(Guid CompanyId, Guid SwitchId, Guid? PlanId = null);

public sealed record AccountingProviderSwitchRehearsalInputDto(Guid Id, long SwitchVersion, string Strategy,
    string SourceSnapshotHash, string StagingHash, string MappingHash, string GapHash, long StagedRecordCount,
    decimal FinancialTotal, string DatasetSummaryJson, DateTime CreatedUtc);
public sealed record AccountingProviderSwitchRehearsalDatasetResultDto(Guid Id, string Dataset,
    long ExpectedCount, long ObservedCount, decimal ExpectedTotal, decimal ObservedTotal, string? Currency,
    string Result, string ReasonCode, string EvidenceJson, DateTime CalculatedUtc);
public sealed record AccountingProviderSwitchReconciliationCheckDto(Guid Id, string CheckKey,
    string ExpectedValue, string ObservedValue, decimal Tolerance, string? Currency, string Result,
    string ReasonCode, string DataSourcesJson, string CalculationVersion, bool ManualEvidenceAllowed,
    bool HasCurrentManualEvidence, DateTime CalculatedUtc);
public sealed record AccountingProviderSwitchManualEvidenceDto(Guid Id, Guid CheckId, string Explanation,
    string EvidenceReference, Guid RecordedByUserId, DateTime RecordedUtc, DateTime? ExpiresUtc);
public sealed record AccountingProviderSwitchRehearsalDto(Guid Id, Guid CompanyId, Guid SwitchId, string Status,
    string? SimulationKind, bool ProviderAcceptanceProven, string? Disclosure, int CompletedWorkItems,
    int TotalWorkItems, int ProgressPercent, int AttemptCount, DateTime? NextAttemptUtc, string? FailureCode,
    string? FailureSummary, DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, long Version,
    AccountingProviderSwitchRehearsalInputDto? Input,
    IReadOnlyList<AccountingProviderSwitchRehearsalDatasetResultDto> Datasets,
    IReadOnlyList<AccountingProviderSwitchReconciliationCheckDto> Checks,
    IReadOnlyList<AccountingProviderSwitchManualEvidenceDto> ManualEvidence,
    bool IsReadyForPlan, string ReadinessExplanation);
public sealed record AccountingProviderSwitchRehearsalProgressDto(Guid RehearsalId, string Status,
    int CompletedWorkItems, int TotalWorkItems, int ProgressPercent, int AttemptCount,
    DateTime? NextAttemptUtc, string? FailureCode, string? FailureSummary, bool IsReadyForPlan,
    string ReadinessExplanation);

public sealed record AccountingProviderSwitchCutoverPlanDto(Guid Id, Guid CompanyId, Guid SwitchId,
    Guid RehearsalId, int PlanVersion, string PlanHash, string SourceSnapshotHash, string Strategy,
    DateTime FreezeStartsUtc, DateTime FreezeEndsUtc, string RecoveryBoundary, string ParticipantsJson,
    string SnapshotJson, Guid GeneratedByUserId, DateTime GeneratedUtc, Guid? ApprovalRequestId,
    string? ApprovalStatus, bool IsCurrent, bool IsApprovedAndCurrent);
public sealed record AccountingProviderSwitchPlanReadinessDto(Guid SwitchId,
    AccountingProviderSwitchCutoverPlanDto? Plan, bool IsReady, string? BlockingReasonCode,
    string Explanation);

public sealed record AccountingProviderSwitchRehearsalTargetRequest(Guid CompanyId, Guid SwitchId,
    string TargetKind, string? TargetProviderKey, string InputHash, IReadOnlyList<RehearsalStagedRecord> Records,
    string CorrelationId);
public sealed record RehearsalStagedRecord(Guid Id, string Dataset, string SourceIdentity, string SourceVersion,
    string SourceHash, string NormalizedHash, string NormalizedDataJson, string EvidenceJson,
    decimal FinancialAmount, string? Currency, string Disposition);
public sealed record AccountingProviderSwitchRehearsalTargetResult(bool IsSupported, bool ProviderAcceptanceProven,
    string SimulationKind, string Disclosure, IReadOnlyDictionary<Guid, string> RecordOutcomes);

public interface IAccountingProviderSwitchRehearsalAdapter
{
    bool CanHandle(string targetKind, string? targetProviderKey);
    Task<AccountingProviderSwitchRehearsalTargetResult> PreviewAsync(
        AccountingProviderSwitchRehearsalTargetRequest request, CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchRehearsalService
{
    Task<AccountingProviderSwitchRehearsalDto> StartAsync(StartAccountingProviderSwitchRehearsalCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchRehearsalDto> ReplayAsync(ReplayAccountingProviderSwitchRehearsalCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchRehearsalDto> GetAsync(GetAccountingProviderSwitchRehearsalQuery query,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchManualEvidenceDto> RecordManualEvidenceAsync(
        RecordAccountingProviderSwitchManualEvidenceCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverPlanDto> GeneratePlanAsync(
        GenerateAccountingProviderSwitchCutoverPlanCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverPlanDto> RequestPlanApprovalAsync(
        RequestAccountingProviderSwitchPlanApprovalCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchPlanReadinessDto> GetPlanReadinessAsync(
        GetAccountingProviderSwitchPlanReadinessQuery query, CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchRehearsalJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}
