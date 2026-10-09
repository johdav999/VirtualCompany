using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ReconcileBankTransactionRequest(IReadOnlyList<ReconcileBankTransactionPaymentRequest> Payments, long ExpectedSourceVersion = 1, string HandlingMode = "payment", string? ReviewReason = null, Guid? CategorizationFinanceAccountId = null, IReadOnlyList<BankReconciliationAdjustmentDto>? Adjustments = null, string? IdempotencyKey = null)
{
    public IReadOnlyList<ReconcileBankTransactionPaymentRequest> Payments { get; set; } = Payments;
    public long ExpectedSourceVersion { get; set; } = ExpectedSourceVersion;
    public string HandlingMode { get; set; } = HandlingMode;
    public string? ReviewReason { get; set; } = ReviewReason;
    public Guid? CategorizationFinanceAccountId { get; set; } = CategorizationFinanceAccountId;
    public IReadOnlyList<BankReconciliationAdjustmentDto>? Adjustments { get; set; } = Adjustments;
    public string? IdempotencyKey { get; set; } = IdempotencyKey;

    public ReconcileBankTransactionRequest() : this([], 1, "payment", default !, default !, [], default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ReconcileBankTransactionPaymentRequest(Guid PaymentId, decimal AllocatedAmount)
{
    public Guid PaymentId { get; set; } = PaymentId;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;

    public ReconcileBankTransactionPaymentRequest() : this(default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ReclassifyBankSuspenseRequest(Guid TargetFinanceAccountId, Guid FiscalPeriodId, DateOnly PostingDate, string Reason, long ExpectedSourceVersion, string IdempotencyKey)
{
    public Guid TargetFinanceAccountId { get; set; } = TargetFinanceAccountId;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public string Reason { get; set; } = Reason;
    public long ExpectedSourceVersion { get; set; } = ExpectedSourceVersion;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public ReclassifyBankSuspenseRequest() : this(default !, default !, default !, string.Empty, default !, string.Empty)
    {
    }
}
