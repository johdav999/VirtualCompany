using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record FinanceLinkedDocumentDto(Guid Id, string Title, string? OriginalFileName, string ContentType)
{
    public Guid Id { get; set; } = Id;
    public string Title { get; set; } = Title;
    public string? OriginalFileName { get; set; } = OriginalFileName;
    public string ContentType { get; set; } = ContentType;

    public FinanceLinkedDocumentDto() : this(default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record FinanceWorkflowOutputSchemaDto(string Classification, string RiskLevel, string RecommendedAction, string Rationale, decimal Confidence, string SourceWorkflow);
public sealed record FinanceActionPermissionsDto(bool CanChangeTransactionCategory, bool CanChangeInvoiceApprovalStatus, bool CanManagePolicies)
{
    [JsonPropertyName("canEditTransactionCategory")]
    public bool CanChangeTransactionCategory { get; set; } = CanChangeTransactionCategory;
    public bool CanChangeInvoiceApprovalStatus { get; set; } = CanChangeInvoiceApprovalStatus;

    [JsonPropertyName("canManagePolicyConfiguration")]
    public bool CanManagePolicies { get; set; } = CanManagePolicies;

    public FinanceActionPermissionsDto() : this(default !, default !, default !)
    {
    }
}

public sealed record FinanceLinkedDocumentAccessDto(string AccessState, string Message, bool CanOpen, FinanceLinkedDocumentDto? Document)
{
    [JsonPropertyName("availability")]
    public string AccessState { get; set; } = AccessState;
    public string Message { get; set; } = Message;

    [JsonPropertyName("canNavigate")]
    public bool CanOpen { get; set; } = CanOpen;
    public FinanceLinkedDocumentDto? Document { get; set; } = Document;

    public FinanceLinkedDocumentAccessDto() : this(default !, string.Empty, default !, default !)
    {
    }
}

public sealed record FinanceTransactionDto(Guid Id, Guid AccountId, string AccountName, Guid? CounterpartyId, string? CounterpartyName, Guid? InvoiceId, Guid? BillId, DateTime TransactionUtc, string TransactionType, decimal Amount, string Currency, string Description, string ExternalReference, FinanceLinkedDocumentDto? LinkedDocument, bool IsFlagged = false, string AnomalyState = "clear", string Source = "simulation")
{
    public Guid Id { get; set; } = Id;
    public Guid AccountId { get; set; } = AccountId;
    public string AccountName { get; set; } = AccountName;
    public Guid? CounterpartyId { get; set; } = CounterpartyId;
    public string? CounterpartyName { get; set; } = CounterpartyName;
    public Guid? InvoiceId { get; set; } = InvoiceId;
    public Guid? BillId { get; set; } = BillId;
    public DateTime TransactionUtc { get; set; } = TransactionUtc;
    public string TransactionType { get; set; } = TransactionType;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Description { get; set; } = Description;
    public string ExternalReference { get; set; } = ExternalReference;
    public FinanceLinkedDocumentDto? LinkedDocument { get; set; } = LinkedDocument;
    public bool IsFlagged { get; set; } = IsFlagged;
    public string AnomalyState { get; set; } = AnomalyState;
    public string Source { get; set; } = Source;

    public FinanceTransactionDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty)
    {
    }
}

public sealed record FinanceTransactionDetailDto(Guid Id, Guid AccountId, string AccountName, Guid? CounterpartyId, string? CounterpartyName, Guid? InvoiceId, Guid? BillId, DateTime TransactionUtc, string Category, decimal Amount, string Currency, string Description, string ExternalReference, bool IsFlagged, string AnomalyState, IReadOnlyList<string> Flags, FinanceActionPermissionsDto Permissions, FinanceLinkedDocumentAccessDto LinkedDocument, FinanceTransactionPaymentContextDto? PaymentContext = null, string Source = "manual")
{
    public Guid Id { get; set; } = Id;
    public Guid AccountId { get; set; } = AccountId;
    public string AccountName { get; set; } = AccountName;
    public Guid? CounterpartyId { get; set; } = CounterpartyId;
    public string? CounterpartyName { get; set; } = CounterpartyName;
    public Guid? InvoiceId { get; set; } = InvoiceId;
    public Guid? BillId { get; set; } = BillId;
    public DateTime TransactionUtc { get; set; } = TransactionUtc;
    public string Category { get; set; } = Category;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Description { get; set; } = Description;
    public string ExternalReference { get; set; } = ExternalReference;
    public bool IsFlagged { get; set; } = IsFlagged;
    public string AnomalyState { get; set; } = AnomalyState;
    public IReadOnlyList<string> Flags { get; set; } = Flags;
    public FinanceActionPermissionsDto Permissions { get; set; } = Permissions;
    public FinanceLinkedDocumentAccessDto LinkedDocument { get; set; } = LinkedDocument;
    public FinanceTransactionPaymentContextDto? PaymentContext { get; set; } = PaymentContext;
    public string Source { get; set; } = Source;

    public FinanceTransactionDetailDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, [], new(), new(), default !, string.Empty)
    {
    }
}

public sealed record FinanceDataResetResultDto(Guid CompanyId, int TotalDeleted, IReadOnlyDictionary<string, int> DeletedCounts)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public int TotalDeleted { get; set; } = TotalDeleted;
    public IReadOnlyDictionary<string, int> DeletedCounts { get; set; } = DeletedCounts;

    public FinanceDataResetResultDto() : this(default !, default !, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
    {
    }
}
