using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record FinanceHistoricalReceivablePaymentDto(
    Guid InvoiceId,
    string CustomerName,
    DateTime DueUtc,
    DateTime PaidUtc,
    decimal InvoiceAmount,
    string Currency,
    Guid? CustomerId = null);

public sealed record SupplierInvoicePaymentProposalDto(
    Guid Id,
    Guid BillId,
    Guid SupplierId,
    string SupplierName,
    decimal Amount,
    string Currency,
    DateTime DueUtc,
    string PaymentReference,
    string Status,
    Guid? TaskId,
    Guid? ApprovalRequestId,
    Guid? RequestedByUserId,
    Guid? DecidedByUserId,
    DateTime? DecidedUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    string ExportMode = "register_payment",
    string ExportStatus = "not_exported",
    string? ExportProviderKey = null,
    Guid? ExportConnectionId = null,
    Guid? ExportRequestedByUserId = null,
    DateTime? ExportRequestedUtc = null,
    DateTime? ExportedUtc = null,
    string? ExportResponseSummary = null);

public sealed record RequestSupplierInvoicePaymentProposalCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName = "Finance user");

public sealed record ExportSupplierInvoicePaymentInstructionCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string ExportMode = "register_payment",
    string ProviderKey = FinanceIntegrationProviderKeys.Fortnox);

public interface IFinanceSupplierPaymentProposalService
{
    Task<SupplierInvoicePaymentProposalDto> RequestPaymentProposalAsync(
        RequestSupplierInvoicePaymentProposalCommand command,
        CancellationToken cancellationToken);

    Task<SupplierInvoicePaymentProposalDto> ExportPaymentInstructionAsync(
        ExportSupplierInvoicePaymentInstructionCommand command,
        CancellationToken cancellationToken);
}

public sealed record FinanceTransactionPaymentContextDto(
    bool IsPartiallyPaid,
    decimal PaidAmount,
    decimal TotalAmount,
    decimal RemainingAmount,
    string Currency);

