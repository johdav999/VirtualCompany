namespace VirtualCompany.Application.Finance;
public sealed record FinanceBillInboxDetailDto(Guid Id, string SupplierName, string? SupplierOrgNumber, string BillReference, DateTime? BillDateUtc, DateTime? DueDateUtc, decimal? Amount, decimal? VatAmount, string? Currency, string Status, decimal? Confidence, string ConfidenceLevel, IReadOnlyList<FinanceBillExtractedFieldDto> ExtractedFields, IReadOnlyList<FinanceBillWarningDto> ValidationWarnings, IReadOnlyList<FinanceBillWarningDto> DuplicateWarnings, FinanceBillProposalSummaryDto ProposalSummary, FinanceBillSourcePreviewDto? SourcePreview, IReadOnlyList<FinanceBillReviewActionDto> ActionHistory, bool CanApprove, string? ApprovalBlockedReason, bool UsesInternalAccounting, bool CanUseFortnoxAccounting, string AccountingGuidance, Guid? OperationalBillId, FinanceBillFortnoxRegistrationDto? FortnoxRegistration)
{
    public Guid Id { get; set; } = Id;
    public string SupplierName { get; set; } = SupplierName;
    public string? SupplierOrgNumber { get; set; } = SupplierOrgNumber;
    public string BillReference { get; set; } = BillReference;
    public DateTime? BillDateUtc { get; set; } = BillDateUtc;
    public DateTime? DueDateUtc { get; set; } = DueDateUtc;
    public decimal? Amount { get; set; } = Amount;
    public decimal? VatAmount { get; set; } = VatAmount;
    public string? Currency { get; set; } = Currency;
    public string Status { get; set; } = Status;
    public decimal? Confidence { get; set; } = Confidence;
    public string ConfidenceLevel { get; set; } = ConfidenceLevel;
    public IReadOnlyList<FinanceBillExtractedFieldDto> ExtractedFields { get; set; } = ExtractedFields;
    public IReadOnlyList<FinanceBillWarningDto> ValidationWarnings { get; set; } = ValidationWarnings;
    public IReadOnlyList<FinanceBillWarningDto> DuplicateWarnings { get; set; } = DuplicateWarnings;
    public FinanceBillProposalSummaryDto ProposalSummary { get; set; } = ProposalSummary;
    public FinanceBillSourcePreviewDto? SourcePreview { get; set; } = SourcePreview;
    public IReadOnlyList<FinanceBillReviewActionDto> ActionHistory { get; set; } = ActionHistory;
    public bool CanApprove { get; set; } = CanApprove;
    public string? ApprovalBlockedReason { get; set; } = ApprovalBlockedReason;
    public bool UsesInternalAccounting { get; set; } = UsesInternalAccounting;
    public bool CanUseFortnoxAccounting { get; set; } = CanUseFortnoxAccounting;
    public string AccountingGuidance { get; set; } = AccountingGuidance;
    public Guid? OperationalBillId { get; set; } = OperationalBillId;
    public FinanceBillFortnoxRegistrationDto? FortnoxRegistration { get; set; } = FortnoxRegistration;

