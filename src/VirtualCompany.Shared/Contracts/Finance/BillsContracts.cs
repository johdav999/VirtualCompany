using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record FinanceBillDetailDto(Guid Id, Guid CounterpartyId, string CounterpartyName, string BillNumber, DateTime ReceivedUtc, DateTime DueUtc, decimal Amount, string Currency, string Status, FinanceActionPermissionsDto Permissions, FinanceLinkedDocumentAccessDto LinkedDocument, IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights, string PostingStatus = "booked", string SettlementStatus = "unpaid", string DueStatus = "not_due", string DocumentKind = "supplier_invoice", string? ProviderStatus = null, string ProcessingStatus = "none", FinanceTransactionPaymentContextDto? PaymentContext = null, IReadOnlyList<FinanceInvoiceRelatedTransactionDto>? RelatedTransactions = null, SupplierInvoicePaymentProposalDto? PaymentProposal = null, SupplierInvoiceSourceDocumentAttachmentDto? SourceDocumentAttachment = null, SupplierInvoiceDraftActionDto? DraftAction = null, IReadOnlyList<SupplierInvoiceCorrectionActionDto>? CorrectionActions = null, SupplierInvoiceEnrichmentActionDto? EnrichmentAction = null, PaidSupplierBillExpenseAvailabilityDto? PaidExpensePostingAvailability = null, SupplierBillAccountingStateDto? Accounting = null, string Source = "manual")
{
    public Guid Id { get; set; } = Id;
    public Guid CounterpartyId { get; set; } = CounterpartyId;
    public string CounterpartyName { get; set; } = CounterpartyName;
    public string BillNumber { get; set; } = BillNumber;
    public DateTime ReceivedUtc { get; set; } = ReceivedUtc;
    public DateTime DueUtc { get; set; } = DueUtc;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Status { get; set; } = Status;
    public FinanceActionPermissionsDto Permissions { get; set; } = Permissions;
    public FinanceLinkedDocumentAccessDto LinkedDocument { get; set; } = LinkedDocument;
    public IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights { get; set; } = AgentInsights;
    public string PostingStatus { get; set; } = PostingStatus;
    public string SettlementStatus { get; set; } = SettlementStatus;
    public string DueStatus { get; set; } = DueStatus;
    public string DocumentKind { get; set; } = DocumentKind;
    public string? ProviderStatus { get; set; } = ProviderStatus;
    public string ProcessingStatus { get; set; } = ProcessingStatus;
    public FinanceTransactionPaymentContextDto? PaymentContext { get; set; } = PaymentContext;
    public IReadOnlyList<FinanceInvoiceRelatedTransactionDto>? RelatedTransactions { get; set; } = RelatedTransactions;
    public SupplierInvoicePaymentProposalDto? PaymentProposal { get; set; } = PaymentProposal;
    public SupplierInvoiceSourceDocumentAttachmentDto? SourceDocumentAttachment { get; set; } = SourceDocumentAttachment;
    public SupplierInvoiceDraftActionDto? DraftAction { get; set; } = DraftAction;
    public IReadOnlyList<SupplierInvoiceCorrectionActionDto>? CorrectionActions { get; set; } = CorrectionActions;
    public SupplierInvoiceEnrichmentActionDto? EnrichmentAction { get; set; } = EnrichmentAction;
    public PaidSupplierBillExpenseAvailabilityDto? PaidExpensePostingAvailability { get; set; } = PaidExpensePostingAvailability;
    public SupplierBillAccountingStateDto? Accounting { get; set; } = Accounting;
    public string Source { get; set; } = Source;

    public FinanceBillDetailDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, new(), new(), [], string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, [], default !, default !, default !, [], default !, default !, default !, string.Empty)
    {
    }
}

