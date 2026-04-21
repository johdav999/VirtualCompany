using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed record CreateFinancePaymentAllocationRequest(
    Guid? InvoiceId,
    Guid? BillId,
    decimal AllocatedAmount,
    string Currency)
{
    public CreateFinancePaymentAllocationDto ToDto(Guid paymentId) =>
        new(paymentId, InvoiceId, BillId, AllocatedAmount, Currency);
}