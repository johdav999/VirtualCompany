using VirtualCompany.Application.Auditing;

namespace VirtualCompany.Application.Finance;

public static class AccountingPostingReasonCodes
{
    public const string ConfigurationMissing = "accounting_configuration_missing";
    public const string ConfigurationIncomplete = "accounting_configuration_incomplete";
    public const string AuthorityUnavailable = "accounting_authority_unavailable";
    public const string PeriodNotFound = "posting_period_not_found";
    public const string PeriodClosed = "posting_period_closed";
    public const string PeriodLocked = "posting_period_locked";
    public const string PostingDateOutsidePeriod = "posting_date_outside_period";
    public const string VoucherSeriesNotFound = "voucher_series_not_found";
    public const string VoucherSeriesInactive = "voucher_series_inactive";
    public const string AccountNotFound = "posting_account_not_found";
    public const string AccountUnclassified = "posting_account_unclassified";
    public const string AccountInactive = "posting_account_inactive";
    public const string AccountPostingDisabled = "account_posting_disabled";
    public const string ManualPostingRestricted = "manual_posting_restricted";
    public const string CurrencyMismatch = "posting_currency_mismatch";
    public const string InvalidPrecision = "posting_precision_invalid";
    public const string TooFewLines = "posting_requires_two_lines";
    public const string InvalidLine = "posting_line_invalid";
    public const string UnbalancedEntry = "posting_entry_unbalanced";
    public const string InvalidSource = "posting_source_invalid";
    public const string InvalidFacts = "posting_facts_invalid";
    public const string ActorInvalid = "posting_actor_invalid";
    public const string ApprovalMissing = "posting_approval_missing";
    public const string ApprovalInvalid = "posting_approval_invalid";
    public const string IdempotencyConflict = "posting_idempotency_conflict";
    public const string JournalNotFound = "journal_not_found";
    public const string JournalNotPosted = "journal_not_posted";
    public const string AlreadyReversed = "journal_already_reversed";
    public const string CorrectionReasonRequired = "correction_reason_required";
    public const string ConcurrencyConflict = "posting_concurrency_conflict";
}

public sealed record ProposedAccountingLine(
    Guid FinanceAccountId,
    decimal DebitAmount,
    decimal CreditAmount,
    string Currency,
    string? Description = null,
    Guid? CostCenterId = null,
    IReadOnlyDictionary<string, string>? TaxFacts = null,
    IReadOnlyDictionary<string, string>? DimensionFacts = null);

public sealed record ProposedAccountingEvidence(Guid DocumentId, string ContentHash, string Title);

public sealed record ProposedAccountingEntry(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    DateOnly DocumentDate,
    DateOnly PostingDate,
    string PostingType,
    string Description,
    string SourceType,
    string SourceId,
    string SourceVersion,
    string IdempotencyKey,
    IReadOnlyList<ProposedAccountingLine> Lines,
    Guid ActorUserId,
    Guid? ApprovalRequestId = null,
    bool RequiresApproval = false,
    IReadOnlyDictionary<string, string>? PolicyFacts = null,
    string Action = "post",
    string? ApprovalPayloadHash = null,
    IReadOnlyList<ProposedAccountingEvidence>? Evidence = null,
    Guid? OriginalLedgerEntryId = null,
    string? CorrectionReason = null,
    string ActorType = AuditActorTypes.User,
    DateTime? EffectivePostedAtUtc = null);

public sealed record AccountingPostingIssue(string ReasonCode, string Explanation, Guid? SubjectId = null);

public sealed record AccountingPostingPreview(
    bool IsValid,
    decimal DebitTotal,
    decimal CreditTotal,
    decimal Difference,
    string BaseCurrency,
    int RoundingPrecision,
    IReadOnlyList<AccountingPostingIssue> Issues);

public sealed record PreviewAccountingEntryCommand(ProposedAccountingEntry Entry);
public sealed record PreviewNonAuthoritativeAccountingCandidateCommand(ProposedAccountingEntry Entry);
public sealed record PostAccountingEntryCommand(ProposedAccountingEntry Entry, string? CorrelationId = null);
public sealed record MaterializeAccountingProviderSwitchJournalCommand(Guid CompanyId, Guid SwitchId,
    Guid ExecutionId, Guid CandidateId, string FinalSnapshotHash, Guid ActivationApprovalRequestId,
    Guid ActorUserId, string CorrelationId);
public sealed record ReverseAccountingEntryCommand(
    Guid CompanyId,
    Guid OriginalLedgerEntryId,
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    DateOnly PostingDate,
    string Reason,
    string SourceVersion,
    string IdempotencyKey,
    Guid ActorUserId,
    Guid? ApprovalRequestId = null,
    string? CorrelationId = null);