public sealed record FinanceBillDto(Guid Id, Guid CounterpartyId, string CounterpartyName, string BillNumber, DateTime ReceivedUtc, DateTime DueUtc, decimal Amount, string Currency, string Status, FinanceLinkedDocumentDto? LinkedDocument, string Source = "simulation", string PostingStatus = "booked", string SettlementStatus = "unpaid", string DueStatus = "not_due", string DocumentKind = "supplier_invoice", string? ProviderStatus = null, string ProcessingStatus = "none", FinanceTransactionPaymentContextDto? PaymentContext = null, SupplierInvoicePaymentProposalDto? PaymentProposal = null, SupplierInvoiceSourceDocumentAttachmentDto? SourceDocumentAttachment = null, SupplierInvoiceDraftActionDto? DraftAction = null, IReadOnlyList<SupplierInvoiceCorrectionActionDto>? CorrectionActions = null, SupplierInvoiceEnrichmentActionDto? EnrichmentAction = null)
{
    public Guid Id { get; set; } = Id;
    public Guid CounterpartyId { get; set; } = CounterpartyId;
    public string CounterpartyName { get; set; } = CounterpartyName;
    public string BillNumber { get; set; } = BillNumber;
    public DateTime ReceivedUtc { get; set; } = ReceivedUtc;
    public DateTime DueUtc { get; set; } = DueUtc;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Status { get; set; } = Status;
    public FinanceLinkedDocumentDto? LinkedDocument { get; set; } = LinkedDocument;
    public string Source { get; set; } = Source;
    public string PostingStatus { get; set; } = PostingStatus;
    public string SettlementStatus { get; set; } = SettlementStatus;
    public string DueStatus { get; set; } = DueStatus;
    public string DocumentKind { get; set; } = DocumentKind;
    public string? ProviderStatus { get; set; } = ProviderStatus;
    public string ProcessingStatus { get; set; } = ProcessingStatus;
    public FinanceTransactionPaymentContextDto? PaymentContext { get; set; } = PaymentContext;
    public SupplierInvoicePaymentProposalDto? PaymentProposal { get; set; } = PaymentProposal;
    public SupplierInvoiceSourceDocumentAttachmentDto? SourceDocumentAttachment { get; set; } = SourceDocumentAttachment;
    public SupplierInvoiceDraftActionDto? DraftAction { get; set; } = DraftAction;
    public IReadOnlyList<SupplierInvoiceCorrectionActionDto>? CorrectionActions { get; set; } = CorrectionActions;
    public SupplierInvoiceEnrichmentActionDto? EnrichmentAction { get; set; } = EnrichmentAction;

    public FinanceBillDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !, default !, default !, [], default !)
    {
    }
}

public sealed record PaidSupplierBillExpenseAvailabilityDto(bool CanPost, string StatusLabel, string StatusTone, string Message, string? AccountCode = null, IReadOnlyList<string>? BlockingReasons = null, IReadOnlyList<string>? ReasonCodes = null, bool RequiresApproval = false)
{
    public bool CanPost { get; set; } = CanPost;
    public string StatusLabel { get; set; } = StatusLabel;
    public string StatusTone { get; set; } = StatusTone;
    public string Message { get; set; } = Message;
    public string? AccountCode { get; set; } = AccountCode;
    public IReadOnlyList<string>? BlockingReasons { get; set; } = BlockingReasons;
    public IReadOnlyList<string>? ReasonCodes { get; set; } = ReasonCodes;
    public bool RequiresApproval { get; set; } = RequiresApproval;

    public PaidSupplierBillExpenseAvailabilityDto() : this(default !, string.Empty, "neutral", string.Empty, default !, [], [], default !)
    {
    }
}

