using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class TreasuryTransfer : ICompanyOwnedEntity
{
    private TreasuryTransfer() { }

    public TreasuryTransfer(Guid id, Guid companyId, string sourceIdentity, Guid fromBankAccountId,
        Guid toBankAccountId, decimal amount, decimal feeAmount, string currency, Guid? feeFinanceAccountId,
        decimal materialityThreshold, Guid? correctionOfTransferId, Guid actorUserId, DateTime createdUtc)
    {
        Id = TreasuryValues.Id(id);
        CompanyId = TreasuryValues.Required(companyId, nameof(companyId));
        SourceIdentity = TreasuryValues.Text(sourceIdentity, nameof(sourceIdentity), 200);
        FromBankAccountId = TreasuryValues.Required(fromBankAccountId, nameof(fromBankAccountId));
        ToBankAccountId = TreasuryValues.Required(toBankAccountId, nameof(toBankAccountId));
        if (FromBankAccountId == ToBankAccountId) throw new ArgumentException("Transfer accounts must be different.");
        Amount = TreasuryValues.PositiveMoney(amount, nameof(amount));
        FeeAmount = TreasuryValues.NonNegativeMoney(feeAmount, nameof(feeAmount));
        Currency = TreasuryValues.Currency(currency);
        FeeFinanceAccountId = TreasuryValues.OptionalId(feeFinanceAccountId, nameof(feeFinanceAccountId));
        if (FeeAmount > 0m && !FeeFinanceAccountId.HasValue)
            throw new ArgumentException("A fee account is required when a transfer fee is present.", nameof(feeFinanceAccountId));
        MaterialityThreshold = TreasuryValues.NonNegativeMoney(materialityThreshold, nameof(materialityThreshold));
        RequiresApproval = MaterialityThreshold > 0m && Amount + FeeAmount >= MaterialityThreshold;
        CorrectionOfTransferId = TreasuryValues.OptionalId(correctionOfTransferId, nameof(correctionOfTransferId));
        CreatedByUserId = UpdatedByUserId = TreasuryValues.Required(actorUserId, nameof(actorUserId));
        CreatedUtc = UpdatedUtc = TreasuryValues.Timestamp(createdUtc, nameof(createdUtc));
        Status = TreasuryMovementStatuses.NeedsReview;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string SourceIdentity { get; private set; } = null!;
    public Guid FromBankAccountId { get; private set; }
    public Guid ToBankAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal FeeAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public Guid? FeeFinanceAccountId { get; private set; }
    public decimal MaterialityThreshold { get; private set; }
    public bool RequiresApproval { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? OutboundBankTransactionId { get; private set; }
    public Guid? InboundBankTransactionId { get; private set; }
    public Guid? CorrectionOfTransferId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? ReasonCode { get; private set; }
    public long Version { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? PostedUtc { get; private set; }
    public DateTime? ReversedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void AttachBankLeg(long expectedVersion, string legRole, Guid bankTransactionId, Guid actorUserId,
        DateTime updatedUtc)
    {
        EnsureMutable(expectedVersion);
        var normalizedRole = TreasuryTransferLegRoles.Normalize(legRole);
        bankTransactionId = TreasuryValues.Required(bankTransactionId, nameof(bankTransactionId));
        if (normalizedRole == TreasuryTransferLegRoles.Outbound)
        {
            if (OutboundBankTransactionId.HasValue && OutboundBankTransactionId != bankTransactionId)
                throw new InvalidOperationException("The outbound transfer leg is already linked to different evidence.");
            OutboundBankTransactionId = bankTransactionId;
        }
        else
        {
            if (InboundBankTransactionId.HasValue && InboundBankTransactionId != bankTransactionId)
                throw new InvalidOperationException("The inbound transfer leg is already linked to different evidence.");
            InboundBankTransactionId = bankTransactionId;
        }
        RefreshStatus();
        Touch(actorUserId, updatedUtc);
    }

    public void BindApproval(long expectedVersion, Guid approvalRequestId, Guid actorUserId, DateTime updatedUtc)
    {
        EnsureMutable(expectedVersion);
        ApprovalRequestId = TreasuryValues.Required(approvalRequestId, nameof(approvalRequestId));
        RefreshStatus();
        Touch(actorUserId, updatedUtc);
    }

    public void MarkPosted(long expectedVersion, Guid actorUserId, DateTime postedUtc)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The treasury source changed after it was reviewed.");
        if (Status != TreasuryMovementStatuses.ReadyToPost)
            throw new InvalidOperationException("The transfer is not ready to post.");
        Status = TreasuryMovementStatuses.Posted;
        ReasonCode = null;
        PostedUtc = TreasuryValues.Timestamp(postedUtc, nameof(postedUtc));
        Touch(actorUserId, PostedUtc.Value);
    }

    public void MarkReversed(long expectedVersion, Guid actorUserId, DateTime reversedUtc)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The treasury source changed after it was reviewed.");
        if (Status != TreasuryMovementStatuses.Posted) throw new InvalidOperationException("Only a posted transfer can be reversed.");
        Status = TreasuryMovementStatuses.Reversed;
        ReversedUtc = TreasuryValues.Timestamp(reversedUtc, nameof(reversedUtc));
        Touch(actorUserId, ReversedUtc.Value);
    }

    private void RefreshStatus()
    {
        if (OutboundBankTransactionId.HasValue && InboundBankTransactionId.HasValue)
        {
            Status = RequiresApproval && !ApprovalRequestId.HasValue
                ? TreasuryMovementStatuses.AwaitingApproval
                : TreasuryMovementStatuses.ReadyToPost;
            ReasonCode = RequiresApproval && !ApprovalRequestId.HasValue ? "treasury_approval_required" : null;
        }
        else if (OutboundBankTransactionId.HasValue || InboundBankTransactionId.HasValue)
        {
            Status = TreasuryMovementStatuses.InTransit;
            ReasonCode = "treasury_transfer_leg_missing";
        }
        else
        {
            Status = TreasuryMovementStatuses.NeedsReview;
            ReasonCode = "treasury_transfer_evidence_missing";
        }
    }

    private void EnsureMutable(long expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The treasury source changed after it was reviewed.");
        if (Status is TreasuryMovementStatuses.Posted or TreasuryMovementStatuses.Reversed)
            throw new InvalidOperationException("Posted treasury evidence cannot be changed. Create a correction instead.");
    }

    private void Touch(Guid actorUserId, DateTime updatedUtc)
    {
        UpdatedByUserId = TreasuryValues.Required(actorUserId, nameof(actorUserId));
        UpdatedUtc = TreasuryValues.Timestamp(updatedUtc, nameof(updatedUtc));
        Version++;
    }
}

public sealed class BankAdjustment : ICompanyOwnedEntity
{
    private BankAdjustment() { }

    public BankAdjustment(Guid id, Guid companyId, string sourceIdentity, string adjustmentKind,
        Guid bankAccountId, Guid bankTransactionId, Guid counterpartFinanceAccountId, decimal amount,
        string currency, string description, decimal materialityThreshold, Guid? correctionOfAdjustmentId,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = TreasuryValues.Id(id); CompanyId = TreasuryValues.Required(companyId, nameof(companyId));
        SourceIdentity = TreasuryValues.Text(sourceIdentity, nameof(sourceIdentity), 200);
        AdjustmentKind = BankAdjustmentKinds.Normalize(adjustmentKind);
        if (!BankAdjustmentKinds.IsSupported(AdjustmentKind)) throw new ArgumentOutOfRangeException(nameof(adjustmentKind));
        BankAccountId = TreasuryValues.Required(bankAccountId, nameof(bankAccountId));
        BankTransactionId = TreasuryValues.Required(bankTransactionId, nameof(bankTransactionId));
        CounterpartFinanceAccountId = TreasuryValues.Required(counterpartFinanceAccountId, nameof(counterpartFinanceAccountId));
        Amount = TreasuryValues.PositiveMoney(amount, nameof(amount)); Currency = TreasuryValues.Currency(currency);
        Description = TreasuryValues.Text(description, nameof(description), 500);
        MaterialityThreshold = TreasuryValues.NonNegativeMoney(materialityThreshold, nameof(materialityThreshold));
        RequiresApproval = MaterialityThreshold > 0m && Amount >= MaterialityThreshold;
        CorrectionOfAdjustmentId = TreasuryValues.OptionalId(correctionOfAdjustmentId, nameof(correctionOfAdjustmentId));
        CreatedByUserId = UpdatedByUserId = TreasuryValues.Required(actorUserId, nameof(actorUserId));
        CreatedUtc = UpdatedUtc = TreasuryValues.Timestamp(createdUtc, nameof(createdUtc));
        Status = RequiresApproval ? TreasuryMovementStatuses.AwaitingApproval : TreasuryMovementStatuses.ReadyToPost;
        ReasonCode = RequiresApproval ? "treasury_approval_required" : null; Version = 1;
    }

    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public string SourceIdentity { get; private set; } = null!; public string AdjustmentKind { get; private set; } = null!;
    public Guid BankAccountId { get; private set; } public Guid BankTransactionId { get; private set; }
    public Guid CounterpartFinanceAccountId { get; private set; } public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!; public string Description { get; private set; } = null!;
    public decimal MaterialityThreshold { get; private set; } public bool RequiresApproval { get; private set; }
    public Guid? ApprovalRequestId { get; private set; } public Guid? CorrectionOfAdjustmentId { get; private set; }
    public string Status { get; private set; } = null!; public string? ReasonCode { get; private set; }
    public long Version { get; private set; } public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public DateTime? PostedUtc { get; private set; }
    public DateTime? ReversedUtc { get; private set; } public Company Company { get; private set; } = null!;

    public void BindApproval(long expectedVersion, Guid approvalRequestId, Guid actorUserId, DateTime updatedUtc) =>
        TreasuryLifecycle.BindApproval(this, expectedVersion, approvalRequestId, actorUserId, updatedUtc);
    public void MarkPosted(long expectedVersion, Guid actorUserId, DateTime postedUtc) =>
        TreasuryLifecycle.MarkPosted(this, expectedVersion, actorUserId, postedUtc);
    public void MarkReversed(long expectedVersion, Guid actorUserId, DateTime reversedUtc) =>
        TreasuryLifecycle.MarkReversed(this, expectedVersion, actorUserId, reversedUtc);

    internal void ApplyApproval(Guid approvalRequestId, Guid actorUserId, DateTime updatedUtc)
    { ApprovalRequestId = approvalRequestId; Status = TreasuryMovementStatuses.ReadyToPost; ReasonCode = null; Touch(actorUserId, updatedUtc); }
    internal void ApplyPosted(Guid actorUserId, DateTime postedUtc)
    { Status = TreasuryMovementStatuses.Posted; ReasonCode = null; PostedUtc = TreasuryValues.Timestamp(postedUtc, nameof(postedUtc)); Touch(actorUserId, PostedUtc.Value); }
    internal void ApplyReversed(Guid actorUserId, DateTime reversedUtc)
    { Status = TreasuryMovementStatuses.Reversed; ReversedUtc = TreasuryValues.Timestamp(reversedUtc, nameof(reversedUtc)); Touch(actorUserId, ReversedUtc.Value); }
    private void Touch(Guid actor, DateTime time) { UpdatedByUserId = TreasuryValues.Required(actor, nameof(actor)); UpdatedUtc = TreasuryValues.Timestamp(time, nameof(time)); Version++; }
}

public sealed class CardSettlement : ICompanyOwnedEntity
{
    private CardSettlement() { }
    public CardSettlement(Guid id, Guid companyId, string sourceIdentity, string providerBatchReference,
        Guid bankAccountId, Guid receivableFinanceAccountId, decimal grossAmount, decimal feeAmount,
        decimal netAmount, string currency, decimal materialityThreshold, Guid? correctionOfSettlementId,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = TreasuryValues.Id(id); CompanyId = TreasuryValues.Required(companyId, nameof(companyId));
        SourceIdentity = TreasuryValues.Text(sourceIdentity, nameof(sourceIdentity), 200);
        ProviderBatchReference = TreasuryValues.Text(providerBatchReference, nameof(providerBatchReference), 200);
        BankAccountId = TreasuryValues.Required(bankAccountId, nameof(bankAccountId));
        ReceivableFinanceAccountId = TreasuryValues.Required(receivableFinanceAccountId, nameof(receivableFinanceAccountId));
        GrossAmount = TreasuryValues.PositiveMoney(grossAmount, nameof(grossAmount));
        FeeAmount = TreasuryValues.NonNegativeMoney(feeAmount, nameof(feeAmount));
        NetAmount = TreasuryValues.PositiveMoney(netAmount, nameof(netAmount));
        if (GrossAmount != NetAmount + FeeAmount) throw new ArgumentException("Gross amount must equal net amount plus fees.");
        Currency = TreasuryValues.Currency(currency); MaterialityThreshold = TreasuryValues.NonNegativeMoney(materialityThreshold, nameof(materialityThreshold));
        RequiresApproval = MaterialityThreshold > 0m && GrossAmount >= MaterialityThreshold;
        CorrectionOfSettlementId = TreasuryValues.OptionalId(correctionOfSettlementId, nameof(correctionOfSettlementId));
        CreatedByUserId = UpdatedByUserId = TreasuryValues.Required(actorUserId, nameof(actorUserId));
        CreatedUtc = UpdatedUtc = TreasuryValues.Timestamp(createdUtc, nameof(createdUtc));
        Status = TreasuryMovementStatuses.AwaitingBankEvidence; ReasonCode = "treasury_bank_evidence_missing"; Version = 1;
    }

    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SourceIdentity { get; private set; } = null!;
    public string ProviderBatchReference { get; private set; } = null!; public Guid BankAccountId { get; private set; }
    public Guid ReceivableFinanceAccountId { get; private set; } public Guid? BankTransactionId { get; private set; }
    public decimal GrossAmount { get; private set; } public decimal FeeAmount { get; private set; } public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = null!; public decimal MaterialityThreshold { get; private set; }
    public bool RequiresApproval { get; private set; } public Guid? ApprovalRequestId { get; private set; }
    public Guid? CorrectionOfSettlementId { get; private set; } public string Status { get; private set; } = null!;
    public string? ReasonCode { get; private set; } public long Version { get; private set; } public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public DateTime? PostedUtc { get; private set; } public DateTime? ReversedUtc { get; private set; } public Company Company { get; private set; } = null!;

    public void LinkBankEvidence(long expectedVersion, Guid bankTransactionId, bool amountMatches, Guid actorUserId, DateTime updatedUtc) =>
        SettlementLifecycle.LinkBankEvidence(this, expectedVersion, bankTransactionId, amountMatches, actorUserId, updatedUtc);
    public void BindApproval(long expectedVersion, Guid approvalRequestId, Guid actorUserId, DateTime updatedUtc) =>
        SettlementLifecycle.BindApproval(this, expectedVersion, approvalRequestId, actorUserId, updatedUtc);
    public void MarkPosted(long expectedVersion, Guid actorUserId, DateTime postedUtc) =>
        SettlementLifecycle.MarkPosted(this, expectedVersion, actorUserId, postedUtc);
    public void MarkReversed(long expectedVersion, Guid actorUserId, DateTime reversedUtc) =>
        SettlementLifecycle.MarkReversed(this, expectedVersion, actorUserId, reversedUtc);

    internal void ApplyBankEvidence(Guid bankTransactionId, bool amountMatches, Guid actor, DateTime time)
    { BankTransactionId = bankTransactionId; Status = amountMatches ? ReadyStatus() : TreasuryMovementStatuses.NeedsReview; ReasonCode = amountMatches ? ApprovalReason() : "treasury_bank_amount_mismatch"; Touch(actor, time); }
    internal void ApplyApproval(Guid approvalId, Guid actor, DateTime time) { ApprovalRequestId = approvalId; if (BankTransactionId.HasValue && Status != TreasuryMovementStatuses.NeedsReview) { Status = TreasuryMovementStatuses.ReadyToPost; ReasonCode = null; } Touch(actor, time); }
    internal void ApplyPosted(Guid actor, DateTime time) { Status = TreasuryMovementStatuses.Posted; ReasonCode = null; PostedUtc = TreasuryValues.Timestamp(time, nameof(time)); Touch(actor, PostedUtc.Value); }
    internal void ApplyReversed(Guid actor, DateTime time) { Status = TreasuryMovementStatuses.Reversed; ReversedUtc = TreasuryValues.Timestamp(time, nameof(time)); Touch(actor, ReversedUtc.Value); }
    private string ReadyStatus() => RequiresApproval && !ApprovalRequestId.HasValue ? TreasuryMovementStatuses.AwaitingApproval : TreasuryMovementStatuses.ReadyToPost;
    private string? ApprovalReason() => RequiresApproval && !ApprovalRequestId.HasValue ? "treasury_approval_required" : null;
    private void Touch(Guid actor, DateTime time) { UpdatedByUserId = TreasuryValues.Required(actor, nameof(actor)); UpdatedUtc = TreasuryValues.Timestamp(time, nameof(time)); Version++; }
}

public sealed class PayoutSettlement : ICompanyOwnedEntity
{
    private PayoutSettlement() { }
    public PayoutSettlement(Guid id, Guid companyId, string sourceIdentity, string providerBatchReference,
        Guid bankAccountId, Guid payoutClearingFinanceAccountId, decimal grossAmount, decimal feeAmount,
        decimal netAmount, string currency, decimal materialityThreshold, Guid? correctionOfSettlementId,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = TreasuryValues.Id(id); CompanyId = TreasuryValues.Required(companyId, nameof(companyId));
        SourceIdentity = TreasuryValues.Text(sourceIdentity, nameof(sourceIdentity), 200);
        ProviderBatchReference = TreasuryValues.Text(providerBatchReference, nameof(providerBatchReference), 200);
        BankAccountId = TreasuryValues.Required(bankAccountId, nameof(bankAccountId));
        PayoutClearingFinanceAccountId = TreasuryValues.Required(payoutClearingFinanceAccountId, nameof(payoutClearingFinanceAccountId));
        GrossAmount = TreasuryValues.PositiveMoney(grossAmount, nameof(grossAmount)); FeeAmount = TreasuryValues.NonNegativeMoney(feeAmount, nameof(feeAmount));
        NetAmount = TreasuryValues.PositiveMoney(netAmount, nameof(netAmount));
        if (GrossAmount != NetAmount + FeeAmount) throw new ArgumentException("Gross amount must equal net amount plus fees.");
        Currency = TreasuryValues.Currency(currency); MaterialityThreshold = TreasuryValues.NonNegativeMoney(materialityThreshold, nameof(materialityThreshold));
        RequiresApproval = MaterialityThreshold > 0m && GrossAmount >= MaterialityThreshold;
        CorrectionOfSettlementId = TreasuryValues.OptionalId(correctionOfSettlementId, nameof(correctionOfSettlementId));
        CreatedByUserId = UpdatedByUserId = TreasuryValues.Required(actorUserId, nameof(actorUserId));
        CreatedUtc = UpdatedUtc = TreasuryValues.Timestamp(createdUtc, nameof(createdUtc));
        Status = TreasuryMovementStatuses.AwaitingBankEvidence; ReasonCode = "treasury_bank_evidence_missing"; Version = 1;
    }

    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SourceIdentity { get; private set; } = null!;
    public string ProviderBatchReference { get; private set; } = null!; public Guid BankAccountId { get; private set; }
    public Guid PayoutClearingFinanceAccountId { get; private set; } public Guid? BankTransactionId { get; private set; }
    public decimal GrossAmount { get; private set; } public decimal FeeAmount { get; private set; } public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = null!; public decimal MaterialityThreshold { get; private set; } public bool RequiresApproval { get; private set; }
    public Guid? ApprovalRequestId { get; private set; } public Guid? CorrectionOfSettlementId { get; private set; } public string Status { get; private set; } = null!;
    public string? ReasonCode { get; private set; } public long Version { get; private set; } public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public DateTime? PostedUtc { get; private set; } public DateTime? ReversedUtc { get; private set; } public Company Company { get; private set; } = null!;

    public void LinkBankEvidence(long expectedVersion, Guid bankTransactionId, bool amountMatches, Guid actorUserId, DateTime updatedUtc) =>
        SettlementLifecycle.LinkBankEvidence(this, expectedVersion, bankTransactionId, amountMatches, actorUserId, updatedUtc);
    public void BindApproval(long expectedVersion, Guid approvalRequestId, Guid actorUserId, DateTime updatedUtc) =>
        SettlementLifecycle.BindApproval(this, expectedVersion, approvalRequestId, actorUserId, updatedUtc);
    public void MarkPosted(long expectedVersion, Guid actorUserId, DateTime postedUtc) =>
        SettlementLifecycle.MarkPosted(this, expectedVersion, actorUserId, postedUtc);
    public void MarkReversed(long expectedVersion, Guid actorUserId, DateTime reversedUtc) =>
        SettlementLifecycle.MarkReversed(this, expectedVersion, actorUserId, reversedUtc);

    internal void ApplyBankEvidence(Guid bankTransactionId, bool amountMatches, Guid actor, DateTime time)
    { BankTransactionId = bankTransactionId; Status = amountMatches ? ReadyStatus() : TreasuryMovementStatuses.NeedsReview; ReasonCode = amountMatches ? ApprovalReason() : "treasury_bank_amount_mismatch"; Touch(actor, time); }
    internal void ApplyApproval(Guid approvalId, Guid actor, DateTime time) { ApprovalRequestId = approvalId; if (BankTransactionId.HasValue && Status != TreasuryMovementStatuses.NeedsReview) { Status = TreasuryMovementStatuses.ReadyToPost; ReasonCode = null; } Touch(actor, time); }
    internal void ApplyPosted(Guid actor, DateTime time) { Status = TreasuryMovementStatuses.Posted; ReasonCode = null; PostedUtc = TreasuryValues.Timestamp(time, nameof(time)); Touch(actor, PostedUtc.Value); }
    internal void ApplyReversed(Guid actor, DateTime time) { Status = TreasuryMovementStatuses.Reversed; ReversedUtc = TreasuryValues.Timestamp(time, nameof(time)); Touch(actor, ReversedUtc.Value); }
    private string ReadyStatus() => RequiresApproval && !ApprovalRequestId.HasValue ? TreasuryMovementStatuses.AwaitingApproval : TreasuryMovementStatuses.ReadyToPost;
    private string? ApprovalReason() => RequiresApproval && !ApprovalRequestId.HasValue ? "treasury_approval_required" : null;
    private void Touch(Guid actor, DateTime time) { UpdatedByUserId = TreasuryValues.Required(actor, nameof(actor)); UpdatedUtc = TreasuryValues.Timestamp(time, nameof(time)); Version++; }
}

public sealed class TreasurySourceEvidence : ICompanyOwnedEntity
{
    private TreasurySourceEvidence() { }
    public TreasurySourceEvidence(Guid id, Guid companyId, string sourceType, Guid sourceId, string evidenceType,
        string reference, string contentHash, string description, DateTime createdUtc)
    {
        Id = TreasuryValues.Id(id); CompanyId = TreasuryValues.Required(companyId, nameof(companyId));
        SourceType = TreasurySourceTypes.Normalize(sourceType); if (!TreasurySourceTypes.IsSupported(SourceType)) throw new ArgumentOutOfRangeException(nameof(sourceType));
        SourceId = TreasuryValues.Required(sourceId, nameof(sourceId)); EvidenceType = TreasuryValues.Text(evidenceType, nameof(evidenceType), 64).ToLowerInvariant();
        Reference = TreasuryValues.Text(reference, nameof(reference), 300); ContentHash = TreasuryValues.Hash(contentHash, nameof(contentHash));
        Description = TreasuryValues.Text(description, nameof(description), 500); CreatedUtc = TreasuryValues.Timestamp(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SourceType { get; private set; } = null!;
    public Guid SourceId { get; private set; } public string EvidenceType { get; private set; } = null!; public string Reference { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!; public string Description { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
}

public sealed class TreasurySourceEvent : ICompanyOwnedEntity
{
    private TreasurySourceEvent() { }
    public TreasurySourceEvent(Guid id, Guid companyId, string sourceType, Guid sourceId, string action,
        Guid actorUserId, string? reasonCode, string beforeJson, string afterJson, DateTime createdUtc)
    {
        Id = TreasuryValues.Id(id); CompanyId = TreasuryValues.Required(companyId, nameof(companyId));
        SourceType = TreasurySourceTypes.Normalize(sourceType); if (!TreasurySourceTypes.IsSupported(SourceType)) throw new ArgumentOutOfRangeException(nameof(sourceType));
        SourceId = TreasuryValues.Required(sourceId, nameof(sourceId)); Action = TreasuryValues.Text(action, nameof(action), 80);
        ActorUserId = TreasuryValues.Required(actorUserId, nameof(actorUserId)); ReasonCode = TreasuryValues.OptionalText(reasonCode, nameof(reasonCode), 100);
        BeforeJson = TreasuryValues.RequiredJson(beforeJson, nameof(beforeJson)); AfterJson = TreasuryValues.RequiredJson(afterJson, nameof(afterJson));
        CreatedUtc = TreasuryValues.Timestamp(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SourceType { get; private set; } = null!;
    public Guid SourceId { get; private set; } public string Action { get; private set; } = null!; public Guid ActorUserId { get; private set; }
    public string? ReasonCode { get; private set; } public string BeforeJson { get; private set; } = null!; public string AfterJson { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; } public Company Company { get; private set; } = null!;
}

public sealed class TreasurySourceLedgerLink : ICompanyOwnedEntity
{
    private TreasurySourceLedgerLink() { }
    public TreasurySourceLedgerLink(Guid id, Guid companyId, string sourceType, Guid sourceId, Guid ledgerEntryId,
        string linkRole, DateTime createdUtc)
    {
        Id = TreasuryValues.Id(id); CompanyId = TreasuryValues.Required(companyId, nameof(companyId));
        SourceType = TreasurySourceTypes.Normalize(sourceType); if (!TreasurySourceTypes.IsSupported(SourceType)) throw new ArgumentOutOfRangeException(nameof(sourceType));
        SourceId = TreasuryValues.Required(sourceId, nameof(sourceId)); LedgerEntryId = TreasuryValues.Required(ledgerEntryId, nameof(ledgerEntryId));
        LinkRole = linkRole?.Trim().ToLowerInvariant() is TreasuryLedgerLinkRoles.Posting or TreasuryLedgerLinkRoles.Reversal
            ? linkRole.Trim().ToLowerInvariant() : throw new ArgumentOutOfRangeException(nameof(linkRole));
        CreatedUtc = TreasuryValues.Timestamp(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SourceType { get; private set; } = null!;
    public Guid SourceId { get; private set; } public Guid LedgerEntryId { get; private set; } public string LinkRole { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; } public Company Company { get; private set; } = null!; public LedgerEntry LedgerEntry { get; private set; } = null!;
}

internal static class TreasuryLifecycle
{
    public static void BindApproval(BankAdjustment source, long version, Guid approval, Guid actor, DateTime time)
    { EnsureMutable(version, source.Version, source.Status); source.ApplyApproval(TreasuryValues.Required(approval, nameof(approval)), actor, time); }
    public static void MarkPosted(BankAdjustment source, long version, Guid actor, DateTime time)
    { EnsureReady(version, source.Version, source.Status); source.ApplyPosted(actor, time); }
    public static void MarkReversed(BankAdjustment source, long version, Guid actor, DateTime time)
    { EnsurePosted(version, source.Version, source.Status); source.ApplyReversed(actor, time); }
    private static void EnsureMutable(long expected, long actual, string status) { EnsureVersion(expected, actual); if (status is TreasuryMovementStatuses.Posted or TreasuryMovementStatuses.Reversed) throw new InvalidOperationException("Posted treasury evidence cannot be changed. Create a correction instead."); }
    private static void EnsureReady(long expected, long actual, string status) { EnsureVersion(expected, actual); if (status != TreasuryMovementStatuses.ReadyToPost) throw new InvalidOperationException("The treasury source is not ready to post."); }
    private static void EnsurePosted(long expected, long actual, string status) { EnsureVersion(expected, actual); if (status != TreasuryMovementStatuses.Posted) throw new InvalidOperationException("Only a posted treasury source can be reversed."); }
    private static void EnsureVersion(long expected, long actual) { if (expected != actual) throw new InvalidOperationException("The treasury source changed after it was reviewed."); }
}

internal static class SettlementLifecycle
{
    public static void LinkBankEvidence(CardSettlement source, long version, Guid transaction, bool matches, Guid actor, DateTime time)
    { EnsureMutable(version, source.Version, source.Status); source.ApplyBankEvidence(TreasuryValues.Required(transaction, nameof(transaction)), matches, actor, time); }
    public static void BindApproval(CardSettlement source, long version, Guid approval, Guid actor, DateTime time)
    { EnsureMutable(version, source.Version, source.Status); source.ApplyApproval(TreasuryValues.Required(approval, nameof(approval)), actor, time); }
    public static void MarkPosted(CardSettlement source, long version, Guid actor, DateTime time)
    { EnsureReady(version, source.Version, source.Status); source.ApplyPosted(actor, time); }
    public static void MarkReversed(CardSettlement source, long version, Guid actor, DateTime time)
    { EnsurePosted(version, source.Version, source.Status); source.ApplyReversed(actor, time); }
    public static void LinkBankEvidence(PayoutSettlement source, long version, Guid transaction, bool matches, Guid actor, DateTime time)
    { EnsureMutable(version, source.Version, source.Status); source.ApplyBankEvidence(TreasuryValues.Required(transaction, nameof(transaction)), matches, actor, time); }
    public static void BindApproval(PayoutSettlement source, long version, Guid approval, Guid actor, DateTime time)
    { EnsureMutable(version, source.Version, source.Status); source.ApplyApproval(TreasuryValues.Required(approval, nameof(approval)), actor, time); }
    public static void MarkPosted(PayoutSettlement source, long version, Guid actor, DateTime time)
    { EnsureReady(version, source.Version, source.Status); source.ApplyPosted(actor, time); }
    public static void MarkReversed(PayoutSettlement source, long version, Guid actor, DateTime time)
    { EnsurePosted(version, source.Version, source.Status); source.ApplyReversed(actor, time); }
    private static void EnsureMutable(long expected, long actual, string status) { EnsureVersion(expected, actual); if (status is TreasuryMovementStatuses.Posted or TreasuryMovementStatuses.Reversed) throw new InvalidOperationException("Posted treasury evidence cannot be changed. Create a correction instead."); }
    private static void EnsureReady(long expected, long actual, string status) { EnsureVersion(expected, actual); if (status != TreasuryMovementStatuses.ReadyToPost) throw new InvalidOperationException("The treasury source is not ready to post."); }
    private static void EnsurePosted(long expected, long actual, string status) { EnsureVersion(expected, actual); if (status != TreasuryMovementStatuses.Posted) throw new InvalidOperationException("Only a posted treasury source can be reversed."); }
    private static void EnsureVersion(long expected, long actual) { if (expected != actual) throw new InvalidOperationException("The treasury source changed after it was reviewed."); }
}

internal static class TreasuryValues
{
    public static Guid Id(Guid value) => value == Guid.Empty ? Guid.NewGuid() : value;
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static Guid? OptionalId(Guid? value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} cannot be empty.", name) : value;
    public static decimal PositiveMoney(decimal value, string name) => value <= 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal NonNegativeMoney(decimal value, string name) => value < 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static string Currency(string value) { var normalized = Text(value, nameof(value), 3).ToUpperInvariant(); return normalized.Length == 3 && normalized.All(char.IsLetter) ? normalized : throw new ArgumentOutOfRangeException(nameof(value)); }
    public static string Text(string value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name); var normalized = value.Trim(); return normalized.Length <= max ? normalized : throw new ArgumentOutOfRangeException(name); }
    public static string? OptionalText(string? value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) return null; var normalized = value.Trim(); return normalized.Length <= max ? normalized : throw new ArgumentOutOfRangeException(name); }
    public static string Hash(string value, string name) { var normalized = Text(value, name, 128).ToLowerInvariant(); return normalized.All(char.IsAsciiHexDigit) && normalized.Length is >= 32 and <= 128 ? normalized : throw new ArgumentOutOfRangeException(name, "Evidence hash must be hexadecimal."); }
    public static string RequiredJson(string value, string name) => Text(value, name, 1_000_000);
    public static DateTime Timestamp(DateTime value, string name) => EntityTimestampNormalizer.NormalizeUtc(value, name);
}
