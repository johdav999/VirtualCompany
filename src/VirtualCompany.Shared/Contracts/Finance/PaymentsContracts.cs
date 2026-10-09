using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record SupplierInvoicePaymentProposalDto(Guid Id, Guid BillId, Guid SupplierId, string SupplierName, decimal Amount, string Currency, DateTime DueUtc, string PaymentReference, string Status, Guid? TaskId, Guid? ApprovalRequestId, Guid? RequestedByUserId, Guid? DecidedByUserId, DateTime? DecidedUtc, DateTime CreatedUtc, DateTime UpdatedUtc, string ExportMode = "register_payment", string ExportStatus = "not_exported", string? ExportProviderKey = null, Guid? ExportConnectionId = null, Guid? ExportRequestedByUserId = null, DateTime? ExportRequestedUtc = null, DateTime? ExportedUtc = null, string? ExportResponseSummary = null)
{
    public Guid Id { get; set; } = Id;
    public Guid BillId { get; set; } = BillId;
    public Guid SupplierId { get; set; } = SupplierId;
    public string SupplierName { get; set; } = SupplierName;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public DateTime DueUtc { get; set; } = DueUtc;
    public string PaymentReference { get; set; } = PaymentReference;
    public string Status { get; set; } = Status;
    public Guid? TaskId { get; set; } = TaskId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public Guid? RequestedByUserId { get; set; } = RequestedByUserId;
    public Guid? DecidedByUserId { get; set; } = DecidedByUserId;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public string ExportMode { get; set; } = ExportMode;
    public string ExportStatus { get; set; } = ExportStatus;
    public string? ExportProviderKey { get; set; } = ExportProviderKey;
    public Guid? ExportConnectionId { get; set; } = ExportConnectionId;
    public Guid? ExportRequestedByUserId { get; set; } = ExportRequestedByUserId;
    public DateTime? ExportRequestedUtc { get; set; } = ExportRequestedUtc;
    public DateTime? ExportedUtc { get; set; } = ExportedUtc;
    public string? ExportResponseSummary { get; set; } = ExportResponseSummary;

    public SupplierInvoicePaymentProposalDto() : this(default !, default !, default !, string.Empty, default !, string.Empty, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, "register_payment", "not_exported", default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceTransactionPaymentContextDto(bool IsPartiallyPaid, decimal PaidAmount, decimal TotalAmount, decimal RemainingAmount, string Currency)
{
    public bool IsPartiallyPaid { get; set; } = IsPartiallyPaid;
    public decimal PaidAmount { get; set; } = PaidAmount;
    public decimal TotalAmount { get; set; } = TotalAmount;
    public decimal RemainingAmount { get; set; } = RemainingAmount;
    public string Currency { get; set; } = Currency;

    public FinanceTransactionPaymentContextDto() : this(default !, default !, default !, default !, string.Empty)
    {
    }
}
