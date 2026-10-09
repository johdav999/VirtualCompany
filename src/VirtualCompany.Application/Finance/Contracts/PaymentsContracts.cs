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

