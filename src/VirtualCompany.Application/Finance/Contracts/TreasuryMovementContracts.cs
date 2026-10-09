namespace VirtualCompany.Application.Finance;

public static class TreasuryMovementReasonCodes
{
    public const string CrossCurrencyTransferBlocked = "treasury_cross_currency_transfer_blocked";
    public const string TransferLegMissing = "treasury_transfer_leg_missing";
    public const string TransferEvidenceMissing = "treasury_transfer_evidence_missing";
    public const string BankEvidenceMissing = "treasury_bank_evidence_missing";
    public const string BankAmountMismatch = "treasury_bank_amount_mismatch";
    public const string ApprovalRequired = "treasury_approval_required";
    public const string SourceVersionConflict = "treasury_source_version_conflict";
    public const string SourceIdentityConflict = "treasury_source_identity_conflict";
    public const string InvalidLifecycleState = "treasury_invalid_lifecycle_state";
    public const string UnsupportedSourceType = "treasury_source_type_unsupported";
    public const string InvalidAccountingPolicy = "treasury_accounting_policy_invalid";
    public const string BankTransactionAlreadyLinked = "treasury_bank_transaction_already_linked";
}

public sealed record CreateTreasuryTransferCommand(Guid CompanyId, string SourceIdentity,
    Guid FromBankAccountId, Guid ToBankAccountId, decimal Amount, decimal FeeAmount, string Currency,
    Guid? FeeFinanceAccountId, decimal MaterialityThreshold, Guid? CorrectionOfTransferId,
    Guid? OutboundBankTransactionId, Guid? InboundBankTransactionId,
    IReadOnlyList<TreasuryEvidenceInputDto> Evidence, Guid ActorUserId, string? CorrelationId = null);

public sealed record CreateBankAdjustmentCommand(Guid CompanyId, string SourceIdentity, string AdjustmentKind,
    Guid BankAccountId, Guid BankTransactionId, Guid CounterpartFinanceAccountId, decimal Amount,
    string Currency, string Description, decimal MaterialityThreshold, Guid? CorrectionOfAdjustmentId,
    IReadOnlyList<TreasuryEvidenceInputDto> Evidence, Guid ActorUserId, string? CorrelationId = null);

public sealed record CreateCardSettlementCommand(Guid CompanyId, string SourceIdentity, string ProviderBatchReference,
    Guid BankAccountId, Guid ReceivableFinanceAccountId, decimal GrossAmount, decimal FeeAmount,
    decimal NetAmount, string Currency, decimal MaterialityThreshold, Guid? CorrectionOfSettlementId,
    Guid? BankTransactionId, IReadOnlyList<TreasuryEvidenceInputDto> Evidence, Guid ActorUserId,
    string? CorrelationId = null);

public sealed record CreatePayoutSettlementCommand(Guid CompanyId, string SourceIdentity, string ProviderBatchReference,
    Guid BankAccountId, Guid PayoutClearingFinanceAccountId, decimal GrossAmount, decimal FeeAmount,
    decimal NetAmount, string Currency, decimal MaterialityThreshold, Guid? CorrectionOfSettlementId,
    Guid? BankTransactionId, IReadOnlyList<TreasuryEvidenceInputDto> Evidence, Guid ActorUserId,
    string? CorrelationId = null);

public sealed record LinkTreasuryBankEvidenceCommand(Guid CompanyId, string SourceType, Guid SourceId,
    Guid BankTransactionId, string? TransferLegRole, long ExpectedVersion, Guid ActorUserId,
    string? CorrelationId = null);

public sealed record BindTreasuryApprovalCommand(Guid CompanyId, string SourceType, Guid SourceId,
    Guid ApprovalRequestId, long ExpectedVersion, Guid ActorUserId, string? CorrelationId = null);

public sealed record PreviewTreasuryPostingCommand(Guid CompanyId, string SourceType, Guid SourceId,
    Guid FiscalPeriodId, DateOnly PostingDate, Guid ActorUserId);

public sealed record PostTreasurySourceCommand(Guid CompanyId, string SourceType, Guid SourceId,
    Guid FiscalPeriodId, DateOnly PostingDate, long ExpectedVersion, Guid ActorUserId,
    string? CorrelationId = null);

public sealed record ReverseTreasurySourceCommand(Guid CompanyId, string SourceType, Guid SourceId,
    Guid FiscalPeriodId, DateOnly PostingDate, long ExpectedVersion, string Reason, Guid ActorUserId,
    string? CorrelationId = null);

public sealed record ListTreasurySourcesQuery(Guid CompanyId, string? Status = null,
    Guid? BankTransactionId = null, int Limit = 100);
public sealed record GetTreasurySourceQuery(Guid CompanyId, string SourceType, Guid SourceId);

public interface ITreasuryMovementReadService
{
    Task<TreasurySourceListDto> ListAsync(ListTreasurySourcesQuery query, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto?> GetAsync(GetTreasurySourceQuery query, CancellationToken cancellationToken);
}

public interface ITreasuryMovementCommandService
{
    Task<TreasurySourceDetailDto> CreateTransferAsync(CreateTreasuryTransferCommand command, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto> CreateBankAdjustmentAsync(CreateBankAdjustmentCommand command, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto> CreateCardSettlementAsync(CreateCardSettlementCommand command, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto> CreatePayoutSettlementAsync(CreatePayoutSettlementCommand command, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto> LinkBankEvidenceAsync(LinkTreasuryBankEvidenceCommand command, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto> BindApprovalAsync(BindTreasuryApprovalCommand command, CancellationToken cancellationToken);
    Task<TreasuryPostingPreviewDto> PreviewAsync(PreviewTreasuryPostingCommand command, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto> PostAsync(PostTreasurySourceCommand command, CancellationToken cancellationToken);
    Task<TreasurySourceDetailDto> ReverseAsync(ReverseTreasurySourceCommand command, CancellationToken cancellationToken);
}

public sealed class TreasuryMovementException : Exception
{
    public TreasuryMovementException(string reasonCode, string message, bool isConflict = false) : base(message)
    { ReasonCode = reasonCode; IsConflict = isConflict; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
