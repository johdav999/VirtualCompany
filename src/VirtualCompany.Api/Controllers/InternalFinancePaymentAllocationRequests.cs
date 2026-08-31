using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed record CreateFinancePaymentAllocationRequest(
    Guid? InvoiceId,
    Guid? BillId,
    decimal AllocatedAmount,
    string Currency,
    string? IdempotencyKey = null,
    decimal FeeAmount = 0m,
    decimal WriteOffAmount = 0m)
{
    public CreateFinancePaymentAllocationDto ToDto(Guid paymentId) =>
        new(paymentId, InvoiceId, BillId, AllocatedAmount, Currency, IdempotencyKey, FeeAmount, WriteOffAmount);
}

public sealed record ReverseFinancePaymentAllocationRequest(
    DateOnly PostingDate,
    string Reason,
    string IdempotencyKey);
