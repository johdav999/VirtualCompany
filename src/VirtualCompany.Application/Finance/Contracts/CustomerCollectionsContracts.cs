namespace VirtualCompany.Application.Finance;

public static class CustomerCollectionReasonCodes
{
    public const string NotFound = "customer_collection_not_found";
    public const string CustomerNotFound = "customer_collection_customer_not_found";
    public const string InvoiceNotFound = "customer_collection_invoice_not_found";
    public const string PolicyMissing = "customer_collection_policy_missing";
    public const string StaleVersion = "customer_collection_stale_version";
    public const string StaleEvidence = "customer_collection_stale_evidence";
    public const string NoOpenBalance = "customer_collection_no_open_balance";
    public const string InvoiceNotOverdue = "customer_collection_invoice_not_overdue";
    public const string CollectionOnHold = "customer_collection_on_hold";
    public const string DisputeOpen = "customer_collection_dispute_open";
    public const string RecipientMissing = "customer_collection_recipient_missing";
    public const string ApprovalRequired = "customer_collection_approval_required";
    public const string IdempotencyConflict = "customer_collection_idempotency_conflict";
    public const string DeliveryAmbiguous = "customer_collection_delivery_ambiguous";
    public const string UnsupportedCurrency = "customer_collection_unsupported_currency";
    public const string UnsupportedCharges = "customer_collection_unsupported_charges";
}

public sealed record CustomerAgingQuery(
    Guid CompanyId,
    DateOnly CutoffDate,
    string TimeZoneId,
    Guid? CustomerId = null,
    string? Currency = null,
    int Skip = 0,
    int Take = 100);

public sealed record CustomerAgingItemDto(
    Guid InvoiceId,
    Guid CustomerId,
    string InvoiceNumber,
    string CustomerName,
    DateOnly IssuedDate,
    DateOnly DueDate,
    int DaysOverdue,
    string AgingBucket,
    string Currency,
    decimal OriginalAmount,
    decimal AllocatedAmount,
    decimal OpenAmount,
    bool IsDisputed,
    bool IsOnHold,
    string? PromiseStatus,
    DateOnly? PromiseDueDate,
    int ReminderStage,
    decimal? CreditLimit,
    decimal CustomerExposure,
    string RecommendedAction,
    IReadOnlyList<string> EvidenceCitations,
    decimal? FunctionalOriginalAmount = null,
    decimal? FunctionalAllocatedAmount = null,
    decimal? FunctionalOpenAmount = null,
    string? FunctionalCurrency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null,
    string? ExchangeRateIdentity = null);

public sealed record CustomerAgingResultDto(
    Guid CompanyId,
    DateOnly CutoffDate,
    DateTime CutoffExclusiveUtc,
    string TimeZoneId,
    string Currency,
    int TotalCount,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal DaysOver90,
    decimal TotalOpen,
    decimal ControlAccountDifference,
    bool IsControlAccountReconciled,
    IReadOnlyList<CustomerAgingItemDto> Items,
    string? FunctionalCurrency = null,
    decimal? FunctionalCurrent = null,
    decimal? FunctionalDays1To30 = null,
    decimal? FunctionalDays31To60 = null,
    decimal? FunctionalDays61To90 = null,
    decimal? FunctionalDaysOver90 = null,
    decimal? FunctionalTotalOpen = null);

public sealed record GenerateCustomerStatementCommand(
    Guid CompanyId,
    Guid CustomerId,
    DateOnly FromDate,
    DateOnly CutoffDate,
    string TimeZoneId,
    string Locale,
    string Currency,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record CustomerStatementItemDto(
    Guid Id,
    string ItemType,
    Guid? InvoiceId,
    Guid? PaymentAllocationId,
    DateOnly EffectiveDate,
    string Reference,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal RunningBalance,
    string SourceHash,
    decimal? FunctionalDebitAmount = null,
    decimal? FunctionalCreditAmount = null,
    decimal? FunctionalRunningBalance = null,
    string? FunctionalCurrency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null,
    string? ExchangeRateIdentity = null,
    string? CurrencyProvenance = null);

public sealed record CustomerStatementDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    DateOnly FromDate,
    DateOnly CutoffDate,
    string TimeZoneId,
    string Locale,
    string Currency,
    decimal OpeningBalance,
    decimal InvoiceActivity,
    decimal AllocationActivity,
    decimal CreditActivity,
    decimal ClosingBalance,
    string Checksum,
    string SourceManifestHash,
    string MediaType,
    string FileName,
    string ContentHash,
    long ContentLength,
    DateTime CreatedUtc,
    IReadOnlyList<CustomerStatementItemDto> Items,
    bool IsIdempotentReplay = false,
    string? FunctionalCurrency = null,
    decimal? FunctionalOpeningBalance = null,
    decimal? FunctionalInvoiceActivity = null,
    decimal? FunctionalAllocationActivity = null,
    decimal? FunctionalCreditActivity = null,
    decimal? FunctionalClosingBalance = null,
    string FunctionalEvidenceStatus = "legacy_unavailable");

