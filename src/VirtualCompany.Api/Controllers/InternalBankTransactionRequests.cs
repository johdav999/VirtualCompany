using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;
public sealed record ImportBankStatementRequest(Guid BankAccountId, string SourceKey, string StatementIdentity, string ContentHash, IReadOnlyList<ImportBankStatementRowDto> Rows);
public static class ReconcileBankTransactionRequestMapping
{
    public static ReconcileBankTransactionCommand ToCommand(this ReconcileBankTransactionRequest request, Guid companyId, Guid bankTransactionId, Guid actorUserId, string? correlationId) => new(companyId, bankTransactionId, request.Payments?.Select(x => x.ToDto()).ToArray() ?? [], actorUserId, request.ExpectedSourceVersion, request.HandlingMode, request.ReviewReason, request.CategorizationFinanceAccountId, request.Adjustments, request.IdempotencyKey, correlationId);
}

public static class ReconcileBankTransactionPaymentRequestMapping
{
    public static BankTransactionPaymentMatchDto ToDto(this ReconcileBankTransactionPaymentRequest request) => new(request.PaymentId, request.AllocatedAmount);
}
