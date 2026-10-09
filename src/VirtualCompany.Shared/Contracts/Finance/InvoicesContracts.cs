using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record FinanceInvoiceWorkflowContextDto(Guid? WorkflowInstanceId, Guid TaskId, string WorkflowName, string ReviewTaskStatus, Guid? ApprovalRequestId, string Classification, string RiskLevel, string RecommendedAction, string Rationale, decimal Confidence, bool RequiresHumanApproval, string? ApprovalStatus = null, string? ApprovalAssigneeSummary = null, bool CanNavigateToWorkflow = false, bool CanNavigateToApproval = false)
{
    public Guid? WorkflowInstanceId { get; set; } = WorkflowInstanceId;
    public Guid TaskId { get; set; } = TaskId;
    public string WorkflowName { get; set; } = WorkflowName;
    public string ReviewTaskStatus { get; set; } = ReviewTaskStatus;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string Classification { get; set; } = Classification;
    public string RiskLevel { get; set; } = RiskLevel;
    public string RecommendedAction { get; set; } = RecommendedAction;
    public string Rationale { get; set; } = Rationale;
    public decimal Confidence { get; set; } = Confidence;
    public bool RequiresHumanApproval { get; set; } = RequiresHumanApproval;
    public string? ApprovalStatus { get; set; } = ApprovalStatus;
    public string? ApprovalAssigneeSummary { get; set; } = ApprovalAssigneeSummary;
    public bool CanNavigateToWorkflow { get; set; } = CanNavigateToWorkflow;
    public bool CanNavigateToApproval { get; set; } = CanNavigateToApproval;

    public FinanceInvoiceWorkflowContextDto() : this(default !, default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceInvoiceDetailDto(Guid Id, Guid CounterpartyId, string CounterpartyName, string InvoiceNumber, DateTime IssuedUtc, DateTime DueUtc, decimal Amount, string Currency, string Status, FinanceInvoiceWorkflowContextDto? WorkflowContext, FinanceActionPermissionsDto Permissions, FinanceLinkedDocumentAccessDto LinkedDocument, IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights, string PostingStatus = "booked", string SettlementStatus = "unpaid", string DueStatus = "not_due", string DocumentKind = "invoice", string? ProviderStatus = null, string ProcessingStatus = "none", FinanceTransactionPaymentContextDto? PaymentContext = null, IReadOnlyList<FinanceInvoiceRelatedTransactionDto>? RelatedTransactions = null, CustomerInvoiceAccountingStateDto? Accounting = null, string Source = "manual")
{
    public FinanceInvoiceDetailDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !, new(), new(), [], string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, [], default !, string.Empty)
    {
    }
}

public sealed record FinanceInvoiceRelatedTransactionDto(Guid Id, DateTime TransactionUtc, string TransactionType, decimal Amount, string Currency, string Description, string ExternalReference)
{
    public Guid Id { get; set; } = Id;
    public DateTime TransactionUtc { get; set; } = TransactionUtc;
    public string TransactionType { get; set; } = TransactionType;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Description { get; set; } = Description;
    public string ExternalReference { get; set; } = ExternalReference;

    public FinanceInvoiceRelatedTransactionDto() : this(default !, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record FinanceInvoiceDto(Guid Id, Guid CounterpartyId, string CounterpartyName, string InvoiceNumber, DateTime IssuedUtc, DateTime DueUtc, decimal Amount, string Currency, string Status, FinanceLinkedDocumentDto? LinkedDocument, string Source = "simulation", string PostingStatus = "booked", string SettlementStatus = "unpaid", string DueStatus = "not_due", string DocumentKind = "invoice", string? ProviderStatus = null, string ProcessingStatus = "none", FinanceTransactionPaymentContextDto? PaymentContext = null, string AccountingStatus = "not_ready", string AccountingStatusLabel = "Not ready", Guid? AccountingLedgerEntryId = null)
{
    public Guid Id { get; set; } = Id;
    public Guid CounterpartyId { get; set; } = CounterpartyId;
    public string CounterpartyName { get; set; } = CounterpartyName;
    public string InvoiceNumber { get; set; } = InvoiceNumber;
    public DateTime IssuedUtc { get; set; } = IssuedUtc;
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
    public string AccountingStatus { get; set; } = AccountingStatus;
    public string AccountingStatusLabel { get; set; } = AccountingStatusLabel;
    public Guid? AccountingLedgerEntryId { get; set; } = AccountingLedgerEntryId;

    public FinanceInvoiceDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, "not_ready", "Not ready", default !)
    {
    }
}
