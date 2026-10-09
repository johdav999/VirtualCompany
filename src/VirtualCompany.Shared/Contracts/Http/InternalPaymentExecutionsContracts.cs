using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record QueuePaymentExecutionRequest(long ExpectedBatchVersion, Guid BankConnectionId, Guid CompanyBankAccountId, string IdempotencyKey)
{
    public long ExpectedBatchVersion { get; set; } = ExpectedBatchVersion;
    public Guid BankConnectionId { get; set; } = BankConnectionId;
    public Guid CompanyBankAccountId { get; set; } = CompanyBankAccountId;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public QueuePaymentExecutionRequest() : this(default !, default !, default !, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CancelPaymentExecutionRequest(long ExpectedVersion, string Reason, string IdempotencyKey)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string Reason { get; set; } = Reason;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public CancelPaymentExecutionRequest() : this(default !, string.Empty, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ReconcilePaymentExecutionRequest(long ExpectedVersion, string? ProviderPaymentId, string Reason, string IdempotencyKey)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string? ProviderPaymentId { get; set; } = ProviderPaymentId;
    public string Reason { get; set; } = Reason;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public ReconcilePaymentExecutionRequest() : this(default !, default !, string.Empty, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record SettlePaymentExecutionRequest(long ExpectedVersion, Guid BankTransactionId, long ExpectedBankTransactionSourceVersion, string IdempotencyKey)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public Guid BankTransactionId { get; set; } = BankTransactionId;
    public long ExpectedBankTransactionSourceVersion { get; set; } = ExpectedBankTransactionSourceVersion;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public SettlePaymentExecutionRequest() : this(default !, default !, default !, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record RetryPaymentRemittanceRequest(long ExpectedExecutionVersion, string IdempotencyKey)
{
    public long ExpectedExecutionVersion { get; set; } = ExpectedExecutionVersion;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public RetryPaymentRemittanceRequest() : this(default !, string.Empty)
    {
    }
}
