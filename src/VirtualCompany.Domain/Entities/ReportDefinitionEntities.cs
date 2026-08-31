namespace VirtualCompany.Domain.Entities;

public static class ReportDefinitionVersionStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Active = "active";
    public const string Retired = "retired";
}

public static class ReportDefinitionLineTypes
{
    public const string Detail = "detail";
    public const string Formula = "formula";
    public const string Heading = "heading";
    public const string Subtotal = "subtotal";
    public static bool IsSupported(string value) => value is Detail or Formula or Heading or Subtotal;
}

public static class ReportDefinitionSignRules
{
    public const string Normal = "normal";
    public const string Invert = "invert";
    public static bool IsSupported(string value) => value is Normal or Invert;
}

public static class ReportDefinitionCurrencyModes
{
    public const string Functional = "functional";
    public const string Document = "document";
    public static bool IsSupported(string value) => value is Functional or Document;
}

public sealed class ReportDefinition : ICompanyOwnedEntity
{
    private ReportDefinition() { }

    public ReportDefinition(Guid id, Guid companyId, string code, string name, string reportKind,
        string sourceTemplateKey, Guid createdByUserId, DateTime createdUtc)
    {
        Id = ReportDefinitionText.Required(id, nameof(id));
        CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        Code = ReportDefinitionText.Code(code, nameof(code));
        Name = ReportDefinitionText.Value(name, nameof(name), 200);
        ReportKind = ReportDefinitionText.Value(reportKind, nameof(reportKind), 64);
        SourceTemplateKey = ReportDefinitionText.Value(sourceTemplateKey, nameof(sourceTemplateKey), 100);
        CreatedByUserId = ReportDefinitionText.Required(createdByUserId, nameof(createdByUserId));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string ReportKind { get; private set; } = null!;
    public string SourceTemplateKey { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<ReportDefinitionVersion> Versions { get; private set; } = [];
}

public sealed class ReportDefinitionVersion : ICompanyOwnedEntity
{
    private ReportDefinitionVersion() { }

    public ReportDefinitionVersion(Guid id, Guid companyId, Guid definitionId, int versionNumber,
        string name, string reportKind, Guid createdByUserId, DateTime createdUtc)
    {
        Id = ReportDefinitionText.Required(id, nameof(id));
        CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        DefinitionId = ReportDefinitionText.Required(definitionId, nameof(definitionId));
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        VersionNumber = versionNumber;
        Name = ReportDefinitionText.Value(name, nameof(name), 200);
        ReportKind = ReportDefinitionText.Value(reportKind, nameof(reportKind), 64);
        Status = ReportDefinitionVersionStatuses.Draft;
        CreatedByUserId = ReportDefinitionText.Required(createdByUserId, nameof(createdByUserId));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        Revision = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Name { get; private set; } = null!;
    public string ReportKind { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateOnly? EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public string DefinitionHash { get; private set; } = string.Empty;
    public int Revision { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? SubmittedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? ActivatedUtc { get; private set; }
    public DateTime? RetiredUtc { get; private set; }
    public ReportDefinition Definition { get; private set; } = null!;
    public ICollection<ReportDefinitionSection> Sections { get; private set; } = [];
    public ICollection<ReportDefinitionAccountGroup> AccountGroups { get; private set; } = [];
    public ICollection<ReportDefinitionValidationResult> ValidationResults { get; private set; } = [];
    public ICollection<ReportDefinitionApproval> Approvals { get; private set; } = [];
    public ReportDefinitionComparison? Comparison { get; private set; }

    public void UpdateDraft(string name, int expectedRevision, DateTime now)
    {
        RequireDraft(expectedRevision);
        Name = ReportDefinitionText.Value(name, nameof(name), 200);
        DefinitionHash = string.Empty;
        Touch(now);
    }

    public void MarkValidated(string definitionHash, int expectedRevision, DateTime now)
    {
        RequireDraft(expectedRevision);
        DefinitionHash = ReportDefinitionText.Hash(definitionHash, nameof(definitionHash));
        Touch(now);
    }

    public void Submit(int expectedRevision, DateTime now)
    {
        RequireDraft(expectedRevision);
        if (string.IsNullOrWhiteSpace(DefinitionHash)) throw new InvalidOperationException("A validated definition is required.");
        Status = ReportDefinitionVersionStatuses.Submitted;
        SubmittedUtc = EntityTimestampNormalizer.NormalizeUtc(now, nameof(now));
        Touch(now);
    }

    public void Approve(int expectedRevision, DateTime now)
    {
        RequireRevision(expectedRevision);
        if (Status != ReportDefinitionVersionStatuses.Submitted)
            throw new InvalidOperationException("Only a submitted report definition can be approved.");
        Status = ReportDefinitionVersionStatuses.Approved;
        ApprovedUtc = EntityTimestampNormalizer.NormalizeUtc(now, nameof(now));
        Touch(now);
    }

    public void Reject(int expectedRevision, DateTime now)
    {
        RequireRevision(expectedRevision);
        if (Status != ReportDefinitionVersionStatuses.Submitted)
            throw new InvalidOperationException("Only a submitted report definition can be rejected.");
        Status = ReportDefinitionVersionStatuses.Draft;
        DefinitionHash = string.Empty;
        Touch(now);
    }

    public void Activate(DateOnly effectiveFrom, int expectedRevision, DateTime now)
    {
        RequireRevision(expectedRevision);
        if (Status != ReportDefinitionVersionStatuses.Approved)
            throw new InvalidOperationException("Only an approved report definition can be activated.");
        EffectiveFrom = effectiveFrom;
        EffectiveTo = null;
        Status = ReportDefinitionVersionStatuses.Active;
        ActivatedUtc = EntityTimestampNormalizer.NormalizeUtc(now, nameof(now));
        Touch(now);
    }

    public void Retire(DateOnly effectiveTo, int expectedRevision, DateTime now)
    {
        RequireRevision(expectedRevision);
        if (Status is not (ReportDefinitionVersionStatuses.Active or ReportDefinitionVersionStatuses.Approved))
            throw new InvalidOperationException("Only an approved or active report definition can be retired.");
        if (EffectiveFrom.HasValue && effectiveTo < EffectiveFrom.Value)
            throw new ArgumentOutOfRangeException(nameof(effectiveTo));
        EffectiveTo = effectiveTo;
        Status = ReportDefinitionVersionStatuses.Retired;
        RetiredUtc = EntityTimestampNormalizer.NormalizeUtc(now, nameof(now));
        Touch(now);
    }

    public bool IsEffectiveOn(DateOnly date) => Status is ReportDefinitionVersionStatuses.Active or ReportDefinitionVersionStatuses.Retired &&
        EffectiveFrom <= date && (!EffectiveTo.HasValue || date < EffectiveTo.Value);

    private void RequireDraft(int expectedRevision)
    {
        RequireRevision(expectedRevision);
        if (Status != ReportDefinitionVersionStatuses.Draft)
            throw new InvalidOperationException("Only a draft report definition can be edited.");
    }

    private void RequireRevision(int expectedRevision)
    {
        if (Revision != expectedRevision) throw new ReportDefinitionConcurrencyException(Revision);
    }

    private void Touch(DateTime now)
    {
        Revision++;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(now, nameof(now));
    }
}

public sealed class ReportDefinitionSection : ICompanyOwnedEntity
{
    private ReportDefinitionSection() { }
    public ReportDefinitionSection(Guid id, Guid companyId, Guid versionId, string code, string label, int displayOrder)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        VersionId = ReportDefinitionText.Required(versionId, nameof(versionId)); Code = ReportDefinitionText.Code(code, nameof(code));
        Label = ReportDefinitionText.Value(label, nameof(label), 200); DisplayOrder = displayOrder;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VersionId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public int DisplayOrder { get; private set; }
    public ReportDefinitionVersion Version { get; private set; } = null!;
    public ICollection<ReportDefinitionLine> Lines { get; private set; } = [];
}

public sealed class ReportDefinitionLine : ICompanyOwnedEntity
{
    private ReportDefinitionLine() { }
    public ReportDefinitionLine(Guid id, Guid companyId, Guid versionId, Guid sectionId, string code, string label,
        string lineType, int displayOrder, string? formula, string signRule, int scale, int decimals,
        bool suppressZero, string currencyMode, Guid? dimensionTypeId, Guid? dimensionMemberId)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        VersionId = ReportDefinitionText.Required(versionId, nameof(versionId)); SectionId = ReportDefinitionText.Required(sectionId, nameof(sectionId));
        Code = ReportDefinitionText.Code(code, nameof(code)); Label = ReportDefinitionText.Value(label, nameof(label), 200);
        if (!ReportDefinitionLineTypes.IsSupported(lineType)) throw new ArgumentOutOfRangeException(nameof(lineType));
        if (!ReportDefinitionSignRules.IsSupported(signRule)) throw new ArgumentOutOfRangeException(nameof(signRule));
        if (!ReportDefinitionCurrencyModes.IsSupported(currencyMode)) throw new ArgumentOutOfRangeException(nameof(currencyMode));
        if (scale is not (1 or 1000 or 1000000)) throw new ArgumentOutOfRangeException(nameof(scale));
        if (decimals is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(decimals));
        LineType = lineType; DisplayOrder = displayOrder; Formula = string.IsNullOrWhiteSpace(formula) ? null : formula.Trim();
        SignRule = signRule; Scale = scale; Decimals = decimals; SuppressZero = suppressZero; CurrencyMode = currencyMode;
        DimensionTypeId = dimensionTypeId; DimensionMemberId = dimensionMemberId;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid SectionId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public string LineType { get; private set; } = null!;
    public int DisplayOrder { get; private set; }
    public string? Formula { get; private set; }
    public string SignRule { get; private set; } = null!;
    public int Scale { get; private set; }
    public int Decimals { get; private set; }
    public bool SuppressZero { get; private set; }
    public string CurrencyMode { get; private set; } = null!;
    public Guid? DimensionTypeId { get; private set; }
    public Guid? DimensionMemberId { get; private set; }
    public ReportDefinitionVersion Version { get; private set; } = null!;
    public ReportDefinitionSection Section { get; private set; } = null!;
    public ICollection<ReportDefinitionAccountGroup> AccountGroups { get; private set; } = [];
}

public sealed class ReportDefinitionAccountGroup : ICompanyOwnedEntity
{
    private ReportDefinitionAccountGroup() { }
    public ReportDefinitionAccountGroup(Guid id, Guid companyId, Guid versionId, Guid lineId, string code, string name)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        VersionId = ReportDefinitionText.Required(versionId, nameof(versionId)); LineId = ReportDefinitionText.Required(lineId, nameof(lineId));
        Code = ReportDefinitionText.Code(code, nameof(code)); Name = ReportDefinitionText.Value(name, nameof(name), 200);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid LineId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public ReportDefinitionVersion Version { get; private set; } = null!;
    public ReportDefinitionLine Line { get; private set; } = null!;
    public ICollection<ReportDefinitionAccountGroupMember> Members { get; private set; } = [];
}

public sealed class ReportDefinitionAccountGroupMember : ICompanyOwnedEntity
{
    private ReportDefinitionAccountGroupMember() { }
    public ReportDefinitionAccountGroupMember(Guid id, Guid companyId, Guid groupId, Guid financeAccountId)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        GroupId = ReportDefinitionText.Required(groupId, nameof(groupId)); FinanceAccountId = ReportDefinitionText.Required(financeAccountId, nameof(financeAccountId));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public ReportDefinitionAccountGroup Group { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;
}

public sealed class ReportDefinitionComparison : ICompanyOwnedEntity
{
    private ReportDefinitionComparison() { }
    public ReportDefinitionComparison(Guid id, Guid companyId, Guid versionId, string mode, int periodCount, bool showVariance, bool showVariancePercent)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        VersionId = ReportDefinitionText.Required(versionId, nameof(versionId));
        if (mode is not ("none" or "prior_period" or "prior_year" or "rolling")) throw new ArgumentOutOfRangeException(nameof(mode));
        if (periodCount is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(periodCount));
        Mode = mode; PeriodCount = periodCount; ShowVariance = showVariance; ShowVariancePercent = showVariancePercent;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VersionId { get; private set; }
    public string Mode { get; private set; } = null!;
    public int PeriodCount { get; private set; }
    public bool ShowVariance { get; private set; }
    public bool ShowVariancePercent { get; private set; }
    public ReportDefinitionVersion Version { get; private set; } = null!;
}

public sealed class ReportDefinitionValidationResult : ICompanyOwnedEntity
{
    private ReportDefinitionValidationResult() { }
    public ReportDefinitionValidationResult(Guid id, Guid companyId, Guid versionId, bool isValid, string definitionHash,
        Guid validatedByUserId, DateTime validatedUtc)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        VersionId = ReportDefinitionText.Required(versionId, nameof(versionId)); IsValid = isValid;
        DefinitionHash = ReportDefinitionText.Hash(definitionHash, nameof(definitionHash));
        ValidatedByUserId = ReportDefinitionText.Required(validatedByUserId, nameof(validatedByUserId));
        ValidatedUtc = EntityTimestampNormalizer.NormalizeUtc(validatedUtc, nameof(validatedUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VersionId { get; private set; }
    public bool IsValid { get; private set; }
    public string DefinitionHash { get; private set; } = null!;
    public Guid ValidatedByUserId { get; private set; }
    public DateTime ValidatedUtc { get; private set; }
    public ReportDefinitionVersion Version { get; private set; } = null!;
    public ICollection<ReportDefinitionValidationIssue> Issues { get; private set; } = [];
}

public sealed class ReportDefinitionValidationIssue : ICompanyOwnedEntity
{
    private ReportDefinitionValidationIssue() { }
    public ReportDefinitionValidationIssue(Guid id, Guid companyId, Guid validationResultId, string code,
        string severity, string explanation, Guid? lineId = null, Guid? accountId = null)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        ValidationResultId = ReportDefinitionText.Required(validationResultId, nameof(validationResultId));
        Code = ReportDefinitionText.Value(code, nameof(code), 100); Severity = ReportDefinitionText.Value(severity, nameof(severity), 20);
        Explanation = ReportDefinitionText.Value(explanation, nameof(explanation), 1000); LineId = lineId; AccountId = accountId;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ValidationResultId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Severity { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public Guid? LineId { get; private set; }
    public Guid? AccountId { get; private set; }
    public ReportDefinitionValidationResult ValidationResult { get; private set; } = null!;
}

public sealed class ReportDefinitionApproval : ICompanyOwnedEntity
{
    private ReportDefinitionApproval() { }
    public ReportDefinitionApproval(Guid id, Guid companyId, Guid versionId, Guid submittedByUserId, DateTime submittedUtc)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        VersionId = ReportDefinitionText.Required(versionId, nameof(versionId)); SubmittedByUserId = ReportDefinitionText.Required(submittedByUserId, nameof(submittedByUserId));
        SubmittedUtc = EntityTimestampNormalizer.NormalizeUtc(submittedUtc, nameof(submittedUtc)); Status = "pending";
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VersionId { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid SubmittedByUserId { get; private set; }
    public DateTime SubmittedUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedUtc { get; private set; }
    public string? DecisionNote { get; private set; }
    public ReportDefinitionVersion Version { get; private set; } = null!;
    public void Decide(bool approved, Guid actor, string? note, DateTime now)
    {
        if (Status != "pending") throw new InvalidOperationException("The approval has already been decided.");
        Status = approved ? "approved" : "rejected"; DecidedByUserId = ReportDefinitionText.Required(actor, nameof(actor));
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : ReportDefinitionText.Value(note, nameof(note), 1000);
        DecidedUtc = EntityTimestampNormalizer.NormalizeUtc(now, nameof(now));
    }
}

public sealed class ReportDefinitionCommandReceipt : ICompanyOwnedEntity
{
    private ReportDefinitionCommandReceipt() { }
    public ReportDefinitionCommandReceipt(Guid id, Guid companyId, string idempotencyKey, string operation,
        Guid versionId, Guid actorUserId, DateTime createdUtc)
    {
        Id = ReportDefinitionText.Required(id, nameof(id)); CompanyId = ReportDefinitionText.Required(companyId, nameof(companyId));
        IdempotencyKey = ReportDefinitionText.Value(idempotencyKey, nameof(idempotencyKey), 200);
        Operation = ReportDefinitionText.Value(operation, nameof(operation), 64); VersionId = ReportDefinitionText.Required(versionId, nameof(versionId));
        ActorUserId = ReportDefinitionText.Required(actorUserId, nameof(actorUserId)); CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public Guid VersionId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public ReportDefinitionVersion Version { get; private set; } = null!;
}

public sealed class ReportDefinitionConcurrencyException(int currentRevision)
    : Exception("The report definition changed after it was loaded. Refresh and retry.")
{
    public int CurrentRevision { get; } = currentRevision;
}

internal static class ReportDefinitionText
{
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Value(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    public static string Code(string value, string name)
    {
        var result = Value(value, name, 80).ToUpperInvariant();
        if (!result.All(x => char.IsLetterOrDigit(x) || x is '_' or '-' or '.')) throw new ArgumentException($"{name} contains unsupported characters.", name);
        return result;
    }
    public static string Hash(string value, string name)
    {
        var result = Value(value, name, 64).ToLowerInvariant();
        return result.Length == 64 && result.All(Uri.IsHexDigit) ? result : throw new ArgumentException($"{name} must be SHA-256.", name);
    }
}
