namespace VirtualCompany.Application.Finance;
public sealed record PaymentBatchListDto(IReadOnlyList<PaymentBatchSummaryDto> Items, int DraftCount, int NeedsValidationCount, int AwaitingApprovalCount, IReadOnlyList<PaymentBatchTotalDto> PlannedTotals)
{
    public IReadOnlyList<PaymentBatchSummaryDto> Items { get; set; } = Items;
    public int DraftCount { get; set; } = DraftCount;
    public int NeedsValidationCount { get; set; } = NeedsValidationCount;
    public int AwaitingApprovalCount { get; set; } = AwaitingApprovalCount;
    public IReadOnlyList<PaymentBatchTotalDto> PlannedTotals { get; set; } = PlannedTotals;

    public PaymentBatchListDto() : this([], default !, default !, default !, [])
    {
    }
}

public sealed record PaymentBatchSummaryDto(Guid Id, string Reference, string Name, DateOnly PlannedExecutionDate, string Status, long Version, int InstructionSetVersion, int ObligationCount, IReadOnlyList<PaymentBatchTotalDto> Totals, Guid CreatedByUserId, Guid? SubmittedByUserId, Guid? ApprovedByUserId, DateTime CreatedUtc, DateTime UpdatedUtc, bool IsIdempotentReplay = false)
{
    public Guid Id { get; set; } = Id;
    public string Reference { get; set; } = Reference;
    public string Name { get; set; } = Name;
    public DateOnly PlannedExecutionDate { get; set; } = PlannedExecutionDate;
    public string Status { get; set; } = Status;
    public long Version { get; set; } = Version;
    public int InstructionSetVersion { get; set; } = InstructionSetVersion;
    public int ObligationCount { get; set; } = ObligationCount;
    public IReadOnlyList<PaymentBatchTotalDto> Totals { get; set; } = Totals;
    public Guid CreatedByUserId { get; set; } = CreatedByUserId;
    public Guid? SubmittedByUserId { get; set; } = SubmittedByUserId;
    public Guid? ApprovedByUserId { get; set; } = ApprovedByUserId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;