public sealed record AccountingJournalLineDto(
    Guid Id,
    Guid FinanceAccountId,
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    string Currency,
    Guid? CostCenterId,
    string? Description,
    IReadOnlyDictionary<string, string> TaxFacts,
    IReadOnlyDictionary<string, string> DimensionFacts);

public sealed record AccountingJournalEvidenceDto(Guid DocumentId, string Title, string ContentHash, string OriginalFileName);
public sealed record AccountingJournalApprovalDto(Guid Id, string Status, string ApprovalType, string? DecisionSummary,
    DateTime CreatedUtc, DateTime? DecidedUtc);
public sealed record AccountingJournalAuditEventDto(Guid Id, string ActorType, Guid? ActorId, string Action, string Outcome,
    string? Summary, DateTime OccurredUtc);
public sealed record AccountingJournalCorrectionDto(Guid Id, string EntryNumber, string PostingType, DateOnly? PostingDate,
    string? Reason, string Status);

public sealed record AccountingJournalDto(
    Guid Id,
    Guid CompanyId,
    Guid FiscalPeriodId,
    string EntryNumber,
    string Status,
    string VoucherSeriesCode,
    long? VoucherSequenceNumber,
    int? VoucherFiscalYear,
    DateOnly? DocumentDate,
    DateOnly? PostingDate,
    string BaseCurrency,
    string? PostingType,
    string? Description,
    string? SourceType,
    string? SourceId,
    string? SourceVersion,
    string? PolicyPackKey,
    string? PolicyPackVersion,
    Guid? PostedByUserId,
    Guid? ApprovalRequestId,
    Guid? OriginalLedgerEntryId,
    string? CorrectionReason,
    DateTime? PostedAtUtc,
    decimal DebitTotal,
    decimal CreditTotal,
    IReadOnlyList<AccountingJournalLineDto> Lines,
    IReadOnlyList<AccountingJournalEvidenceDto>? Evidence = null,
    AccountingJournalApprovalDto? Approval = null,
    IReadOnlyList<AccountingJournalCorrectionDto>? Corrections = null,
    IReadOnlyList<AccountingJournalAuditEventDto>? AuditTimeline = null);

public sealed record PostedAccountingJournal(AccountingJournalDto Journal, bool IsIdempotentReplay);

public sealed record ListAccountingJournalsQuery(Guid CompanyId, DateOnly? From = null, DateOnly? To = null, int Skip = 0, int Take = 100,
    string? Search = null, string? SourceType = null, string? PostingType = null, string? VoucherSeriesCode = null);
public sealed record AccountingJournalListResult(IReadOnlyList<AccountingJournalDto> Items, int TotalCount, int Skip, int Take);
public sealed record GetAccountingJournalQuery(Guid CompanyId, Guid LedgerEntryId);
public sealed record GetAccountingJournalBySourceQuery(Guid CompanyId, string SourceType, string SourceId, string? SourceVersion = null);

public interface IAccountingPostingService
{
    Task<AccountingPostingPreview> PreviewAsync(PreviewAccountingEntryCommand command, CancellationToken cancellationToken);
    Task<AccountingPostingPreview> PreviewNonAuthoritativeCandidateAsync(
        PreviewNonAuthoritativeAccountingCandidateCommand command,
        CancellationToken cancellationToken);
    Task<PostedAccountingJournal> PostAsync(PostAccountingEntryCommand command, CancellationToken cancellationToken);
    Task<PostedAccountingJournal> MaterializeProviderSwitchJournalAsync(
        MaterializeAccountingProviderSwitchJournalCommand command, CancellationToken cancellationToken);
    Task<PostedAccountingJournal> ReverseAsync(ReverseAccountingEntryCommand command, CancellationToken cancellationToken);
}

public interface IAccountingJournalReadService
{
    Task<AccountingJournalListResult> ListAsync(ListAccountingJournalsQuery query, CancellationToken cancellationToken);
    Task<AccountingJournalDto> GetAsync(GetAccountingJournalQuery query, CancellationToken cancellationToken);
    Task<AccountingJournalDto?> GetBySourceAsync(GetAccountingJournalBySourceQuery query, CancellationToken cancellationToken);
}

public sealed class AccountingPostingException : Exception
{
    public AccountingPostingException(string reasonCode, string message, bool isConflict = false)
        : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? throw new ArgumentException("ReasonCode is required.", nameof(reasonCode)) : reasonCode.Trim();
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
