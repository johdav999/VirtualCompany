namespace VirtualCompany.Application.Finance;
public sealed record PaymentSettlementDto(Guid Id, Guid BankTransactionId, string BankReference, decimal Amount, string Currency, int PaymentCount, int AllocationCount, IReadOnlyList<Guid> LedgerEntryIds, DateTime SettledUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid BankTransactionId { get; set; } = BankTransactionId;
    public string BankReference { get; set; } = BankReference;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public int PaymentCount { get; set; } = PaymentCount;
    public int AllocationCount { get; set; } = AllocationCount;
    public IReadOnlyList<Guid> LedgerEntryIds { get; set; } = LedgerEntryIds;
    public DateTime SettledUtc { get; set; } = SettledUtc;

    public PaymentSettlementDto() : this(default !, default !, string.Empty, default !, string.Empty, default !, default !, [], default !)
    {
    }
}

public sealed record PaymentBatchExecutionDto(Guid Id, Guid BatchId, string BatchReference, int InstructionSetVersion, long Version, string ProviderKey, string ProviderDisplayName, Guid BankConnectionId, string InstitutionName, Guid CompanyBankAccountId, string BankAccountName, string MaskedBankAccount, string Status, string? ProviderPaymentId, Uri? AuthorizationUri, string? ProviderStatus, string RequestHash, string BusinessIdempotencyKey, bool UpdatesExpected, bool CanCancelAtProvider, string? ReasonCode, string? SafeSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ProviderAcceptedUtc, DateTime? ProviderCompletedUtc, DateTime? SettledUtc, IReadOnlyList<PaymentExecutionAttemptDto> Attempts, IReadOnlyList<PaymentAcknowledgementDto> Acknowledgements, IReadOnlyList<PaymentExecutionInstructionDto> Instructions, IReadOnlyList<PaymentRemittanceDto> Remittances, PaymentSettlementDto? Settlement, PaymentExecutionAllowedActionsDto AllowedActions, bool IsIdempotentReplay = false)
{
    public Guid Id { get; set; } = Id;
    public Guid BatchId { get; set; } = BatchId;
    public string BatchReference { get; set; } = BatchReference;
    public int InstructionSetVersion { get; set; } = InstructionSetVersion;
    public long Version { get; set; } = Version;
    public string ProviderKey { get; set; } = ProviderKey;
    public string ProviderDisplayName { get; set; } = ProviderDisplayName;
    public Guid BankConnectionId { get; set; } = BankConnectionId;
    public string InstitutionName { get; set; } = InstitutionName;
    public Guid CompanyBankAccountId { get; set; } = CompanyBankAccountId;
    public string BankAccountName { get; set; } = BankAccountName;
    public string MaskedBankAccount { get; set; } = MaskedBankAccount;
    public string Status { get; set; } = Status;
    public string? ProviderPaymentId { get; set; } = ProviderPaymentId;
    public Uri? AuthorizationUri { get; set; } = AuthorizationUri;
    public string? ProviderStatus { get; set; } = ProviderStatus;
    public string RequestHash { get; set; } = RequestHash;
    public string BusinessIdempotencyKey { get; set; } = BusinessIdempotencyKey;
    public bool UpdatesExpected { get; set; } = UpdatesExpected;
    public bool CanCancelAtProvider { get; set; } = CanCancelAtProvider;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string? SafeSummary { get; set; } = SafeSummary;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime? ProviderAcceptedUtc { get; set; } = ProviderAcceptedUtc;
    public DateTime? ProviderCompletedUtc { get; set; } = ProviderCompletedUtc;
    public DateTime? SettledUtc { get; set; } = SettledUtc;
    public IReadOnlyList<PaymentExecutionAttemptDto> Attempts { get; set; } = Attempts;
    public IReadOnlyList<PaymentAcknowledgementDto> Acknowledgements { get; set; } = Acknowledgements;
    public IReadOnlyList<PaymentExecutionInstructionDto> Instructions { get; set; } = Instructions;
    public IReadOnlyList<PaymentRemittanceDto> Remittances { get; set; } = Remittances;
    public PaymentSettlementDto? Settlement { get; set; } = Settlement;
    public PaymentExecutionAllowedActionsDto AllowedActions { get; set; } = AllowedActions;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public PaymentBatchExecutionDto() : this(default !, default !, string.Empty, default !, default !, string.Empty, string.Empty, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [], [], [], default !, new(), default !)
    {
    }
}

