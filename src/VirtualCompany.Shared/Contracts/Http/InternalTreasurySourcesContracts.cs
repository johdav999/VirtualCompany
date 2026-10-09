using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CreateTreasuryTransferRequest(string SourceIdentity, Guid FromBankAccountId, Guid ToBankAccountId, decimal Amount, decimal FeeAmount, string Currency, Guid? FeeFinanceAccountId, decimal MaterialityThreshold, Guid? CorrectionOfTransferId, Guid? OutboundBankTransactionId, Guid? InboundBankTransactionId, IReadOnlyList<TreasuryEvidenceInputDto>? Evidence)
{
    public string SourceIdentity { get; set; } = SourceIdentity;
    public Guid FromBankAccountId { get; set; } = FromBankAccountId;
    public Guid ToBankAccountId { get; set; } = ToBankAccountId;
    public decimal Amount { get; set; } = Amount;
    public decimal FeeAmount { get; set; } = FeeAmount;
    public string Currency { get; set; } = Currency;
    public Guid? FeeFinanceAccountId { get; set; } = FeeFinanceAccountId;
    public decimal MaterialityThreshold { get; set; } = MaterialityThreshold;
    public Guid? CorrectionOfTransferId { get; set; } = CorrectionOfTransferId;
    public Guid? OutboundBankTransactionId { get; set; } = OutboundBankTransactionId;
    public Guid? InboundBankTransactionId { get; set; } = InboundBankTransactionId;
    public IReadOnlyList<TreasuryEvidenceInputDto>? Evidence { get; set; } = Evidence;

    public CreateTreasuryTransferRequest() : this(string.Empty, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, [])
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CreateBankAdjustmentRequest(string SourceIdentity, string AdjustmentKind, Guid BankAccountId, Guid BankTransactionId, Guid CounterpartFinanceAccountId, decimal Amount, string Currency, string Description, decimal MaterialityThreshold, Guid? CorrectionOfAdjustmentId, IReadOnlyList<TreasuryEvidenceInputDto>? Evidence)
{
    public string SourceIdentity { get; set; } = SourceIdentity;
    public string AdjustmentKind { get; set; } = AdjustmentKind;
    public Guid BankAccountId { get; set; } = BankAccountId;
    public Guid BankTransactionId { get; set; } = BankTransactionId;
    public Guid CounterpartFinanceAccountId { get; set; } = CounterpartFinanceAccountId;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string Description { get; set; } = Description;
    public decimal MaterialityThreshold { get; set; } = MaterialityThreshold;
    public Guid? CorrectionOfAdjustmentId { get; set; } = CorrectionOfAdjustmentId;
    public IReadOnlyList<TreasuryEvidenceInputDto>? Evidence { get; set; } = Evidence;

    public CreateBankAdjustmentRequest() : this(string.Empty, string.Empty, default !, default !, default !, default !, string.Empty, string.Empty, default !, default !, [])
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CreateCardSettlementRequest(string SourceIdentity, string ProviderBatchReference, Guid BankAccountId, Guid ReceivableFinanceAccountId, decimal GrossAmount, decimal FeeAmount, decimal NetAmount, string Currency, decimal MaterialityThreshold, Guid? CorrectionOfSettlementId, Guid? BankTransactionId, IReadOnlyList<TreasuryEvidenceInputDto>? Evidence)
{
    public string SourceIdentity { get; set; } = SourceIdentity;
    public string ProviderBatchReference { get; set; } = ProviderBatchReference;
    public Guid BankAccountId { get; set; } = BankAccountId;
    public Guid ReceivableFinanceAccountId { get; set; } = ReceivableFinanceAccountId;
    public decimal GrossAmount { get; set; } = GrossAmount;
    public decimal FeeAmount { get; set; } = FeeAmount;
    public decimal NetAmount { get; set; } = NetAmount;
    public string Currency { get; set; } = Currency;
    public decimal MaterialityThreshold { get; set; } = MaterialityThreshold;
    public Guid? CorrectionOfSettlementId { get; set; } = CorrectionOfSettlementId;
    public Guid? BankTransactionId { get; set; } = BankTransactionId;
    public IReadOnlyList<TreasuryEvidenceInputDto>? Evidence { get; set; } = Evidence;

    public CreateCardSettlementRequest() : this(string.Empty, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, [])
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CreatePayoutSettlementRequest(string SourceIdentity, string ProviderBatchReference, Guid BankAccountId, Guid PayoutClearingFinanceAccountId, decimal GrossAmount, decimal FeeAmount, decimal NetAmount, string Currency, decimal MaterialityThreshold, Guid? CorrectionOfSettlementId, Guid? BankTransactionId, IReadOnlyList<TreasuryEvidenceInputDto>? Evidence)
{
    public string SourceIdentity { get; set; } = SourceIdentity;
    public string ProviderBatchReference { get; set; } = ProviderBatchReference;
    public Guid BankAccountId { get; set; } = BankAccountId;
    public Guid PayoutClearingFinanceAccountId { get; set; } = PayoutClearingFinanceAccountId;
    public decimal GrossAmount { get; set; } = GrossAmount;
    public decimal FeeAmount { get; set; } = FeeAmount;
    public decimal NetAmount { get; set; } = NetAmount;
    public string Currency { get; set; } = Currency;
    public decimal MaterialityThreshold { get; set; } = MaterialityThreshold;
    public Guid? CorrectionOfSettlementId { get; set; } = CorrectionOfSettlementId;
    public Guid? BankTransactionId { get; set; } = BankTransactionId;
    public IReadOnlyList<TreasuryEvidenceInputDto>? Evidence { get; set; } = Evidence;

    public CreatePayoutSettlementRequest() : this(string.Empty, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, [])
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record LinkTreasuryBankEvidenceRequest(Guid BankTransactionId, string? TransferLegRole, long ExpectedVersion)
{
    public Guid BankTransactionId { get; set; } = BankTransactionId;
    public string? TransferLegRole { get; set; } = TransferLegRole;
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public LinkTreasuryBankEvidenceRequest() : this(default !, default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record BindTreasuryApprovalRequest(Guid ApprovalRequestId, long ExpectedVersion)
{
    public Guid ApprovalRequestId { get; set; } = ApprovalRequestId;
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public BindTreasuryApprovalRequest() : this(default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record PreviewTreasuryPostingRequest(Guid FiscalPeriodId, DateOnly PostingDate)
{
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public DateOnly PostingDate { get; set; } = PostingDate;

    public PreviewTreasuryPostingRequest() : this(default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record PostTreasurySourceRequest(Guid FiscalPeriodId, DateOnly PostingDate, long ExpectedVersion)
{
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public PostTreasurySourceRequest() : this(default !, default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ReverseTreasurySourceRequest(Guid FiscalPeriodId, DateOnly PostingDate, long ExpectedVersion, string Reason)
{
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string Reason { get; set; } = Reason;

    public ReverseTreasurySourceRequest() : this(default !, default !, default !, string.Empty)
    {
    }
}
