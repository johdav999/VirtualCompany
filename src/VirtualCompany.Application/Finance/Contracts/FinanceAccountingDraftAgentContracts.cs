using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Finance;

public static class FinanceAccountingDraftAgentToolIds
{
    public const string CreateManualJournalDraft = "finance.accounting_drafts.create_manual_journal";
    public const string CreateCorrectionDraft = "finance.accounting_drafts.create_correction_or_reversal";
    public const string CreateReconciliationDecisionDraft = "finance.accounting_drafts.create_reconciliation_decision";
    public const string CreateAccountingTreatmentDraft = "finance.accounting_drafts.create_accounting_treatment";
    public const string SubmitForApproval = "finance.accounting_drafts.submit_for_approval";

    public static IReadOnlyList<string> RecommendationTools { get; } =
    [
        CreateManualJournalDraft,
        CreateCorrectionDraft,
        CreateReconciliationDecisionDraft,
        CreateAccountingTreatmentDraft
    ];

    public static IReadOnlyList<string> ExecuteTools { get; } = [SubmitForApproval];
    public static IReadOnlyList<string> All { get; } = [.. RecommendationTools, .. ExecuteTools];
    public static bool Contains(string toolName) => All.Contains(toolName, StringComparer.OrdinalIgnoreCase);
}

public static class FinanceAccountingDraftAgentContract
{
    public const string Version = "2026-09-01.prompt5.v1";
    public const int MaximumLines = 100;
    public const int MaximumEvidenceRecords = 100;
    public const string AuthorityNotice =
        "Draft creation and approval submission never post a journal, apply a reconciliation, reopen a period, or self-approve.";
}

public static class FinanceAccountingDraftSourceTypes
{
    public const string LedgerJournal = "ledger_journal";
    public const string BankTransaction = "bank_transaction";
    public const string Payment = "payment";
    public const string Invoice = "invoice";
    public const string Bill = "bill";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        LedgerJournal, BankTransaction, Payment, Invoice, Bill
    };
}

public sealed record FinanceAccountingDraftResultDto(
    string DraftKind,
    ManualJournalDraftDto Draft,
    ManualJournalPolicyDecisionDto Validation,
    AccountingPostingPreview PostingPreview,
    IReadOnlyList<string> ModelProposedFields,
    IReadOnlyList<string> SafeEditableFields,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> AllowedActions,
    bool SubmissionBlocked,
    string AuthorityNotice);

public sealed record FinanceReconciliationDecisionDraftResultDto(
    AdvancedReconciliationGroupDetailDto Draft,
    string Rationale,
    IReadOnlyList<ManualJournalSourceReferenceDto> SourceRecords,
    IReadOnlyList<string> AllowedActions,
    bool AppliesMatch,
    string AuthorityNotice);

public sealed record FinanceAccountingDraftSubmissionDto(
    ManualJournalSubmissionResult Submission,
    string SubmittedPayloadHash,
    IReadOnlyList<ManualJournalSourceReferenceDto> SourceRecords,
    IReadOnlyList<string> AllowedActions,
    bool Posted,
    string AuthorityNotice);

public interface IFinanceAccountingDraftAgentService
{
    Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken);
}