public sealed record GetCustomerStatementQuery(Guid CompanyId, Guid StatementId);
public sealed record ListCustomerStatementsQuery(Guid CompanyId, Guid? CustomerId = null, int Skip = 0, int Take = 100);
public sealed record CustomerStatementListResult(int TotalCount, IReadOnlyList<CustomerStatementDto> Items);

public sealed record CustomerCollectionPolicyStageInput(
    int Stage,
    int DaysAfterDue,
    string Channel,
    string TemplateKey,
    bool RequiresApproval);

public sealed record CustomerCollectionPolicyExceptionInput(Guid CustomerId, string Reason, DateOnly? ExcludedUntilDate = null);

public sealed record UpsertCustomerCollectionPolicyCommand(
    Guid CompanyId,
    long? ExpectedVersion,
    int GracePeriodDays,
    decimal MaterialityThreshold,
    string DefaultLocale,
    bool RequireApproval,
    bool FeesEnabled,
    bool InterestEnabled,
    IReadOnlyList<CustomerCollectionPolicyStageInput> Stages,
    Guid ActorUserId,
    string? CorrelationId = null,
    IReadOnlyList<CustomerCollectionPolicyExceptionInput>? CustomerExceptions = null);

public sealed record CustomerCollectionPolicyStageDto(int Stage, int DaysAfterDue, string Channel, string TemplateKey, bool RequiresApproval);
public sealed record CustomerCollectionPolicyExceptionDto(Guid CustomerId, string Reason, DateOnly? ExcludedUntilDate);
public sealed record CustomerCollectionPolicyDto(
    Guid Id,
    int GracePeriodDays,
    decimal MaterialityThreshold,
    string DefaultLocale,
    bool RequireApproval,
    bool FeesEnabled,
    bool InterestEnabled,
    long Version,
    DateTime UpdatedUtc,
    IReadOnlyList<CustomerCollectionPolicyStageDto> Stages,
    IReadOnlyList<CustomerCollectionPolicyExceptionDto> CustomerExceptions);

