

namespace VirtualCompany.Application.Finance;


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

public sealed record CustomerStatementListResult(int TotalCount, IReadOnlyList<CustomerStatementDto> Items);

public sealed record CustomerCollectionCaseListResult(int TotalCount, IReadOnlyList<CustomerCollectionCaseDto> Items);