public sealed record PaymentExecutionInstructionDto(Guid Id, Guid PaymentInstructionId, int Sequence, decimal Amount, string Currency, string BeneficiaryName, string MaskedDestination, string? ProviderTransactionId, string Status, string? ReasonCode, Guid? PaymentId, Guid? PaymentAllocationId)
{
    public Guid Id { get; set; } = Id;
    public Guid PaymentInstructionId { get; set; } = PaymentInstructionId;
    public int Sequence { get; set; } = Sequence;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string BeneficiaryName { get; set; } = BeneficiaryName;
    public string MaskedDestination { get; set; } = MaskedDestination;
    public string? ProviderTransactionId { get; set; } = ProviderTransactionId;
    public string Status { get; set; } = Status;
    public string? ReasonCode { get; set; } = ReasonCode;
    public Guid? PaymentId { get; set; } = PaymentId;
    public Guid? PaymentAllocationId { get; set; } = PaymentAllocationId;

    public PaymentExecutionInstructionDto() : this(default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record PaymentRemittanceDto(Guid Id, Guid PaymentInstructionId, string BeneficiaryName, string? RecipientEmail, string Status, string ContentHash, string? ProviderReference, string? ReasonCode, string? SafeSummary, int AttemptCount, DateTime CreatedUtc, DateTime? AcceptedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid PaymentInstructionId { get; set; } = PaymentInstructionId;
    public string BeneficiaryName { get; set; } = BeneficiaryName;
    public string? RecipientEmail { get; set; } = RecipientEmail;
    public string Status { get; set; } = Status;
    public string ContentHash { get; set; } = ContentHash;
    public string? ProviderReference { get; set; } = ProviderReference;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string? SafeSummary { get; set; } = SafeSummary;
    public int AttemptCount { get; set; } = AttemptCount;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? AcceptedUtc { get; set; } = AcceptedUtc;

    public PaymentRemittanceDto() : this(default !, default !, string.Empty, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record PaymentAcknowledgementDto(Guid Id, string Source, string ProviderStatus, string NormalizedStatus, bool IsFinal, bool UpdatesExpected, string? ReasonCode, string? SafeSummary, string EvidenceHash, DateTime AcknowledgedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Source { get; set; } = Source;
    public string ProviderStatus { get; set; } = ProviderStatus;
    public string NormalizedStatus { get; set; } = NormalizedStatus;
    public bool IsFinal { get; set; } = IsFinal;
    public bool UpdatesExpected { get; set; } = UpdatesExpected;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string? SafeSummary { get; set; } = SafeSummary;
    public string EvidenceHash { get; set; } = EvidenceHash;
    public DateTime AcknowledgedUtc { get; set; } = AcknowledgedUtc;

    public PaymentAcknowledgementDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record PaymentExecutionAllowedActionsDto(bool CanOpenBankAuthorization, bool CanCancel, bool CanRefreshStatus, bool CanAttachProviderReference, bool CanSettle, bool CanRetryRemittance, string? BlockingReasonCode, string Explanation)
{
    public bool CanOpenBankAuthorization { get; set; } = CanOpenBankAuthorization;
    public bool CanCancel { get; set; } = CanCancel;
    public bool CanRefreshStatus { get; set; } = CanRefreshStatus;
    public bool CanAttachProviderReference { get; set; } = CanAttachProviderReference;
    public bool CanSettle { get; set; } = CanSettle;
    public bool CanRetryRemittance { get; set; } = CanRetryRemittance;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string Explanation { get; set; } = Explanation;

    public PaymentExecutionAllowedActionsDto() : this(default !, default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record PaymentExecutionAttemptDto(Guid Id, int AttemptNumber, string Operation, string Outcome, string RequestHash, string? ProviderRequestId, string? ReasonCode, string? SafeSummary, string RetryClassification, DateTime StartedUtc, DateTime? CompletedUtc)
{
    public Guid Id { get; set; } = Id;
    public int AttemptNumber { get; set; } = AttemptNumber;
    public string Operation { get; set; } = Operation;
    public string Outcome { get; set; } = Outcome;
    public string RequestHash { get; set; } = RequestHash;
    public string? ProviderRequestId { get; set; } = ProviderRequestId;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string? SafeSummary { get; set; } = SafeSummary;
    public string RetryClassification { get; set; } = RetryClassification;
    public DateTime StartedUtc { get; set; } = StartedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;

    public PaymentExecutionAttemptDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, string.Empty, default !, default !)
    {
    }
}
