namespace VirtualCompany.Application.Finance;
public sealed record ComplianceAcknowledgementDto(Guid Id, string Kind, string Reference, string ContentHash, Guid ActorUserId, DateTime OccurredUtc)
{
    public Guid Id { get; set; } = Id;
    public string Kind { get; set; } = Kind;
    public string Reference { get; set; } = Reference;
    public string ContentHash { get; set; } = ContentHash;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;

    public ComplianceAcknowledgementDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record ComplianceHistoryDto(Guid Id, string Action, string FromStatus, string ToStatus, Guid ActorUserId, string SourceHash, string? Reason, DateTime OccurredUtc)
{
    public Guid Id { get; set; } = Id;
    public string Action { get; set; } = Action;
    public string FromStatus { get; set; } = FromStatus;
    public string ToStatus { get; set; } = ToStatus;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public string SourceHash { get; set; } = SourceHash;
    public string? Reason { get; set; } = Reason;
    public DateTime OccurredUtc { get; set; } = OccurredUtc;

    public ComplianceHistoryDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record ComplianceCalendarDto(Guid CompanyId, DateOnly From, DateOnly To, int OpenCount, int DueSoonCount, int OverdueCount, int AwaitingAuthorityCount, IReadOnlyList<ComplianceObligationDto> Obligations, string SubmissionCapability = "export_and_manual_evidence_only")
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateOnly From { get; set; } = From;
    public DateOnly To { get; set; } = To;
    public int OpenCount { get; set; } = OpenCount;
    public int DueSoonCount { get; set; } = DueSoonCount;
    public int OverdueCount { get; set; } = OverdueCount;
    public int AwaitingAuthorityCount { get; set; } = AwaitingAuthorityCount;
    public IReadOnlyList<ComplianceObligationDto> Obligations { get; set; } = Obligations;
    public string SubmissionCapability { get; set; } = SubmissionCapability;

    public ComplianceCalendarDto() : this(default !, default !, default !, default !, default !, default !, default !, [], string.Empty)
    {
    }
}

public sealed record ComplianceReminderDto(Guid Id, string Kind, int EscalationLevel, string Status, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Kind { get; set; } = Kind;
    public int EscalationLevel { get; set; } = EscalationLevel;
    public string Status { get; set; } = Status;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public ComplianceReminderDto() : this(default !, string.Empty, default !, string.Empty, default !)
    {
    }
}

public sealed record ComplianceRequirementDto(string Kind, string Label, bool IsSatisfied, string? EvidenceReference)
{
    public string Kind { get; set; } = Kind;
    public string Label { get; set; } = Label;
    public bool IsSatisfied { get; set; } = IsSatisfied;
    public string? EvidenceReference { get; set; } = EvidenceReference;

    public ComplianceRequirementDto() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record ComplianceEvidenceDto(Guid Id, string Reference, string ContentHash, Guid ActorUserId, DateTime SubmittedUtc, string ReviewStatus, Guid? ReviewedByUserId, DateTime? ReviewedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Reference { get; set; } = Reference;
    public string ContentHash { get; set; } = ContentHash;
    public Guid ActorUserId { get; set; } = ActorUserId;
    public DateTime SubmittedUtc { get; set; } = SubmittedUtc;
    public string ReviewStatus { get; set; } = ReviewStatus;
    public Guid? ReviewedByUserId { get; set; } = ReviewedByUserId;
    public DateTime? ReviewedUtc { get; set; } = ReviewedUtc;

    public ComplianceEvidenceDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record ComplianceObligationDto(Guid Id, Guid CompanyId, string DefinitionKey, string Title, string Jurisdiction, string PolicyPackKey, string PolicyPackVersion, string PolicyPackDefinitionHash, string DueDateRule, DateOnly DueDate, Guid OwnerUserId, string Status, string SubmissionMode, Guid VatFilingPeriodId, Guid? VatReturnId, Guid? AccountingCloseTaskId, Guid? CorrectionOfInstanceId, Guid? CorrectedByInstanceId, string SourceHash, string? ExportReference, string? ExportChecksum, long Version, DateTime CreatedUtc, DateTime UpdatedUtc, IReadOnlyList<ComplianceRequirementDto> Requirements, IReadOnlyList<ComplianceHistoryDto> History, IReadOnlyList<ComplianceEvidenceDto> SubmissionEvidence, IReadOnlyList<ComplianceAcknowledgementDto> Acknowledgements, IReadOnlyList<ComplianceReminderDto> Reminders, IReadOnlyList<string> AllowedActions, string ComplianceNotice = "Artifacts and recorded evidence do not by themselves prove filing, authority receipt, approval, or statutory compliance.")
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string DefinitionKey { get; set; } = DefinitionKey;
    public string Title { get; set; } = Title;
    public string Jurisdiction { get; set; } = Jurisdiction;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public string PolicyPackDefinitionHash { get; set; } = PolicyPackDefinitionHash;
    public string DueDateRule { get; set; } = DueDateRule;
    public DateOnly DueDate { get; set; } = DueDate;
    public Guid OwnerUserId { get; set; } = OwnerUserId;
    public string Status { get; set; } = Status;
    public string SubmissionMode { get; set; } = SubmissionMode;
    public Guid VatFilingPeriodId { get; set; } = VatFilingPeriodId;
    public Guid? VatReturnId { get; set; } = VatReturnId;
    public Guid? AccountingCloseTaskId { get; set; } = AccountingCloseTaskId;
    public Guid? CorrectionOfInstanceId { get; set; } = CorrectionOfInstanceId;
    public Guid? CorrectedByInstanceId { get; set; } = CorrectedByInstanceId;
    public string SourceHash { get; set; } = SourceHash;
    public string? ExportReference { get; set; } = ExportReference;
    public string? ExportChecksum { get; set; } = ExportChecksum;
    public long Version { get; set; } = Version;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public IReadOnlyList<ComplianceRequirementDto> Requirements { get; set; } = Requirements;
    public IReadOnlyList<ComplianceHistoryDto> History { get; set; } = History;
    public IReadOnlyList<ComplianceEvidenceDto> SubmissionEvidence { get; set; } = SubmissionEvidence;
    public IReadOnlyList<ComplianceAcknowledgementDto> Acknowledgements { get; set; } = Acknowledgements;
    public IReadOnlyList<ComplianceReminderDto> Reminders { get; set; } = Reminders;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;
    public string ComplianceNotice { get; set; } = ComplianceNotice;

    public ComplianceObligationDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, [], [], [], [], [], [], string.Empty)
    {
    }
}
