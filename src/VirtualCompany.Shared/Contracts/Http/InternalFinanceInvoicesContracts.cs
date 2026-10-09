using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;
public sealed record ReviewFinanceInvoiceWorkflowRequest(Guid? WorkflowInstanceId, Guid? AgentId, Dictionary<string, System.Text.Json.Nodes.JsonNode?>? Payload);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record UpdateFinanceInvoiceApprovalStatusRequest(string Status)
{
    public string Status { get; set; } = Status;

    public UpdateFinanceInvoiceApprovalStatusRequest() : this(string.Empty)
    {
    }
}

public sealed record FinanceInvoiceWorkflowHistoryItemResponse(string EventId, string EventType, string ActorOrSourceDisplayName, DateTime OccurredAtUtc, Guid? RelatedAuditId, Guid? RelatedApprovalId)
{
    public string EventId { get; set; } = EventId;
    public string EventType { get; set; } = EventType;
    public string ActorOrSourceDisplayName { get; set; } = ActorOrSourceDisplayName;
    public DateTime OccurredAtUtc { get; set; } = OccurredAtUtc;
    public Guid? RelatedAuditId { get; set; } = RelatedAuditId;
    public Guid? RelatedApprovalId { get; set; } = RelatedApprovalId;

    public FinanceInvoiceWorkflowHistoryItemResponse() : this(string.Empty, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record FinanceInvoiceReviewActionAvailabilityResponse(bool IsActionable, bool CanApprove, bool CanReject, bool CanSendForFollowUp)
{
    public bool IsActionable { get; set; } = IsActionable;
    public bool CanApprove { get; set; } = CanApprove;
    public bool CanReject { get; set; } = CanReject;
    public bool CanSendForFollowUp { get; set; } = CanSendForFollowUp;

    public FinanceInvoiceReviewActionAvailabilityResponse() : this(default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceInvoiceRecommendationDetailsResponse(string Classification, string Risk, string RationaleSummary, decimal Confidence, string RecommendedAction, string CurrentWorkflowStatus)
{
    public string Classification { get; set; } = Classification;
    public string Risk { get; set; } = Risk;
    public string RationaleSummary { get; set; } = RationaleSummary;
    public decimal Confidence { get; set; } = Confidence;
    public string RecommendedAction { get; set; } = RecommendedAction;
    public string CurrentWorkflowStatus { get; set; } = CurrentWorkflowStatus;

    public FinanceInvoiceRecommendationDetailsResponse() : this(string.Empty, string.Empty, string.Empty, default !, string.Empty, string.Empty)
    {
    }
}

public sealed record FinanceInvoiceReviewDetailResponse(Guid Id, string InvoiceNumber, string SupplierName, decimal Amount, string Currency, string Status, string RiskLevel, string RecommendationStatus, string RecommendationSummary, string RecommendedAction, decimal Confidence, DateTime LastUpdatedUtc, Guid SourceInvoiceId, Guid? RelatedApprovalId, FinanceInvoiceReviewActionAvailabilityResponse Actions, FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails = null, IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse>? WorkflowHistory = null)
{
    public Guid Id { get; set; } = Id;
    public string InvoiceNumber { get; set; } = InvoiceNumber;
    public string SupplierName { get; set; } = SupplierName;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Status { get; set; } = Status;
    public string RiskLevel { get; set; } = RiskLevel;
    public string RecommendationStatus { get; set; } = RecommendationStatus;
    public string RecommendationSummary { get; set; } = RecommendationSummary;
    public string RecommendedAction { get; set; } = RecommendedAction;
    public decimal Confidence { get; set; } = Confidence;
    public DateTime LastUpdatedUtc { get; set; } = LastUpdatedUtc;
    public Guid SourceInvoiceId { get; set; } = SourceInvoiceId;
    public Guid? RelatedApprovalId { get; set; } = RelatedApprovalId;
    public FinanceInvoiceReviewActionAvailabilityResponse Actions { get; set; } = Actions;
    public FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails { get; set; } = RecommendationDetails;
    public IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse>? WorkflowHistory { get; set; } = WorkflowHistory;

    public FinanceInvoiceReviewDetailResponse() : this(default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, new(), default !, [])
    {
    }
}

public sealed record FinanceInvoiceReviewListItemResponse(Guid Id, string InvoiceNumber, string SupplierName, decimal Amount, string Currency, string Status, string RiskLevel, string RecommendationStatus, string RecommendationOutcome, decimal Confidence, DateTime LastUpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string InvoiceNumber { get; set; } = InvoiceNumber;
    public string SupplierName { get; set; } = SupplierName;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Status { get; set; } = Status;
    public string RiskLevel { get; set; } = RiskLevel;
    public string RecommendationStatus { get; set; } = RecommendationStatus;
    public string RecommendationOutcome { get; set; } = RecommendationOutcome;
    public decimal Confidence { get; set; } = Confidence;
    public DateTime LastUpdatedUtc { get; set; } = LastUpdatedUtc;

    public FinanceInvoiceReviewListItemResponse() : this(default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record FinanceInvoiceDetailResponse(Guid Id, Guid CounterpartyId, string CounterpartyName, string InvoiceNumber, DateTime IssuedUtc, DateTime DueUtc, decimal Amount, string Currency, string Status, FinanceInvoiceWorkflowContextDto? WorkflowContext, FinanceActionPermissionsDto Permissions, FinanceLinkedDocumentAccessDto LinkedDocument, FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails = null, IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse>? WorkflowHistory = null, IReadOnlyList<NormalizedFinanceInsightDto>? AgentInsights = null, string PostingStatus = "booked", string SettlementStatus = "unpaid", string DueStatus = "not_due", string DocumentKind = "invoice", string? ProviderStatus = null, CustomerInvoiceAccountingStateDto? Accounting = null, string Source = "manual")
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
    public FinanceInvoiceWorkflowContextDto? WorkflowContext { get; set; } = WorkflowContext;
    public FinanceActionPermissionsDto Permissions { get; set; } = Permissions;
    public FinanceLinkedDocumentAccessDto LinkedDocument { get; set; } = LinkedDocument;
    public FinanceInvoiceRecommendationDetailsResponse? RecommendationDetails { get; set; } = RecommendationDetails;
    public IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse>? WorkflowHistory { get; set; } = WorkflowHistory;
    public IReadOnlyList<NormalizedFinanceInsightDto>? AgentInsights { get; set; } = AgentInsights;
    public string PostingStatus { get; set; } = PostingStatus;
    public string SettlementStatus { get; set; } = SettlementStatus;
    public string DueStatus { get; set; } = DueStatus;
    public string DocumentKind { get; set; } = DocumentKind;
    public string? ProviderStatus { get; set; } = ProviderStatus;
    public CustomerInvoiceAccountingStateDto? Accounting { get; set; } = Accounting;
    public string Source { get; set; } = Source;

    public FinanceInvoiceDetailResponse() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !, new(), new(), default !, [], [], string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty)
    {
    }
}
