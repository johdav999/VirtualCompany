using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed record ReconcileBankTransactionPaymentRequest(
    Guid PaymentId,
    decimal AllocatedAmount)
{
    public BankTransactionPaymentMatchDto ToDto() => new(PaymentId, AllocatedAmount);
}

public sealed record ReconcileBankTransactionRequest(
    IReadOnlyList<ReconcileBankTransactionPaymentRequest> Payments,
    long ExpectedSourceVersion = 1,
    string HandlingMode = BankReconciliationHandlingModes.Payment,
    string? ReviewReason = null,
    Guid? CategorizationFinanceAccountId = null,
    IReadOnlyList<BankReconciliationAdjustmentDto>? Adjustments = null,
    string? IdempotencyKey = null)
{
    public ReconcileBankTransactionCommand ToCommand(Guid companyId, Guid bankTransactionId, Guid actorUserId, string? correlationId) =>
        new(
            companyId,
            bankTransactionId,
            Payments?.Select(x => x.ToDto()).ToArray() ?? [],
            actorUserId,
            ExpectedSourceVersion,
            HandlingMode,
            ReviewReason,
            CategorizationFinanceAccountId,
            Adjustments,
            IdempotencyKey,
            correlationId);
}

public sealed record ImportBankStatementRequest(
    Guid BankAccountId,
    string SourceKey,
    string StatementIdentity,
    string ContentHash,
    IReadOnlyList<ImportBankStatementRowDto> Rows);

public sealed record ReclassifyBankSuspenseRequest(
    Guid TargetFinanceAccountId,
    Guid FiscalPeriodId,
    DateOnly PostingDate,
    string Reason,
    long ExpectedSourceVersion,
    string IdempotencyKey);
