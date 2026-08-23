namespace VirtualCompany.Application.Finance;

public static class AccountingAuthorityReasonCodes
{
    public const string AuthorityNotConfigured = "accounting_authority_not_configured";
    public const string AuthorityPeriodNotFound = "accounting_authority_period_not_found";
    public const string PeriodBoundaryRequired = "accounting_authority_period_boundary_required";
    public const string NativePostingBlocked = "native_posting_blocked_by_authority";
    public const string ProviderPostingBlocked = "provider_posting_blocked_by_authority";
    public const string ExportBlocked = "provider_export_blocked_by_authority";
    public const string MigrationOperationRequired = "migration_operation_required";
    public const string ProviderRequired = "accounting_authority_provider_required";
    public const string ProviderNotConnected = "accounting_authority_provider_not_connected";
    public const string ConflictingActivity = "accounting_authority_conflicting_activity";
    public const string PreviewStale = "accounting_authority_preview_stale";
    public const string ConcurrencyConflict = "accounting_authority_concurrency_conflict";
    public const string CutoverIncomplete = "accounting_authority_cutover_incomplete";
    public const string ExportNotFound = "accounting_provider_export_not_found";
    public const string ReconciliationRequired = "accounting_provider_export_reconciliation_required";
    public const string SwitchEvidenceRequired = "accounting_provider_switch_evidence_required";
}

public sealed record AccountingAuthorityPeriodDto(
    Guid Id,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Authority,
    string AuthorityLabel,
    string? TargetAuthority,
    string? TargetAuthorityLabel,
    string? ProviderKey,
    string? ProviderName,
    string ChangeReason,
    bool OpeningBalancesReconciled,
    bool TrialBalanceReconciled,
    bool SourceMappingsReconciled,
    int ConflictCount,
    string? ValidationSummary,
    bool IsCutoverReady,
    long Version,
    DateTime UpdatedUtc,
    DateTime? CompletedUtc);

public sealed record AccountingAuthorityProviderDto(
    string ProviderKey,
    string DisplayName,
    bool IsConnected,
    string ConnectionStatus,
    DateTime? LastSuccessfulSyncUtc,
    IReadOnlyCollection<string> GrantedScopes,
    string ModeExplanation,
    string? SafeIssueSummary);

public sealed record AccountingProviderExportDto(
    Guid Id,
    Guid LedgerEntryId,
    string JournalNumber,
    DateOnly PostingDate,
    string SourceType,
    string SourceId,
    string SourceVersion,
    string ProviderKey,
    string ProviderName,
    string Status,
    string StatusLabel,
    Guid WriteRequestId,
    Guid? ApprovalRequestId,
    string? FailureCategory,
    string? SafeSummary,
    string? ProviderExternalId,
    int AttemptCount,
    long Version,
    DateTime UpdatedUtc);

public sealed record AccountingAuthorityReadModel(
    Guid CompanyId,
    AccountingAuthorityPeriodDto? CurrentPeriod,
    IReadOnlyList<AccountingAuthorityPeriodDto> Periods,
    IReadOnlyList<AccountingAuthorityProviderDto> Providers,
    IReadOnlyList<AccountingProviderExportDto> Exports,
    int PendingExportCount,
    int ReconciliationRequiredCount,
    string Explanation,
    bool CanChangeAuthority);

public sealed record AccountingAuthorityIssueDto(
    string ReasonCode,
    string Explanation,
    bool IsBlocking = true,
    Guid? SubjectId = null);

public sealed record AccountingAuthorityChangePreview(
    Guid CompanyId,
    string CurrentAuthority,
    string TargetAuthority,
    string? ProviderKey,
    Guid EffectiveFiscalPeriodId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    int PostedJournalCount,
    int PendingExportCount,
    int UnmappedSourceCount,
    string PreviewToken,
    long ExpectedCurrentVersion,
    bool IsAllowed,
    IReadOnlyList<AccountingAuthorityIssueDto> Issues,
    IReadOnlyList<AccountingAuthorityIssueDto> Warnings);

