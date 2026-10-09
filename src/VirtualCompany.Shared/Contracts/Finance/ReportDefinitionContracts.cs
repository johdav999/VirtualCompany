namespace VirtualCompany.Application.Finance;
public sealed record ReportDefinitionLineDto(Guid Id, string Code, string Label, string LineType, int DisplayOrder, string? Formula, string SignRule, int Scale, int Decimals, bool SuppressZero, string CurrencyMode, Guid? DimensionTypeId, Guid? DimensionMemberId, IReadOnlyList<ReportDefinitionAccountGroupDto> AccountGroups)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Label { get; set; } = Label;
    public string LineType { get; set; } = LineType;
    public int DisplayOrder { get; set; } = DisplayOrder;
    public string? Formula { get; set; } = Formula;
    public string SignRule { get; set; } = SignRule;
    public int Scale { get; set; } = Scale;
    public int Decimals { get; set; } = Decimals;
    public bool SuppressZero { get; set; } = SuppressZero;
    public string CurrencyMode { get; set; } = CurrencyMode;
    public Guid? DimensionTypeId { get; set; } = DimensionTypeId;
    public Guid? DimensionMemberId { get; set; } = DimensionMemberId;
    public IReadOnlyList<ReportDefinitionAccountGroupDto> AccountGroups { get; set; } = AccountGroups;

    public ReportDefinitionLineDto() : this(default !, "", "", "detail", default !, default !, "normal", 1, 2, default !, "functional", default !, default !, [])
    {
    }
}

public sealed record ReportDefinitionApprovalDto(Guid Id, string Status, Guid SubmittedByUserId, DateTime SubmittedUtc, Guid? DecidedByUserId, DateTime? DecidedUtc, string? DecisionNote)
{
    public Guid Id { get; set; } = Id;
    public string Status { get; set; } = Status;
    public Guid SubmittedByUserId { get; set; } = SubmittedByUserId;
    public DateTime SubmittedUtc { get; set; } = SubmittedUtc;
    public Guid? DecidedByUserId { get; set; } = DecidedByUserId;
    public DateTime? DecidedUtc { get; set; } = DecidedUtc;
    public string? DecisionNote { get; set; } = DecisionNote;

    public ReportDefinitionApprovalDto() : this(default !, "", default !, default !, default !, default !, default !)
    {
    }
}

public sealed record ReportSystemTemplateDto(string Key, string Name, string ReportKind, string Description, bool IsStatutoryCandidate)
{
    public string Key { get; set; } = Key;
    public string Name { get; set; } = Name;
    public string ReportKind { get; set; } = ReportKind;
    public string Description { get; set; } = Description;
    public bool IsStatutoryCandidate { get; set; } = IsStatutoryCandidate;

    public ReportSystemTemplateDto() : this("", "", "", "", default !)
    {
    }
}

public sealed record ReportDefinitionComparisonDto(string Mode, int PeriodCount, bool ShowVariance, bool ShowVariancePercent)
{
    public string Mode { get; set; } = Mode;
    public int PeriodCount { get; set; } = PeriodCount;
    public bool ShowVariance { get; set; } = ShowVariance;
    public bool ShowVariancePercent { get; set; } = ShowVariancePercent;

    public ReportDefinitionComparisonDto() : this("none", 1, default !, default !)
    {
    }
}

public sealed record ReportDefinitionAccountGroupDto(Guid Id, string Code, string Name, IReadOnlyList<Guid> FinanceAccountIds)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public IReadOnlyList<Guid> FinanceAccountIds { get; set; } = FinanceAccountIds;

    public ReportDefinitionAccountGroupDto() : this(default !, "", "", [])
    {
    }
}

public sealed record ReportDefinitionVersionDto(Guid DefinitionId, Guid VersionId, string Code, string Name, string ReportKind, string SourceTemplateKey, int VersionNumber, string Status, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? DefinitionHash, int Revision, DateTime CreatedUtc, DateTime UpdatedUtc, IReadOnlyList<ReportDefinitionSectionDto> Sections, ReportDefinitionComparisonDto Comparison, ReportDefinitionValidationDto? LatestValidation, ReportDefinitionApprovalDto? LatestApproval, bool CanEdit, bool CanSubmit, bool CanApprove, bool CanActivate, bool CanRetire)
{
    public Guid DefinitionId { get; set; } = DefinitionId;
    public Guid VersionId { get; set; } = VersionId;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string ReportKind { get; set; } = ReportKind;
    public string SourceTemplateKey { get; set; } = SourceTemplateKey;
    public int VersionNumber { get; set; } = VersionNumber;
    public string Status { get; set; } = Status;
    public DateOnly? EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public string? DefinitionHash { get; set; } = DefinitionHash;
    public int Revision { get; set; } = Revision;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public IReadOnlyList<ReportDefinitionSectionDto> Sections { get; set; } = Sections;
    public ReportDefinitionComparisonDto Comparison { get; set; } = Comparison;
    public ReportDefinitionValidationDto? LatestValidation { get; set; } = LatestValidation;
    public ReportDefinitionApprovalDto? LatestApproval { get; set; } = LatestApproval;
    public bool CanEdit { get; set; } = CanEdit;
    public bool CanSubmit { get; set; } = CanSubmit;
    public bool CanApprove { get; set; } = CanApprove;
    public bool CanActivate { get; set; } = CanActivate;
    public bool CanRetire { get; set; } = CanRetire;