    public PaymentBatchSummaryDto() : this(default !, string.Empty, string.Empty, default !, string.Empty, default !, default !, default !, [], default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record PaymentBatchPreviewDto(Guid BatchId, long Version, bool CanValidate, DateOnly RecommendedExecutionDate, IReadOnlyList<PaymentBatchTotalDto> Totals, IReadOnlyList<PaymentBatchValidationIssueDto> Issues, string InternalApprovalNotice)
{
    public Guid BatchId { get; set; } = BatchId;
    public long Version { get; set; } = Version;
    public bool CanValidate { get; set; } = CanValidate;
    public DateOnly RecommendedExecutionDate { get; set; } = RecommendedExecutionDate;
    public IReadOnlyList<PaymentBatchTotalDto> Totals { get; set; } = Totals;
    public IReadOnlyList<PaymentBatchValidationIssueDto> Issues { get; set; } = Issues;
    public string InternalApprovalNotice { get; set; } = InternalApprovalNotice;

    public PaymentBatchPreviewDto() : this(default !, default !, default !, default !, [], [], string.Empty)
    {
    }
}

public sealed record PaymentBatchObligationDto(Guid Id, string ObligationType, Guid SourceId, string SourceReference, string SourceVersion, string SourceHash, decimal Amount, string Currency, DateOnly DueDate, string PaymentReference, string BeneficiaryName, string Rail, string MaskedDestination, int BeneficiaryVersion, string VerificationEvidenceReference, DateTime VerifiedUtc, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string ObligationType { get; set; } = ObligationType;
    public Guid SourceId { get; set; } = SourceId;
    public string SourceReference { get; set; } = SourceReference;
    public string SourceVersion { get; set; } = SourceVersion;
    public string SourceHash { get; set; } = SourceHash;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public DateOnly DueDate { get; set; } = DueDate;
    public string PaymentReference { get; set; } = PaymentReference;
    public string BeneficiaryName { get; set; } = BeneficiaryName;
    public string Rail { get; set; } = Rail;
    public string MaskedDestination { get; set; } = MaskedDestination;
    public int BeneficiaryVersion { get; set; } = BeneficiaryVersion;
    public string VerificationEvidenceReference { get; set; } = VerificationEvidenceReference;
    public DateTime VerifiedUtc { get; set; } = VerifiedUtc;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public PaymentBatchObligationDto() : this(default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record PaymentBatchDetailDto(PaymentBatchSummaryDto Summary, IReadOnlyList<PaymentBatchObligationDto> Obligations, IReadOnlyList<PaymentInstructionDto> Instructions, PaymentBatchValidationResultDto? Validation, PaymentBatchApprovalDto? Approval, PaymentBatchAllowedActionsDto AllowedActions, string InternalApprovalNotice, string? ExportArtifactHash = null)
{
    public PaymentBatchSummaryDto Summary { get; set; } = Summary;
    public IReadOnlyList<PaymentBatchObligationDto> Obligations { get; set; } = Obligations;
    public IReadOnlyList<PaymentInstructionDto> Instructions { get; set; } = Instructions;
    public PaymentBatchValidationResultDto? Validation { get; set; } = Validation;
    public PaymentBatchApprovalDto? Approval { get; set; } = Approval;
    public PaymentBatchAllowedActionsDto AllowedActions { get; set; } = AllowedActions;
    public string InternalApprovalNotice { get; set; } = InternalApprovalNotice;
    public string? ExportArtifactHash { get; set; } = ExportArtifactHash;

    public PaymentBatchDetailDto() : this(new(), [], [], default !, default !, new(), string.Empty, default !)
    {
    }
}

public sealed record PaymentBatchApprovalDto(Guid BindingId, Guid ApprovalRequestId, string Status, int InstructionSetVersion, string SourceSetHash, Guid RequestedByUserId, Guid? DecidedByUserId, DateTime CreatedUtc, DateTime? DecidedUtc)
{
    public Guid BindingId { get; set; } = BindingId;
    public Guid ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string Status { get; set; } = Status;
    public int InstructionSetVersion { get; set; } = InstructionSetVersion;
    public string SourceSetHash { get; set; } = SourceSetHash;
    public Guid RequestedByUserId { get; set; } = RequestedByUserId;
    public Guid? DecidedByUserId { get; set; } = DecidedByUserId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;

    public PaymentBatchApprovalDto() : this(default !, default !, string.Empty, default !, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record PaymentBatchValidationResultDto(Guid Id, long EvaluatedBatchVersion, int InstructionSetVersion, bool IsValid, string SourceSetHash, IReadOnlyList<PaymentBatchTotalDto> Totals, IReadOnlyList<PaymentBatchValidationIssueDto> Issues, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public long EvaluatedBatchVersion { get; set; } = EvaluatedBatchVersion;
    public int InstructionSetVersion { get; set; } = InstructionSetVersion;
    public bool IsValid { get; set; } = IsValid;
    public string SourceSetHash { get; set; } = SourceSetHash;
    public IReadOnlyList<PaymentBatchTotalDto> Totals { get; set; } = Totals;
    public IReadOnlyList<PaymentBatchValidationIssueDto> Issues { get; set; } = Issues;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public PaymentBatchValidationResultDto() : this(default !, default !, default !, default !, string.Empty, [], [], default !)
    {
    }
}

public sealed record PaymentInstructionDto(Guid Id, int InstructionSetVersion, int Sequence, DateOnly ExecutionDate, decimal Amount, string Currency, string PaymentReference, string BeneficiaryName, string Rail, string MaskedDestination, string SourceVersion, string ContentHash, string Status, bool IsCurrent, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public int InstructionSetVersion { get; set; } = InstructionSetVersion;
    public int Sequence { get; set; } = Sequence;
    public DateOnly ExecutionDate { get; set; } = ExecutionDate;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string PaymentReference { get; set; } = PaymentReference;
    public string BeneficiaryName { get; set; } = BeneficiaryName;
    public string Rail { get; set; } = Rail;
    public string MaskedDestination { get; set; } = MaskedDestination;
    public string SourceVersion { get; set; } = SourceVersion;
    public string ContentHash { get; set; } = ContentHash;
    public string Status { get; set; } = Status;
    public bool IsCurrent { get; set; } = IsCurrent;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public PaymentInstructionDto() : this(default !, default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record PaymentBatchAllowedActionsDto(bool CanAddOrRemove, bool CanValidate, bool CanSubmit, bool CanApprove, bool CanReject, bool CanCancel, bool CanRegenerate, bool CanCheckSendReadiness, string? BlockingReasonCode, string Explanation)
{
    public bool CanAddOrRemove { get; set; } = CanAddOrRemove;
    public bool CanValidate { get; set; } = CanValidate;
    public bool CanSubmit { get; set; } = CanSubmit;
    public bool CanApprove { get; set; } = CanApprove;
    public bool CanReject { get; set; } = CanReject;
    public bool CanCancel { get; set; } = CanCancel;
    public bool CanRegenerate { get; set; } = CanRegenerate;
    public bool CanCheckSendReadiness { get; set; } = CanCheckSendReadiness;
    public string? BlockingReasonCode { get; set; } = BlockingReasonCode;
    public string Explanation { get; set; } = Explanation;

    public PaymentBatchAllowedActionsDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record PaymentBatchSendReadinessDto(Guid BatchId, bool IsReady, string ReasonCode, string Explanation, int InstructionSetVersion, string? ApprovedSourceSetHash, string CurrentSourceSetHash, IReadOnlyList<PaymentBatchValidationIssueDto> Issues)
{
    public Guid BatchId { get; set; } = BatchId;
    public bool IsReady { get; set; } = IsReady;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public int InstructionSetVersion { get; set; } = InstructionSetVersion;
    public string? ApprovedSourceSetHash { get; set; } = ApprovedSourceSetHash;
    public string CurrentSourceSetHash { get; set; } = CurrentSourceSetHash;
    public IReadOnlyList<PaymentBatchValidationIssueDto> Issues { get; set; } = Issues;

    public PaymentBatchSendReadinessDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, string.Empty, [])
    {
    }
}

public sealed record PaymentBatchValidationIssueDto(Guid Id, Guid? ObligationLinkId, string Severity, string ReasonCode, string Explanation)
{
    public Guid Id { get; set; } = Id;
    public Guid? ObligationLinkId { get; set; } = ObligationLinkId;
    public string Severity { get; set; } = Severity;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;

    public PaymentBatchValidationIssueDto() : this(default !, default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record EligiblePaymentObligationDto(string ObligationType, Guid SourceId, string SourceReference, string BeneficiaryName, string? Rail, string? MaskedDestination, decimal Amount, string Currency, DateOnly DueDate, string PaymentReference, bool IsEligible, string ReasonCode, string Explanation, DateOnly RecommendedExecutionDate)
{
    public string ObligationType { get; set; } = ObligationType;
    public Guid SourceId { get; set; } = SourceId;
    public string SourceReference { get; set; } = SourceReference;
    public string BeneficiaryName { get; set; } = BeneficiaryName;
    public string? Rail { get; set; } = Rail;
    public string? MaskedDestination { get; set; } = MaskedDestination;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public DateOnly DueDate { get; set; } = DueDate;
    public string PaymentReference { get; set; } = PaymentReference;
    public bool IsEligible { get; set; } = IsEligible;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public DateOnly RecommendedExecutionDate { get; set; } = RecommendedExecutionDate;

    public EligiblePaymentObligationDto() : this(string.Empty, default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, default !, string.Empty, default !, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record PaymentBatchTotalDto(string Currency, decimal Amount, decimal? AvailableCash, bool HasSufficientCash)
{
    public string Currency { get; set; } = Currency;
    public decimal Amount { get; set; } = Amount;
    public decimal? AvailableCash { get; set; } = AvailableCash;
    public bool HasSufficientCash { get; set; } = HasSufficientCash;

    public PaymentBatchTotalDto() : this(string.Empty, default !, default !, default !)
    {
    }
}

public sealed record PaymentBeneficiaryProfileDto(Guid Id, string PartyType, Guid PartyId, string DisplayName, string Rail, string MaskedDestination, string Currency, int Version, string Status, string VerificationEvidenceReference, DateTime VerifiedUtc)
{
    public Guid Id { get; set; } = Id;
    public string PartyType { get; set; } = PartyType;
    public Guid PartyId { get; set; } = PartyId;
    public string DisplayName { get; set; } = DisplayName;
    public string Rail { get; set; } = Rail;
    public string MaskedDestination { get; set; } = MaskedDestination;
    public string Currency { get; set; } = Currency;
    public int Version { get; set; } = Version;
    public string Status { get; set; } = Status;
    public string VerificationEvidenceReference { get; set; } = VerificationEvidenceReference;
    public DateTime VerifiedUtc { get; set; } = VerifiedUtc;

    public PaymentBeneficiaryProfileDto() : this(default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, string.Empty, default !)
    {
    }
}