    public FinanceBillInboxDetailDto() : this(default !, string.Empty, default !, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !, string.Empty, [], [], [], new(), default !, [], default !, default !, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record FinanceBillReviewActionResultDto(Guid BillId, string PriorStatus, string NewStatus, DateTime OccurredUtc, Guid? OperationalBillId = null)
{
    public Guid BillId { get; set; } = BillId;
    public string PriorStatus { get; set; } = PriorStatus;
    public string NewStatus { get; set; } = NewStatus;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;
    public Guid? OperationalBillId { get; set; } = OperationalBillId;

    public FinanceBillReviewActionResultDto() : this(default !, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record FinanceBillWarningDto(string Code, string Severity, string Message, bool IsResolved)
{
    public string Code { get; set; } = Code;
    public string Severity { get; set; } = Severity;
    public string Message { get; set; } = Message;
    public bool IsResolved { get; set; } = IsResolved;

    public FinanceBillWarningDto() : this(string.Empty, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record SupplierApprovalAutomationDto(Guid BillId, string SupplierName, string? SupplierOrgNumber, IReadOnlyList<SupplierApprovalAutomationStageDto> Stages)
{
    public Guid BillId { get; set; } = BillId;
    public string SupplierName { get; set; } = SupplierName;
    public string? SupplierOrgNumber { get; set; } = SupplierOrgNumber;
    public IReadOnlyList<SupplierApprovalAutomationStageDto> Stages { get; set; } = Stages;

    public SupplierApprovalAutomationDto() : this(default !, string.Empty, default !, [])
    {
    }
}

public sealed record FinanceBillSourcePreviewDto(string Title, string? From, DateTime? ReceivedUtc, string? BodyText, string SourceLabel = "Email body", string? FileName = null)
{
    public string Title { get; set; } = Title;
    public string? From { get; set; } = From;
    public DateTime? ReceivedUtc { get; set; } = ReceivedUtc;
    public string? BodyText { get; set; } = BodyText;
    public string SourceLabel { get; set; } = SourceLabel;
    public string? FileName { get; set; } = FileName;

    public FinanceBillSourcePreviewDto() : this(string.Empty, default !, default !, default !, "Email body", default !)
    {
    }
}

public sealed record SupplierApprovalAutomationStageDto(string Stage, string StepName, bool IsEnabled, Guid? RuleId, Guid AgentId, string AgentDisplayName, bool CanConfigure, string? BlockedReason)
{
    public string Stage { get; set; } = Stage;
    public string StepName { get; set; } = StepName;
    public bool IsEnabled { get; set; } = IsEnabled;
    public Guid? RuleId { get; set; } = RuleId;
    public Guid AgentId { get; set; } = AgentId;
    public string AgentDisplayName { get; set; } = AgentDisplayName;
    public bool CanConfigure { get; set; } = CanConfigure;
    public string? BlockedReason { get; set; } = BlockedReason;

    public SupplierApprovalAutomationStageDto() : this(string.Empty, string.Empty, default !, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record FinanceBillEvidenceReferenceDto(string SourceDocument, string? SourceDocumentType, string? PageReference, string? SectionReference, string? TextSpan, string? Locator, string? Snippet)
{
    public string SourceDocument { get; set; } = SourceDocument;
    public string? SourceDocumentType { get; set; } = SourceDocumentType;
    public string? PageReference { get; set; } = PageReference;
    public string? SectionReference { get; set; } = SectionReference;
    public string? TextSpan { get; set; } = TextSpan;
    public string? Locator { get; set; } = Locator;
    public string? Snippet { get; set; } = Snippet;

    public FinanceBillEvidenceReferenceDto() : this(string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceBillReviewActionDto(Guid Id, string Action, string ActorDisplayName, Guid? ActorUserId, DateTime OccurredUtc, string PriorStatus, string NewStatus, string Rationale)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public string ActorDisplayName { get; set; } = ActorDisplayName;
    public Guid? ActorUserId { get; set; } = ActorUserId;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;
    public string PriorStatus { get; set; } = PriorStatus;
    public string NewStatus { get; set; } = NewStatus;
    public string Rationale { get; set; } = Rationale;

    public FinanceBillReviewActionDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record FinanceBillProposalSummaryDto(string Headline, string Summary, IReadOnlyList<string> RiskFlags, string ApprovalAsk, string RecommendedAction, bool ExplicitlyRequestsApproval, bool InitiatesPayment)
{
    public string Headline { get; set; } = Headline;
    public string Summary { get; set; } = Summary;
    public IReadOnlyList<string> RiskFlags { get; set; } = RiskFlags;
    public string ApprovalAsk { get; set; } = ApprovalAsk;
    public string RecommendedAction { get; set; } = RecommendedAction;
    public bool ExplicitlyRequestsApproval { get; set; } = ExplicitlyRequestsApproval;
    public bool InitiatesPayment { get; set; } = InitiatesPayment;

    public FinanceBillProposalSummaryDto() : this(string.Empty, string.Empty, [], string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record FinanceBillExtractedFieldDto(string FieldName, string DisplayName, string? RawValue, string? NormalizedValue, decimal? Confidence, IReadOnlyList<FinanceBillEvidenceReferenceDto> EvidenceReferences)
{
    public string FieldName { get; set; } = FieldName;
    public string DisplayName { get; set; } = DisplayName;
    public string? RawValue { get; set; } = RawValue;
    public string? NormalizedValue { get; set; } = NormalizedValue;
    public decimal? Confidence { get; set; } = Confidence;
    public IReadOnlyList<FinanceBillEvidenceReferenceDto> EvidenceReferences { get; set; } = EvidenceReferences;

    public FinanceBillExtractedFieldDto() : this(string.Empty, string.Empty, default !, default !, default !, [])
    {
    }
}

public sealed record FinanceBillFortnoxRegistrationDto(Guid? WriteRequestId, Guid? ApprovalId, string Status, string Message, bool CanRequest, bool CanSendDirect, bool CanExecute, bool HasPendingRequest, bool HasExecuted, string? FortnoxPath, string? ExternalId = null, string? ActionKind = null)
{
    public Guid? WriteRequestId { get; set; } = WriteRequestId;
    public Guid? ApprovalId { get; set; } = ApprovalId;
    public string Status { get; set; } = Status;
    public string Message { get; set; } = Message;
    public bool CanRequest { get; set; } = CanRequest;
    public bool CanSendDirect { get; set; } = CanSendDirect;
    public bool CanExecute { get; set; } = CanExecute;
    public bool HasPendingRequest { get; set; } = HasPendingRequest;
    public bool HasExecuted { get; set; } = HasExecuted;
    public string? FortnoxPath { get; set; } = FortnoxPath;
    public string? ExternalId { get; set; } = ExternalId;
    public string? ActionKind { get; set; } = ActionKind;

    public FinanceBillFortnoxRegistrationDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceBillInboxRowDto(Guid Id, string SupplierName, string BillReference, decimal? Amount, string? Currency, DateTime DetectedUtc, string Status, string ConfidenceLevel, int ValidationWarningCount, int DuplicateWarningCount)
{
    public Guid Id { get; set; } = Id;
    public string SupplierName { get; set; } = SupplierName;
    public string BillReference { get; set; } = BillReference;
    public decimal? Amount { get; set; } = Amount;
    public string? Currency { get; set; } = Currency;
    public DateTime DetectedUtc { get; set; } = DetectedUtc;
    public string Status { get; set; } = Status;
    public string ConfidenceLevel { get; set; } = ConfidenceLevel;
    public int ValidationWarningCount { get; set; } = ValidationWarningCount;
    public int DuplicateWarningCount { get; set; } = DuplicateWarningCount;

    public FinanceBillInboxRowDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !, default !)
    {
    }
}