    public ReportDefinitionVersionDto() : this(default !, default !, "", "", "", "", default !, "", default !, default !, default !, default !, default !, default !, [], new(), default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record ReportDefinitionValidationIssueDto(string Code, string Severity, string Explanation, Guid? LineId = null, Guid? AccountId = null)
{
    public string Code { get; set; } = Code;
    public string Severity { get; set; } = Severity;
    public string Explanation { get; set; } = Explanation;
    public Guid? LineId { get; set; } = LineId;
    public Guid? AccountId { get; set; } = AccountId;

    public ReportDefinitionValidationIssueDto() : this("", "", "", default !, default !)
    {
    }
}

public sealed record ReportDefinitionSectionDto(Guid Id, string Code, string Label, int DisplayOrder, IReadOnlyList<ReportDefinitionLineDto> Lines)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Label { get; set; } = Label;
    public int DisplayOrder { get; set; } = DisplayOrder;
    public IReadOnlyList<ReportDefinitionLineDto> Lines { get; set; } = Lines;

    public ReportDefinitionSectionDto() : this(default !, "", "", default !, [])
    {
    }
}

public sealed record ReportDefinitionValidationDto(Guid Id, bool IsValid, string DefinitionHash, Guid ValidatedByUserId, DateTime ValidatedUtc, IReadOnlyList<ReportDefinitionValidationIssueDto> Issues)
{
    public Guid Id { get; set; } = Id;
    public bool IsValid { get; set; } = IsValid;
    public string DefinitionHash { get; set; } = DefinitionHash;
    public Guid ValidatedByUserId { get; set; } = ValidatedByUserId;
    public DateTime ValidatedUtc { get; set; } = ValidatedUtc;
    public IReadOnlyList<ReportDefinitionValidationIssueDto> Issues { get; set; } = Issues;

    public ReportDefinitionValidationDto() : this(default !, default !, "", default !, default !, [])
    {
    }
}

public sealed record ReportDefinitionSummaryDto(Guid Id, string Code, string Name, string ReportKind, string SourceTemplateKey, int LatestVersionNumber, string LatestStatus, Guid LatestVersionId, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, int Revision)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string ReportKind { get; set; } = ReportKind;
    public string SourceTemplateKey { get; set; } = SourceTemplateKey;
    public int LatestVersionNumber { get; set; } = LatestVersionNumber;
    public string LatestStatus { get; set; } = LatestStatus;
    public Guid LatestVersionId { get; set; } = LatestVersionId;
    public DateOnly? EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public int Revision { get; set; } = Revision;

    public ReportDefinitionSummaryDto() : this(default !, "", "", "", "", default !, "", default !, default !, default !, default !)
    {
    }
}

public sealed record ReportDefinitionSectionInput(string Code, string Label, int DisplayOrder, IReadOnlyList<ReportDefinitionLineInput> Lines)
{
    public string Code { get; set; } = Code;
    public string Label { get; set; } = Label;
    public int DisplayOrder { get; set; } = DisplayOrder;
    public IReadOnlyList<ReportDefinitionLineInput> Lines { get; set; } = Lines;

    public ReportDefinitionSectionInput() : this("", "", default !, [])
    {
    }
}

public sealed record ReportDefinitionLineInput(string Code, string Label, string LineType, int DisplayOrder, string? Formula, string SignRule, int Scale, int Decimals, bool SuppressZero, string CurrencyMode, Guid? DimensionTypeId, Guid? DimensionMemberId, IReadOnlyList<ReportDefinitionAccountGroupInput> AccountGroups)
{
    public string Code { get; set; } = Code;
    public string Label { get; set; } = Label;
    public string LineType { get; set; } = LineType;
    public int DisplayOrder { get; set; } = DisplayOrder;
    public string? Formula { get; set; } = Formula;
    public string SignRule { get; set; } = SignRule;
    public int Scale { get; set; } = Scale;
    public int Decimals { get; set; } = Decimals;
    public bool SuppressZero { get; set; } = SuppressZero;
    public string CurrencyMode { get; set; } = CurrencyMode;
    public Guid? DimensionTypeId { get; set; } = DimensionTypeId;
    public Guid? DimensionMemberId { get; set; } = DimensionMemberId;
    public IReadOnlyList<ReportDefinitionAccountGroupInput> AccountGroups { get; set; } = AccountGroups;

    public ReportDefinitionLineInput() : this("", "", "detail", default !, default !, "normal", 1, 2, default !, "functional", default !, default !, [])
    {
    }
}

public sealed record ReportDefinitionAccountGroupInput(string Code, string Name, IReadOnlyList<Guid> FinanceAccountIds)
{
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public IReadOnlyList<Guid> FinanceAccountIds { get; set; } = FinanceAccountIds;

    public ReportDefinitionAccountGroupInput() : this("", "", [])
    {
    }
}
