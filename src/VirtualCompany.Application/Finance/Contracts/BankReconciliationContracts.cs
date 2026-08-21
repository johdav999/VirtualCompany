namespace VirtualCompany.Application.Finance;

public static class BankReconciliationReasonCodes
{
    public const string SourceVersionConflict = "bank_source_version_conflict";
    public const string ImportIdentityConflict = "bank_import_identity_conflict";
    public const string RowIdentityConflict = "bank_row_identity_conflict";
    public const string AllocationExceedsTransaction = "bank_allocation_exceeds_transaction";
    public const string AllocationExceedsPayment = "bank_allocation_exceeds_payment";
    public const string WrongDirection = "bank_payment_direction_mismatch";
    public const string CurrencyMismatch = "bank_payment_currency_mismatch";
    public const string MissingReviewedHandling = "bank_reviewed_handling_required";
    public const string MissingAccountRole = "bank_account_role_missing";
    public const string UnbalancedAdjustments = "bank_adjustments_unbalanced";
    public const string SuspenseRequired = "bank_suspense_required";
    public const string NotSuspense = "bank_transaction_not_in_suspense";
    public const string AlreadyCorrected = "bank_suspense_already_corrected";
}

public static class BankReconciliationHandlingModes
{
    public const string Payment = "payment";
    public const string Categorization = "categorization";
    public const string Suspense = "suspense";
    public const string LeaveUnmatched = "leave_unmatched";
}

public static class BankReconciliationAdjustmentKinds
{
    public const string BankFee = AccountingAccountRoleKeys.BankFee;
    public const string RoundingDifference = AccountingAccountRoleKeys.RoundingDifference;
    public const string ExchangeGain = AccountingAccountRoleKeys.ExchangeGain;
    public const string ExchangeLoss = AccountingAccountRoleKeys.ExchangeLoss;
    public const string SettlementDiscount = AccountingAccountRoleKeys.SettlementDiscount;
}

public sealed record BankReconciliationAdjustmentDto(string Kind, decimal DebitAmount, decimal CreditAmount, string Explanation);

public sealed record ImportBankStatementRowDto(
    string RowIdentity,
    DateTime BookingDateUtc,
    DateTime ValueDateUtc,
    decimal Amount,
    string Currency,
    string ReferenceText,
    string Counterparty,
    string? ExternalReference = null);

public sealed record ImportBankStatementCommand(
    Guid CompanyId,
    Guid BankAccountId,
    string SourceKey,
    string StatementIdentity,
    string ContentHash,
    IReadOnlyList<ImportBankStatementRowDto> Rows,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record BankStatementImportResultDto(
    Guid ImportId,
    int ImportedCount,
    int DuplicateCount,
    int ConflictCount,
    bool IsIdempotentReplay,
    IReadOnlyList<string> ConflictRowIdentities);

public sealed record ReclassifyBankSuspenseCommand(
    Guid CompanyId,
    Guid BankTransactionId,
    Guid TargetFinanceAccountId,
    Guid FiscalPeriodId,
    DateOnly PostingDate,
    string Reason,
    long ExpectedSourceVersion,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record BankReconciliationCandidatePaymentDto(
    Guid PaymentId,
    string PaymentType,
    decimal Amount,
    decimal AlreadyLinkedAmount,
    decimal AvailableAmount,
    string Currency,
    DateTime PaymentDate,
    string CounterpartyReference,
    Guid? InvoiceId,
    string? InvoiceNumber,
    Guid? BillId,
    string? BillNumber);

public sealed record BankReconciliationJournalLinkDto(
    Guid LedgerEntryId,
    string EntryNumber,
    string PostingType,
    string Status,
    DateOnly? PostingDate,
    bool IsOriginalSuspense,
    bool IsCorrection);

public sealed record BankReconciliationFollowUpDto(
    Guid Id,
    string Status,
    string Reason,
    Guid LedgerEntryId,
    DateTime CreatedUtc,
    DateTime? ResolvedUtc);

public sealed record BankReconciliationItemDto(
    Guid BankTransactionId,
    DateTime BookingDate,
    decimal Amount,
    string Currency,
    string Counterparty,
    string ReferenceText,
    string BankAccountDisplayName,
    string State,
    decimal AllocatedAmount,
    decimal RemainingAmount,
    int LinkedPaymentCount,
    long SourceVersion,
    string? ConflictCode,
    string? ConflictExplanation,
    Guid? LedgerEntryId);

public sealed record ListBankReconciliationItemsQuery(
    Guid CompanyId,
    string? State = null,
    string? Search = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Limit = 200);

public sealed record BankReconciliationWorkspaceDto(
    IReadOnlyList<BankReconciliationItemDto> Items,
    IReadOnlyDictionary<string, int> StateCounts);

public sealed record GetBankReconciliationDetailQuery(Guid CompanyId, Guid BankTransactionId);

public sealed record BankReconciliationDetailDto(
    BankTransactionDetailDto Transaction,
    string State,
    decimal RemainingAmount,
    long SourceVersion,
    string? HandlingMode,
    string? ReviewReason,
    IReadOnlyList<BankReconciliationCandidatePaymentDto> CandidatePayments,
    IReadOnlyList<BankReconciliationJournalLinkDto> Journals,
    BankReconciliationFollowUpDto? FollowUp,
    bool CanPostToSuspense,
    bool CanReclassify,
    string? BlockingReason);

public sealed record AccountingAccountRoleResolutionDto(string RoleKey, Guid FinanceAccountId, string AccountCode, string AccountName);

public interface IAccountingAccountRoleResolver
{
    Task<AccountingAccountRoleResolutionDto> ResolveRequiredAsync(Guid companyId, string roleKey, CancellationToken cancellationToken);
    Task<AccountingAccountRoleResolutionDto?> ResolveOptionalAsync(Guid companyId, string roleKey, CancellationToken cancellationToken);
}
