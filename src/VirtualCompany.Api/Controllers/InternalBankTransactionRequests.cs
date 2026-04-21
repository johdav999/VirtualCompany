using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed record ReconcileBankTransactionPaymentRequest(
    Guid PaymentId,
    decimal AllocatedAmount)
{
    public BankTransactionPaymentMatchDto ToDto() => new(PaymentId, AllocatedAmount);
}

public sealed record ReconcileBankTransactionRequest(
    IReadOnlyList<ReconcileBankTransactionPaymentRequest> Payments)
{
    public ReconcileBankTransactionCommand ToCommand(Guid companyId, Guid bankTransactionId) =>
        new(
            companyId,
            bankTransactionId,
            Payments?.Select(x => x.ToDto()).ToArray() ?? []);
}