public sealed record AccountingAuthorityPolicyDecision(
    Guid CompanyId,
    DateOnly AccountingDate,
    string Operation,
    string Authority,
    string? ProviderKey,
    Guid? AuthorityPeriodId,
    bool IsAllowed,
    string? ReasonCode,
    string Explanation);

public sealed record GetAccountingAuthorityQuery(Guid CompanyId, DateOnly? AsOf = null, int ExportLimit = 50);
public sealed record EvaluateAccountingAuthorityQuery(
    Guid CompanyId,
    DateOnly AccountingDate,
    string Operation,
    string? ProviderKey = null);
public sealed record PreviewAccountingAuthorityChangeQuery(
    Guid CompanyId,
    Guid EffectiveFiscalPeriodId,
    string TargetAuthority,
    string? ProviderKey);
public sealed record StartAccountingAuthorityChangeCommand(
    Guid CompanyId,
    Guid EffectiveFiscalPeriodId,
    string TargetAuthority,
    string? ProviderKey,
    string Reason,
    string PreviewToken,
    long ExpectedCurrentVersion,
    Guid ActorUserId,
    string? CorrelationId = null);
public sealed record RecordAccountingCutoverValidationCommand(
    Guid CompanyId,
    Guid AuthorityPeriodId,
    bool OpeningBalancesReconciled,
    bool TrialBalanceReconciled,
    bool SourceMappingsReconciled,
    int ConflictCount,
    string Summary,
    long ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);
