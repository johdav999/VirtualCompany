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
    public const string VoucherSeriesPolicyMismatch = "voucher_series_policy_mismatch";
    public const string AccountNotFound = "posting_account_not_found";
    public const string AccountUnclassified = "posting_account_unclassified";
    public const string AccountInactive = "posting_account_inactive";
    public const string AccountPostingDisabled = "account_posting_disabled";
    public const string ManualPostingRestricted = "manual_posting_restricted";
    public const string CurrencyMismatch = "posting_currency_mismatch";
    public const string DocumentCurrencyMismatch = "posting_document_currency_mismatch";
    public const string DocumentAmountsUnbalanced = "posting_document_amounts_unbalanced";
    public const string RateFactsMissing = "posting_exchange_rate_facts_missing";
    public const string RateFactsInvalid = "posting_exchange_rate_facts_invalid";
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
    IReadOnlyDictionary<string, string>? DimensionFacts = null,
    decimal? DocumentDebitAmount = null,
    decimal? DocumentCreditAmount = null,
    string? DocumentCurrency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null,
    Guid? ExchangeRateConversionId = null,
    string? ExchangeRateIdentity = null,
    decimal? ConversionRoundingResidual = null,
    IReadOnlyList<Guid>? DimensionMemberIds = null);

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
    string? CorrelationId = null,
    string ActorType = AuditActorTypes.User);

public sealed record ListAccountingJournalsQuery(Guid CompanyId, DateOnly? From = null, DateOnly? To = null, int Skip = 0, int Take = 100,
    string? Search = null, string? SourceType = null, string? PostingType = null, string? VoucherSeriesCode = null);
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