public sealed record CustomerCollectionCaseDto(
    Guid Id,
    Guid CustomerId,
    Guid InvoiceId,
    string Status,
    int ReminderStage,
    bool IsOnHold,
    string? HoldReason,
    string? DisputeStatus,
    string? DisputeReason,
    decimal? DisputedAmount,
    string? PromiseStatus,
    decimal? PromiseAmount,
    DateOnly? PromiseDueDate,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    Guid? WorkTaskId,
    long Version,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record ListCustomerCollectionCasesQuery(
    Guid CompanyId,
    Guid? CustomerId = null,
    Guid? InvoiceId = null,
    string? Status = null,
    int Skip = 0,
    int Take = 100);
public sealed record CustomerCollectionCaseListResult(int TotalCount, IReadOnlyList<CustomerCollectionCaseDto> Items);

public sealed record RecordCustomerDisputeCommand(
    Guid CompanyId,
    Guid InvoiceId,
    decimal Amount,
    string Reason,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    long? ExpectedVersion,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record ResolveCustomerDisputeCommand(
    Guid CompanyId,
    Guid CaseId,
    long ExpectedVersion,
    string Resolution,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record RecordPromiseToPayCommand(
    Guid CompanyId,
    Guid InvoiceId,
    decimal Amount,
    DateOnly DueDate,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    long? ExpectedVersion,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record ResolvePromiseToPayCommand(
    Guid CompanyId,
    Guid CaseId,
    long ExpectedVersion,
    bool Kept,
    string Resolution,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record RecordCustomerCollectionResponseCommand(
    Guid CompanyId,
    Guid CaseId,
    long ExpectedVersion,
    string ResponseType,
    string Summary,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record PrepareCustomerReminderCommand(
    Guid CompanyId,
    Guid InvoiceId,
    int? RequestedStage,
    Guid? StatementId,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record SendCustomerReminderCommand(
    Guid CompanyId,
    Guid ReminderDraftId,
    long ExpectedDraftVersion,
    string ExpectedSourceHash,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record CustomerReminderDraftDto(
    Guid Id,
    Guid CaseId,
    Guid InvoiceId,
    Guid CustomerId,
    Guid? StatementId,
    int Stage,
    string RecipientEmail,
    string Subject,
    string Body,
    decimal PreparedOpenAmount,
    string Currency,
    string SourceHash,
    string Status,
    Guid? ApprovalRequestId,
    long Version,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    bool IsIdempotentReplay = false);

public sealed record CustomerReminderDeliveryDto(
    Guid Id,
    Guid ReminderDraftId,
    string Status,
    int Attempts,
    string? ProviderReference,
    string? FailureCode,
    string? FailureSummary,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? AcceptedUtc,
    bool IsIdempotentReplay = false);

public sealed record CollectionMetricsQuery(Guid CompanyId, DateOnly AsOfDate, int LookbackDays = 90, string? Currency = null);
public sealed record CustomerCollectionMetricsDto(
    DateOnly AsOfDate,
    string Currency,
    decimal OverdueValue,
    decimal OpenReceivables,
    decimal CreditSalesInWindow,
    int LookbackDays,
    decimal DsoNumerator,
    decimal DsoDenominator,
    decimal? DaysSalesOutstanding,
    int RemindersAccepted,
    int ReminderPayments,
    decimal? ReminderToPaymentConversion,
    int PromisesKept,
    int PromisesBroken,
    decimal AverageDisputeAgeDays,
    int ManualOverrides,
    int CommunicationFailures);

public sealed record RunCustomerCollectionWorkerCommand(DateTime AsOfUtc, int BatchSize = 100, Guid? CompanyId = null, bool ResetBlockedLease = false);
public sealed record CustomerCollectionWorkerResult(int Examined, int CasesCreated, int DraftsPrepared, int TasksCreated, int PromisesMarkedBroken);

public interface ICustomerCollectionsService
{
    Task<CustomerAgingResultDto> GetAgingAsync(CustomerAgingQuery query, CancellationToken cancellationToken);
    Task<CustomerStatementDto> GenerateStatementAsync(GenerateCustomerStatementCommand command, CancellationToken cancellationToken);
    Task<CustomerStatementDto> GetStatementAsync(GetCustomerStatementQuery query, CancellationToken cancellationToken);
    Task<CustomerStatementListResult> ListStatementsAsync(ListCustomerStatementsQuery query, CancellationToken cancellationToken);
    Task<(Stream Content, string MediaType, string FileName)> OpenStatementAsync(Guid companyId, Guid statementId, CancellationToken cancellationToken);
    Task<CustomerCollectionPolicyDto?> GetPolicyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CustomerCollectionPolicyDto> UpsertPolicyAsync(UpsertCustomerCollectionPolicyCommand command, CancellationToken cancellationToken);
    Task<CustomerCollectionCaseListResult> ListCasesAsync(ListCustomerCollectionCasesQuery query, CancellationToken cancellationToken);
    Task<CustomerCollectionCaseDto> RecordDisputeAsync(RecordCustomerDisputeCommand command, CancellationToken cancellationToken);
    Task<CustomerCollectionCaseDto> ResolveDisputeAsync(ResolveCustomerDisputeCommand command, CancellationToken cancellationToken);
    Task<CustomerCollectionCaseDto> RecordPromiseAsync(RecordPromiseToPayCommand command, CancellationToken cancellationToken);
    Task<CustomerCollectionCaseDto> ResolvePromiseAsync(ResolvePromiseToPayCommand command, CancellationToken cancellationToken);
    Task<CustomerCollectionCaseDto> RecordResponseAsync(RecordCustomerCollectionResponseCommand command, CancellationToken cancellationToken);
    Task<CustomerReminderDraftDto> PrepareReminderAsync(PrepareCustomerReminderCommand command, CancellationToken cancellationToken);
    Task<CustomerReminderDeliveryDto> SendReminderAsync(SendCustomerReminderCommand command, CancellationToken cancellationToken);
    Task<CustomerCollectionMetricsDto> GetMetricsAsync(CollectionMetricsQuery query, CancellationToken cancellationToken);
}

public interface ICustomerCollectionWorkerRunner
{
    Task<CustomerCollectionWorkerResult> RunAsync(RunCustomerCollectionWorkerCommand command, CancellationToken cancellationToken);
}

public interface ICustomerReminderDeliveryDispatcher
{
    Task DeliverAsync(Guid companyId, Guid deliveryId, CancellationToken cancellationToken);
}

public sealed class CustomerCollectionException(string reasonCode, string message, bool isConflict = false, long? currentVersion = null) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
    public bool IsConflict { get; } = isConflict;
    public long? CurrentVersion { get; } = currentVersion;
}
