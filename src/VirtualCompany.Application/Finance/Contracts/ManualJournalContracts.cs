namespace VirtualCompany.Application.Finance;

public static class ManualJournalReasonCodes
{
    public const string NotFound = "manual_journal_not_found";
    public const string NotEditable = "manual_journal_not_editable";
    public const string VersionConflict = "manual_journal_version_conflict";
    public const string IdempotencyConflict = "manual_journal_idempotency_conflict";
    public const string EvidenceRequired = "manual_journal_evidence_required";
    public const string EvidenceNotFound = "manual_journal_evidence_not_found";
    public const string InvalidEvidence = "manual_journal_evidence_invalid";
    public const string ExplanationRequired = "manual_journal_explanation_required";
    public const string ApprovalRequired = "manual_journal_approval_required";
    public const string ApprovalPending = "manual_journal_approval_pending";
    public const string ApprovalRejected = "manual_journal_approval_rejected";
    public const string ApprovalStale = "manual_journal_approval_stale";
    public const string AlreadyPosted = "manual_journal_already_posted";
    public const string InvalidCorrection = "manual_journal_correction_invalid";
}

public sealed record ManualJournalLineInput(
    Guid FinanceAccountId,
    decimal DebitAmount,
    decimal CreditAmount,
    string? Description = null,
    Guid? CostCenterId = null,
    IReadOnlyDictionary<string, string>? TaxFacts = null,
    IReadOnlyDictionary<string, string>? DimensionFacts = null);

public sealed record ManualJournalDraftInput(
    Guid FiscalPeriodId,
    string VoucherSeriesCode,
    DateOnly DocumentDate,
    DateOnly PostingDate,
    string Explanation,
    string Currency,
    IReadOnlyList<ManualJournalLineInput> Lines,
    IReadOnlyList<Guid> EvidenceDocumentIds,
    Guid? OriginalLedgerEntryId = null,
    string? CorrectionReason = null);

public sealed record CreateManualJournalDraftCommand(Guid CompanyId, ManualJournalDraftInput Draft, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record UpdateManualJournalDraftCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion, ManualJournalDraftInput Draft,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record DiscardManualJournalDraftCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record PreviewManualJournalDraftQuery(Guid CompanyId, Guid DraftId, long ExpectedVersion, Guid ActorUserId);
public sealed record SubmitManualJournalForApprovalCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record PostApprovedManualJournalCommand(Guid CompanyId, Guid DraftId, long ExpectedVersion, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record CreateAdjustingJournalDraftCommand(Guid CompanyId, Guid OriginalLedgerEntryId, ManualJournalDraftInput Draft,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record GetManualJournalDraftQuery(Guid CompanyId, Guid DraftId);
public sealed record ListManualJournalDraftsQuery(Guid CompanyId, string? Status = null, int Skip = 0, int Take = 100);
public sealed record GetManualJournalReferenceDataQuery(Guid CompanyId);

public sealed record ManualJournalEvidenceDto(Guid DocumentId, string Title, string ContentHash, string OriginalFileName);
public sealed record ManualJournalVoucherSeriesDto(string Code, string DisplayName, string NumberPrefix);
public sealed record ManualJournalEvidenceOptionDto(Guid DocumentId, string Title, string OriginalFileName, DateTime UploadedUtc);
public sealed record ManualJournalReferenceDataDto(
    IReadOnlyList<ManualJournalVoucherSeriesDto> VoucherSeries,
    IReadOnlyList<ManualJournalEvidenceOptionDto> EvidenceDocuments);
public sealed record ManualJournalLineDto(Guid Id, int LineNumber, Guid FinanceAccountId, string AccountCode, string AccountName,
    decimal DebitAmount, decimal CreditAmount, string Currency, string? Description, Guid? CostCenterId,
    IReadOnlyDictionary<string, string> TaxFacts, IReadOnlyDictionary<string, string> DimensionFacts);
public sealed record ManualJournalApprovalDto(Guid Id, string Status, string? DecisionSummary, long DraftVersion,
    string PayloadHash, DateTime CreatedUtc, DateTime? DecidedUtc);
public sealed record ManualJournalPolicyDecisionDto(bool IsAllowed, bool RequiresApproval, decimal ApprovalThreshold,
    string ApprovalCurrency, IReadOnlyList<AccountingPostingIssue> Issues, IReadOnlyList<AccountingPostingIssue> Warnings);
public sealed record ManualJournalDraftDto(Guid Id, Guid CompanyId, Guid FiscalPeriodId, string VoucherSeriesCode,
    DateOnly DocumentDate, DateOnly PostingDate, string Explanation, string Currency, string Status, long Version,
    string PayloadHash, Guid CreatedByUserId, Guid UpdatedByUserId, Guid? ApprovalRequestId, Guid? LedgerEntryId,
    Guid? OriginalLedgerEntryId, string? CorrectionReason, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? PostedUtc,
    decimal DebitTotal, decimal CreditTotal, decimal Difference, IReadOnlyList<ManualJournalLineDto> Lines,
    IReadOnlyList<ManualJournalEvidenceDto> Evidence, ManualJournalApprovalDto? Approval);
public sealed record ManualJournalPreviewDto(ManualJournalDraftDto Draft, AccountingPostingPreview PostingPreview,
    ManualJournalPolicyDecisionDto Policy);
public sealed record ManualJournalDraftListResult(IReadOnlyList<ManualJournalDraftDto> Items, int TotalCount, int Skip, int Take);
public sealed record ManualJournalSubmissionResult(ManualJournalDraftDto Draft, Guid ApprovalRequestId, bool IsIdempotentReplay);
public sealed record ManualJournalPostingResult(ManualJournalDraftDto Draft, AccountingJournalDto Journal, bool IsIdempotentReplay);

public interface IManualJournalPolicy
{
    Task<ManualJournalPolicyDecisionDto> EvaluateAsync(Guid companyId, ManualJournalDraftInput draft, CancellationToken cancellationToken);
}

public interface IManualJournalService
{
    Task<ManualJournalDraftDto> CreateAsync(CreateManualJournalDraftCommand command, CancellationToken cancellationToken);
    Task<ManualJournalDraftDto> UpdateAsync(UpdateManualJournalDraftCommand command, CancellationToken cancellationToken);
    Task<ManualJournalDraftDto> DiscardAsync(DiscardManualJournalDraftCommand command, CancellationToken cancellationToken);
    Task<ManualJournalPreviewDto> PreviewAsync(PreviewManualJournalDraftQuery query, CancellationToken cancellationToken);
    Task<ManualJournalSubmissionResult> SubmitAsync(SubmitManualJournalForApprovalCommand command, CancellationToken cancellationToken);
    Task<ManualJournalPostingResult> PostAsync(PostApprovedManualJournalCommand command, CancellationToken cancellationToken);
    Task<ManualJournalDraftDto> CreateAdjustmentAsync(CreateAdjustingJournalDraftCommand command, CancellationToken cancellationToken);
    Task<ManualJournalDraftDto> GetAsync(GetManualJournalDraftQuery query, CancellationToken cancellationToken);
    Task<ManualJournalDraftListResult> ListAsync(ListManualJournalDraftsQuery query, CancellationToken cancellationToken);
    Task<ManualJournalReferenceDataDto> GetReferenceDataAsync(GetManualJournalReferenceDataQuery query, CancellationToken cancellationToken);
}

public sealed class ManualJournalException : Exception
{
    public ManualJournalException(string reasonCode, string message, bool isConflict = false, long? currentVersion = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        IsConflict = isConflict;
        CurrentVersion = currentVersion;
    }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