public sealed record SupplierInvoiceCorrectionActionDto(Guid Id, Guid BillId, string ActionType, string Status, string? ProviderKey, Guid? ConnectionId, Guid? RequestedByUserId, Guid? ApprovedByUserId, Guid? TaskId, Guid? ApprovalRequestId, DateTime? RequestedUtc, DateTime? ApprovedUtc, DateTime? CompletedUtc, Guid? CreditNoteBillId, string? ProviderCreditNoteNumber, string? ResponseSummary, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid BillId { get; set; } = BillId;
    public string ActionType { get; set; } = ActionType;
    public string Status { get; set; } = Status;
    public string? ProviderKey { get; set; } = ProviderKey;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public Guid? RequestedByUserId { get; set; } = RequestedByUserId;
    public Guid? ApprovedByUserId { get; set; } = ApprovedByUserId;
    public Guid? TaskId { get; set; } = TaskId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public DateTime? RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? ApprovedUtc { get; set; } = ApprovedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public Guid? CreditNoteBillId { get; set; } = CreditNoteBillId;
    public string? ProviderCreditNoteNumber { get; set; } = ProviderCreditNoteNumber;
    public string? ResponseSummary { get; set; } = ResponseSummary;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public SupplierInvoiceCorrectionActionDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record SupplierInvoiceSourceDocumentAttachmentDto(Guid Id, Guid BillId, Guid? DocumentId, string Status, string? ProviderKey, Guid? ConnectionId, Guid? RequestedByUserId, DateTime? RequestedUtc, DateTime? AttachedUtc, string? ResponseSummary, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid BillId { get; set; } = BillId;
    public Guid? DocumentId { get; set; } = DocumentId;
    public string Status { get; set; } = Status;
    public string? ProviderKey { get; set; } = ProviderKey;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public Guid? RequestedByUserId { get; set; } = RequestedByUserId;
    public DateTime? RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? AttachedUtc { get; set; } = AttachedUtc;
    public string? ResponseSummary { get; set; } = ResponseSummary;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public SupplierInvoiceSourceDocumentAttachmentDto() : this(default !, default !, default !, "not_attached", default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record PaidSupplierBillExpensePostingDto(Guid BillId, Guid DraftActionId, string Status, bool Posted, string ProviderKey, Guid? ConnectionId, string Summary, DateTime? RequestedUtc, DateTime? BookedUtc, SupplierInvoiceDraftActionDto DraftAction)
{
    public Guid BillId { get; set; } = BillId;
    public Guid DraftActionId { get; set; } = DraftActionId;
    public string Status { get; set; } = Status;
    public bool Posted { get; set; } = Posted;
    public string ProviderKey { get; set; } = ProviderKey;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public string Summary { get; set; } = Summary;
    public DateTime? RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? BookedUtc { get; set; } = BookedUtc;
    public SupplierInvoiceDraftActionDto DraftAction { get; set; } = DraftAction;

    public PaidSupplierBillExpensePostingDto() : this(default !, default !, string.Empty, default !, string.Empty, default !, string.Empty, default !, default !, new())
    {
    }
}

public sealed record SupplierInvoiceDraftActionDto(Guid Id, Guid BillId, string Status, string? ProviderKey, Guid? ConnectionId, Guid? RequestedByUserId, DateTime? RequestedUtc, DateTime? UpdatedInProviderUtc, DateTime? BookedUtc, string? ResponseSummary, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid BillId { get; set; } = BillId;
    public string Status { get; set; } = Status;
    public string? ProviderKey { get; set; } = ProviderKey;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public Guid? RequestedByUserId { get; set; } = RequestedByUserId;
    public DateTime? RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? UpdatedInProviderUtc { get; set; } = UpdatedInProviderUtc;
    public DateTime? BookedUtc { get; set; } = BookedUtc;
    public string? ResponseSummary { get; set; } = ResponseSummary;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public SupplierInvoiceDraftActionDto() : this(default !, default !, "draft", default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record SupplierInvoiceEnrichmentActionDto(Guid Id, Guid BillId, string Status, string? ProviderKey, Guid? ConnectionId, Guid? RequestedByUserId, Guid? ApprovedByUserId, Guid? TaskId, Guid? ApprovalRequestId, DateTime? RequestedUtc, DateTime? ApprovedUtc, DateTime? SyncedUtc, string? ResponseSummary, JsonObject SuggestionPayload, JsonArray ReconciliationWarnings, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid BillId { get; set; } = BillId;
    public string Status { get; set; } = Status;
    public string? ProviderKey { get; set; } = ProviderKey;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public Guid? RequestedByUserId { get; set; } = RequestedByUserId;
    public Guid? ApprovedByUserId { get; set; } = ApprovedByUserId;
    public Guid? TaskId { get; set; } = TaskId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public DateTime? RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? ApprovedUtc { get; set; } = ApprovedUtc;
    public DateTime? SyncedUtc { get; set; } = SyncedUtc;
    public string? ResponseSummary { get; set; } = ResponseSummary;
    public JsonObject SuggestionPayload { get; set; } = SuggestionPayload;
    public JsonArray ReconciliationWarnings { get; set; } = ReconciliationWarnings;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public SupplierInvoiceEnrichmentActionDto() : this(default !, default !, "not_suggested", default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], default !, default !)
    {
    }
}