public sealed record CompleteAccountingAuthorityCutoverCommand(
    Guid CompanyId,
    Guid AuthorityPeriodId,
    long ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public interface IAccountingAuthorityPolicy
{
    Task<AccountingAuthorityPolicyDecision> EvaluateAsync(
        EvaluateAccountingAuthorityQuery query,
        CancellationToken cancellationToken);
}

public interface IAccountingAuthorityService : IAccountingAuthorityPolicy
{
    Task<AccountingAuthorityReadModel> GetAsync(GetAccountingAuthorityQuery query, CancellationToken cancellationToken);
    Task<AccountingAuthorityChangePreview> PreviewChangeAsync(
        PreviewAccountingAuthorityChangeQuery query,
        CancellationToken cancellationToken);
    Task<AccountingAuthorityReadModel> StartChangeAsync(
        StartAccountingAuthorityChangeCommand command,
        CancellationToken cancellationToken);
    Task<AccountingAuthorityReadModel> RecordCutoverValidationAsync(
        RecordAccountingCutoverValidationCommand command,
        CancellationToken cancellationToken);
    Task<AccountingAuthorityReadModel> CompleteCutoverAsync(
        CompleteAccountingAuthorityCutoverCommand command,
        CancellationToken cancellationToken);
}

public sealed record AccountingProviderExportEnvelope(
    Guid CompanyId,
    Guid AuthorityPeriodId,
    Guid LedgerEntryId,
    string JournalNumber,
    DateOnly PostingDate,
    string Description,
    string SourceType,
    string SourceId,
    string SourceVersion,
    string Currency,
    IReadOnlyList<AccountingProviderExportLine> Lines);

public sealed record AccountingProviderExportLine(
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    string Currency,
    string? Description);

public sealed record AccountingProviderCommand(
    string ProviderKey,
    string CommandType,
    string HttpMethod,
    string Path,
    string TargetLabel,
    string PayloadSummary,
    string PayloadHash,
    string SanitizedPayloadJson,
    string ProviderPayloadType);

public interface IAccountingProviderExportAdapter
{
    string ProviderKey { get; }
    AccountingProviderCommand Map(AccountingProviderExportEnvelope export);
}

public sealed record QueueAccountingProviderExportCommand(
    Guid CompanyId,
    Guid LedgerEntryId,
    string ProviderKey,
    Guid ActorUserId,
    string? CorrelationId = null);
public sealed record ReconcileAccountingProviderExportCommand(
    Guid CompanyId,
    Guid ExportId,
    bool ProviderConfirmedSuccess,
    string? ProviderExternalId,
    string Summary,
    long ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public interface IAccountingProviderExportService
{
    Task<AccountingProviderExportDto> QueueAsync(
        QueueAccountingProviderExportCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderExportDto> ReconcileAsync(
        ReconcileAccountingProviderExportCommand command,
        CancellationToken cancellationToken);
}

public interface IAccountingProviderExportExecutionTracker
{
    Task EnsureExecutionAllowedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken);
    Task MarkExecutionStartedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken);
    Task MarkExecutionSucceededAsync(
        Guid companyId,
        Guid writeRequestId,
        string? providerExternalId,
        string summary,
        CancellationToken cancellationToken);
    Task MarkExecutionFailedAsync(
        Guid companyId,
        Guid writeRequestId,
        Exception exception,
        bool providerAcceptedRequest,
        CancellationToken cancellationToken);
}

public static class FinanceAccountingActionStatuses
{
    public const string FinanceReviewRequired = "finance_review_required";
    public const string AwaitingApproval = "awaiting_approval";
}

public sealed record RequestFinanceDocumentActionCommand(
    Guid CompanyId,
    string SourceType,
    string SourceId,
    string SourceVersion,
    string DocumentType,
    DateOnly AccountingDate,
    string CounterpartyName,
    string Description,
    decimal Amount,
    string Currency,
    string? ContactReference,
    Guid WriteRequestId,
    Guid? ActorUserId,
    string? CorrelationId = null);

public sealed record FinanceDocumentActionResult(
    string Authority,
    string DestinationKey,
    string DestinationName,
    string Status,
    Guid WriteRequestId,
    Guid? ApprovalId,
    string Message);

public sealed record FinanceDocumentProviderCommand(
    string ProviderKey,
    string CommandType,
    string HttpMethod,
    string Path,
    string TargetLabel,
    string PayloadSummary,
    string PayloadHash,
    string SanitizedPayloadJson,
    string ProviderPayloadType);

public sealed record RequestFinanceCustomerDocumentExportCommand(
    Guid CompanyId,
    Guid DocumentId,
    DateOnly AccountingDate,
    Guid WriteRequestId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string? CorrelationId = null);

public sealed record FinanceCustomerDocumentProviderResult(
    Guid WriteRequestId,
    Guid? ApprovalId,
    string Status,
    string Message);

public interface IFinanceDocumentActionProviderAdapter
{
    string ProviderKey { get; }
    FinanceDocumentProviderCommand Map(RequestFinanceDocumentActionCommand command);
}

public interface IFinanceCustomerDocumentProviderAdapter
{
    string ProviderKey { get; }
    Task<FinanceCustomerDocumentProviderResult> RequestExportAsync(
        RequestFinanceCustomerDocumentExportCommand command,
        CancellationToken cancellationToken);
}

public interface IFinanceAccountingActionService
{
    Task<FinanceDocumentActionResult> RequestDocumentAsync(
        RequestFinanceDocumentActionCommand command,
        CancellationToken cancellationToken);

    Task<FinanceDocumentActionResult> RequestCustomerDocumentExportAsync(
        RequestFinanceCustomerDocumentExportCommand command,
        CancellationToken cancellationToken);

    Task<FinanceIntegrationOutboundExecutionResult> RetryApprovedAsync(
        Guid companyId,
        Guid writeRequestId,
        CancellationToken cancellationToken);
}

public sealed class AccountingAuthorityException : Exception
{
    public AccountingAuthorityException(string reasonCode, string message, bool isConflict = false)
        : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? throw new ArgumentException("ReasonCode is required.", nameof(reasonCode))
            : reasonCode.Trim();
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
