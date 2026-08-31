namespace VirtualCompany.Domain.Entities;

public static class AccountingDimensionStatusValues
{
    public const string Active = "active";
    public const string Archived = "archived";

    public static string Normalize(string value) => Required(value, nameof(value), 24).ToLowerInvariant() switch
    {
        Active => Active,
        Archived => Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "The dimension lifecycle status is not supported.")
    };

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var result = value.Trim();
        if (result.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return result;
    }
}

public static class AccountingDimensionRequirementValues
{
    public const string Optional = "optional";
    public const string Required = "required";
    public const string Prohibited = "prohibited";

    public static string Normalize(string value) => Text(value, nameof(value), 24).ToLowerInvariant() switch
    {
        Optional => Optional,
        Required => Required,
        Prohibited => Prohibited,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "The dimension requirement is not supported.")
    };

    private static string Text(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var result = value.Trim();
        if (result.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return result;
    }
}

public static class AccountingAllocationKindValues
{
    public const string Percentage = "percentage";
    public const string Fixed = "fixed";

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Percentage => Percentage,
        Fixed => Fixed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "The allocation kind is not supported.")
    };
}

public sealed class AccountingDimensionType : ICompanyOwnedEntity
{
    private AccountingDimensionType() { }

    public AccountingDimensionType(Guid id, Guid companyId, string code, string name, string? description,
        bool allowsHierarchy, string status, DateOnly effectiveFrom, DateOnly? effectiveTo,
        Guid createdByUserId, DateTime createdUtc)
    {
        EnsureCompany(companyId);
        if (createdByUserId == Guid.Empty) throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        ValidateDates(effectiveFrom, effectiveTo);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Code = CodeValue(code);
        Name = Required(name, nameof(name), 120);
        Description = Optional(description, nameof(description), 500);
        AllowsHierarchy = allowsHierarchy;
        Status = AccountingDimensionStatusValues.Normalize(status);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedByUserId = createdByUserId;
        CreatedUtc = Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool AllowsHierarchy { get; private set; }
    public string Status { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<AccountingDimensionMember> Members { get; } = new List<AccountingDimensionMember>();

    public void Apply(string name, string? description, bool allowsHierarchy, string status,
        DateOnly effectiveFrom, DateOnly? effectiveTo, DateTime updatedUtc)
    {
        ValidateDates(effectiveFrom, effectiveTo);
        Name = Required(name, nameof(name), 120);
        Description = Optional(description, nameof(description), 500);
        AllowsHierarchy = allowsHierarchy;
        Status = AccountingDimensionStatusValues.Normalize(status);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        UpdatedUtc = Utc(updatedUtc);
        Version++;
    }

    internal static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }

    internal static void ValidateDates(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveFrom == default) throw new ArgumentException("EffectiveFrom is required.", nameof(effectiveFrom));
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "EffectiveTo cannot be earlier than EffectiveFrom.");
    }

    internal static string CodeValue(string value) => Required(value, nameof(value), 64)
        .ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    internal static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var result = value.Trim();
        if (result.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return result;
    }
    internal static string? Optional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maxLength);
    internal static DateTime Utc(DateTime value) => EntityTimestampNormalizer.NormalizeUtc(value, nameof(value));
}

public sealed class AccountingDimensionMember : ICompanyOwnedEntity
{
    private AccountingDimensionMember() { }

    public AccountingDimensionMember(Guid id, Guid companyId, Guid dimensionTypeId, Guid? parentMemberId,
        string code, string name, string status, DateOnly effectiveFrom, DateOnly? effectiveTo,
        Guid createdByUserId, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId);
        if (dimensionTypeId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("DimensionTypeId and CreatedByUserId are required.");
        if (parentMemberId == Guid.Empty) throw new ArgumentException("ParentMemberId cannot be empty.", nameof(parentMemberId));
        AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DimensionTypeId = dimensionTypeId;
        ParentMemberId = parentMemberId;
        Code = AccountingDimensionType.Required(code, nameof(code), 64).ToUpperInvariant();
        Name = AccountingDimensionType.Required(name, nameof(name), 160);
        Status = AccountingDimensionStatusValues.Normalize(status);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedByUserId = createdByUserId;
        CreatedUtc = AccountingDimensionType.Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DimensionTypeId { get; private set; }
    public Guid? ParentMemberId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingDimensionType DimensionType { get; private set; } = null!;
    public AccountingDimensionMember? ParentMember { get; private set; }
    public ICollection<AccountingDimensionMember> Children { get; } = new List<AccountingDimensionMember>();

    public void Apply(Guid? parentMemberId, string name, string status, DateOnly effectiveFrom,
        DateOnly? effectiveTo, DateTime updatedUtc)
    {
        if (parentMemberId == Guid.Empty || parentMemberId == Id)
            throw new ArgumentException("The selected parent member is invalid.", nameof(parentMemberId));
        AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo);
        ParentMemberId = parentMemberId;
        Name = AccountingDimensionType.Required(name, nameof(name), 160);
        Status = AccountingDimensionStatusValues.Normalize(status);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        UpdatedUtc = AccountingDimensionType.Utc(updatedUtc);
        Version++;
    }
}

public sealed class AccountingDimensionAccountPolicy : ICompanyOwnedEntity
{
    private AccountingDimensionAccountPolicy() { }
    public AccountingDimensionAccountPolicy(Guid id, Guid companyId, Guid financeAccountId, Guid dimensionTypeId,
        string requirement, DateOnly effectiveFrom, DateOnly? effectiveTo, Guid createdByUserId, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId);
        if (financeAccountId == Guid.Empty || dimensionTypeId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Account, dimension type, and actor are required.");
        AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; FinanceAccountId = financeAccountId;
        DimensionTypeId = dimensionTypeId; Requirement = AccountingDimensionRequirementValues.Normalize(requirement);
        EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo; CreatedByUserId = createdByUserId;
        CreatedUtc = AccountingDimensionType.Utc(createdUtc); UpdatedUtc = CreatedUtc; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public Guid FinanceAccountId { get; private set; } public Guid DimensionTypeId { get; private set; }
    public string Requirement { get; private set; } = null!; public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; } public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; } public Company Company { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!; public AccountingDimensionType DimensionType { get; private set; } = null!;
    public void Apply(string requirement, DateOnly effectiveFrom, DateOnly? effectiveTo, DateTime updatedUtc)
    { AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo); Requirement = AccountingDimensionRequirementValues.Normalize(requirement);
        EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo; UpdatedUtc = AccountingDimensionType.Utc(updatedUtc); Version++; }
}

public sealed class AccountingDimensionCombinationRule : ICompanyOwnedEntity
{
    private AccountingDimensionCombinationRule() { }
    public AccountingDimensionCombinationRule(Guid id, Guid companyId, Guid leftMemberId, Guid rightMemberId,
        bool isAllowed, DateOnly effectiveFrom, DateOnly? effectiveTo, Guid createdByUserId, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId);
        if (leftMemberId == Guid.Empty || rightMemberId == Guid.Empty || leftMemberId == rightMemberId || createdByUserId == Guid.Empty)
            throw new ArgumentException("Two distinct dimension members and an actor are required.");
        AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; LeftMemberId = leftMemberId;
        RightMemberId = rightMemberId; IsAllowed = isAllowed; EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo;
        CreatedByUserId = createdByUserId; CreatedUtc = AccountingDimensionType.Utc(createdUtc); UpdatedUtc = CreatedUtc; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid LeftMemberId { get; private set; }
    public Guid RightMemberId { get; private set; } public bool IsAllowed { get; private set; } public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; } public Guid CreatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public long Version { get; private set; } public Company Company { get; private set; } = null!;
    public AccountingDimensionMember LeftMember { get; private set; } = null!; public AccountingDimensionMember RightMember { get; private set; } = null!;
    public void Apply(bool isAllowed, DateOnly effectiveFrom, DateOnly? effectiveTo, DateTime updatedUtc)
    { AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo); IsAllowed = isAllowed; EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo; UpdatedUtc = AccountingDimensionType.Utc(updatedUtc); Version++; }
}

public sealed class AccountingDimensionExternalMapping : ICompanyOwnedEntity
{
    private AccountingDimensionExternalMapping() { }
    public AccountingDimensionExternalMapping(Guid id, Guid companyId, string providerKey, string externalDimensionType,
        string externalValue, Guid dimensionTypeId, Guid dimensionMemberId, DateOnly effectiveFrom, DateOnly? effectiveTo,
        Guid createdByUserId, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId);
        if (dimensionTypeId == Guid.Empty || dimensionMemberId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Dimension mapping targets and actor are required.");
        AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        ProviderKey = AccountingDimensionType.CodeValue(providerKey); ExternalDimensionType = AccountingDimensionType.CodeValue(externalDimensionType);
        ExternalValue = AccountingDimensionType.Required(externalValue, nameof(externalValue), 160);
        DimensionTypeId = dimensionTypeId; DimensionMemberId = dimensionMemberId; EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo;
        CreatedByUserId = createdByUserId; CreatedUtc = AccountingDimensionType.Utc(createdUtc); UpdatedUtc = CreatedUtc; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string ProviderKey { get; private set; } = null!;
    public string ExternalDimensionType { get; private set; } = null!; public string ExternalValue { get; private set; } = null!;
    public Guid DimensionTypeId { get; private set; } public Guid DimensionMemberId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; } public DateOnly? EffectiveTo { get; private set; } public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public long Version { get; private set; }
    public Company Company { get; private set; } = null!; public AccountingDimensionType DimensionType { get; private set; } = null!;
    public AccountingDimensionMember DimensionMember { get; private set; } = null!;
    public void Apply(Guid dimensionTypeId, Guid dimensionMemberId, DateOnly effectiveFrom, DateOnly? effectiveTo, DateTime updatedUtc)
    { if (dimensionTypeId == Guid.Empty || dimensionMemberId == Guid.Empty) throw new ArgumentException("Mapping targets are required.");
        AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo); DimensionTypeId = dimensionTypeId; DimensionMemberId = dimensionMemberId;
        EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo; UpdatedUtc = AccountingDimensionType.Utc(updatedUtc); Version++; }
}

public sealed class AccountingDimensionMappingConflict : ICompanyOwnedEntity
{
    private AccountingDimensionMappingConflict() { }
    public AccountingDimensionMappingConflict(Guid id, Guid companyId, string providerKey, string externalDimensionType,
        string externalValue, string reasonCode, string explanation, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        ProviderKey = AccountingDimensionType.CodeValue(providerKey); ExternalDimensionType = AccountingDimensionType.CodeValue(externalDimensionType);
        ExternalValue = AccountingDimensionType.Required(externalValue, nameof(externalValue), 160);
        ReasonCode = AccountingDimensionType.CodeValue(reasonCode); Explanation = AccountingDimensionType.Required(explanation, nameof(explanation), 1000);
        Status = "open"; CreatedUtc = AccountingDimensionType.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string ProviderKey { get; private set; } = null!;
    public string ExternalDimensionType { get; private set; } = null!; public string ExternalValue { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!; public string Explanation { get; private set; } = null!;
    public string Status { get; private set; } = null!; public Guid? ResolvedDimensionMemberId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime? ResolvedUtc { get; private set; } public Company Company { get; private set; } = null!;
    public AccountingDimensionMember? ResolvedDimensionMember { get; private set; }
    public void Resolve(Guid memberId, DateTime resolvedUtc)
    { if (memberId == Guid.Empty) throw new ArgumentException("MemberId is required.", nameof(memberId));
        ResolvedDimensionMemberId = memberId; Status = "resolved"; ResolvedUtc = AccountingDimensionType.Utc(resolvedUtc); }
}

public sealed class AccountingAllocationTemplate : ICompanyOwnedEntity
{
    private AccountingAllocationTemplate() { }
    public AccountingAllocationTemplate(Guid id, Guid companyId, string code, string name, string status,
        decimal? approvalThreshold, Guid createdByUserId, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (createdByUserId == Guid.Empty) throw new ArgumentException("Actor is required.");
        if (approvalThreshold < 0) throw new ArgumentOutOfRangeException(nameof(approvalThreshold));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; Code = AccountingDimensionType.CodeValue(code);
        Name = AccountingDimensionType.Required(name, nameof(name), 160); Status = AccountingDimensionStatusValues.Normalize(status);
        ApprovalThreshold = approvalThreshold; CreatedByUserId = createdByUserId; CreatedUtc = AccountingDimensionType.Utc(createdUtc);
        UpdatedUtc = CreatedUtc; Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!; public string Status { get; private set; } = null!; public decimal? ApprovalThreshold { get; private set; }
    public Guid CreatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; } public Company Company { get; private set; } = null!;
    public ICollection<AccountingAllocationTemplateVersion> Versions { get; } = new List<AccountingAllocationTemplateVersion>();
    public void Apply(string name, string status, decimal? approvalThreshold, DateTime updatedUtc)
    { if (approvalThreshold < 0) throw new ArgumentOutOfRangeException(nameof(approvalThreshold)); Name = AccountingDimensionType.Required(name, nameof(name), 160);
        Status = AccountingDimensionStatusValues.Normalize(status); ApprovalThreshold = approvalThreshold; UpdatedUtc = AccountingDimensionType.Utc(updatedUtc); Version++; }
}

public sealed class AccountingAllocationTemplateVersion : ICompanyOwnedEntity
{
    private AccountingAllocationTemplateVersion() { }
    public AccountingAllocationTemplateVersion(Guid id, Guid companyId, Guid templateId, int versionNumber,
        DateOnly effectiveFrom, DateOnly? effectiveTo, int roundingPrecision, Guid createdByUserId, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (templateId == Guid.Empty || createdByUserId == Guid.Empty) throw new ArgumentException("Template and actor are required.");
        if (versionNumber <= 0) throw new ArgumentOutOfRangeException(nameof(versionNumber)); if (roundingPrecision is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(roundingPrecision));
        AccountingDimensionType.ValidateDates(effectiveFrom, effectiveTo); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        TemplateId = templateId; VersionNumber = versionNumber; EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo;
        RoundingPrecision = roundingPrecision; CreatedByUserId = createdByUserId; CreatedUtc = AccountingDimensionType.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid TemplateId { get; private set; }
    public int VersionNumber { get; private set; } public DateOnly EffectiveFrom { get; private set; } public DateOnly? EffectiveTo { get; private set; }
    public int RoundingPrecision { get; private set; } public Guid CreatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!; public AccountingAllocationTemplate Template { get; private set; } = null!;
    public ICollection<AccountingAllocationTemplateLine> Lines { get; } = new List<AccountingAllocationTemplateLine>();
}

public sealed class AccountingAllocationTemplateLine : ICompanyOwnedEntity
{
    private AccountingAllocationTemplateLine() { }
    public AccountingAllocationTemplateLine(Guid id, Guid companyId, Guid templateVersionId, int sequence,
        Guid dimensionMemberId, string allocationKind, decimal value, string? basis)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (templateVersionId == Guid.Empty || dimensionMemberId == Guid.Empty) throw new ArgumentException("Template version and member are required.");
        if (sequence <= 0 || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId; TemplateVersionId = templateVersionId; Sequence = sequence; DimensionMemberId = dimensionMemberId;
        AllocationKind = AccountingAllocationKindValues.Normalize(allocationKind); Value = value;
        Basis = AccountingDimensionType.Optional(basis, nameof(basis), 160);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid TemplateVersionId { get; private set; }
    public int Sequence { get; private set; } public Guid DimensionMemberId { get; private set; } public string AllocationKind { get; private set; } = null!;
    public decimal Value { get; private set; } public string? Basis { get; private set; } public Company Company { get; private set; } = null!;
    public AccountingAllocationTemplateVersion TemplateVersion { get; private set; } = null!; public AccountingDimensionMember DimensionMember { get; private set; } = null!;
}

public sealed class AccountingAllocationApplication : ICompanyOwnedEntity
{
    private AccountingAllocationApplication() { }
    public AccountingAllocationApplication(Guid id, Guid companyId, Guid templateId, Guid templateVersionId,
        string sourceType, string sourceId, string sourceVersion, string idempotencyKey, string payloadHash,
        decimal sourceAmount, decimal allocatedAmount, string currency, Guid actorUserId, Guid? approvalRequestId, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (templateId == Guid.Empty || templateVersionId == Guid.Empty || actorUserId == Guid.Empty)
            throw new ArgumentException("Template, version, and actor are required.");
        if (approvalRequestId == Guid.Empty) throw new ArgumentException("ApprovalRequestId cannot be empty.", nameof(approvalRequestId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; TemplateId = templateId; TemplateVersionId = templateVersionId;
        SourceType = AccountingDimensionType.CodeValue(sourceType); SourceId = AccountingDimensionType.Required(sourceId, nameof(sourceId), 160);
        SourceVersion = AccountingDimensionType.Required(sourceVersion, nameof(sourceVersion), 128); IdempotencyKey = AccountingDimensionType.Required(idempotencyKey, nameof(idempotencyKey), 200);
        PayloadHash = AccountingDimensionType.Required(payloadHash, nameof(payloadHash), 64).ToLowerInvariant(); SourceAmount = sourceAmount; AllocatedAmount = allocatedAmount;
        Currency = AccountingDimensionType.Required(currency, nameof(currency), 3).ToUpperInvariant(); ActorUserId = actorUserId; ApprovalRequestId = approvalRequestId;
        CreatedUtc = AccountingDimensionType.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid TemplateId { get; private set; }
    public Guid TemplateVersionId { get; private set; } public string SourceType { get; private set; } = null!; public string SourceId { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!; public string PayloadHash { get; private set; } = null!;
    public decimal SourceAmount { get; private set; } public decimal AllocatedAmount { get; private set; } public string Currency { get; private set; } = null!;
    public Guid ActorUserId { get; private set; } public Guid? ApprovalRequestId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!; public AccountingAllocationTemplate Template { get; private set; } = null!;
    public AccountingAllocationTemplateVersion TemplateVersion { get; private set; } = null!; public ApprovalRequest? ApprovalRequest { get; private set; }
    public ICollection<AccountingAllocationApplicationLine> Lines { get; } = new List<AccountingAllocationApplicationLine>();
    public ICollection<AccountingAllocationEvidenceLink> EvidenceLinks { get; } = new List<AccountingAllocationEvidenceLink>();
}

public sealed class AccountingAllocationApplicationLine : ICompanyOwnedEntity
{
    private AccountingAllocationApplicationLine() { }
    public AccountingAllocationApplicationLine(Guid id, Guid companyId, Guid applicationId, int sequence,
        Guid dimensionMemberId, string allocationKind, decimal driverValue, decimal rawAmount, decimal roundedAmount, decimal roundingResidual)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (applicationId == Guid.Empty || dimensionMemberId == Guid.Empty || sequence <= 0) throw new ArgumentException("Application, member, and sequence are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ApplicationId = applicationId; Sequence = sequence;
        DimensionMemberId = dimensionMemberId; AllocationKind = AccountingAllocationKindValues.Normalize(allocationKind); DriverValue = driverValue;
        RawAmount = rawAmount; RoundedAmount = roundedAmount; RoundingResidual = roundingResidual;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ApplicationId { get; private set; }
    public int Sequence { get; private set; } public Guid DimensionMemberId { get; private set; } public string AllocationKind { get; private set; } = null!;
    public decimal DriverValue { get; private set; } public decimal RawAmount { get; private set; } public decimal RoundedAmount { get; private set; }
    public decimal RoundingResidual { get; private set; } public Company Company { get; private set; } = null!;
    public AccountingAllocationApplication Application { get; private set; } = null!; public AccountingDimensionMember DimensionMember { get; private set; } = null!;
}

public sealed class AccountingAllocationEvidenceLink : ICompanyOwnedEntity
{
    private AccountingAllocationEvidenceLink() { }
    public AccountingAllocationEvidenceLink(Guid id, Guid companyId, Guid applicationId, Guid documentId,
        string contentHash, string title, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (applicationId == Guid.Empty || documentId == Guid.Empty) throw new ArgumentException("Application and document are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ApplicationId = applicationId; DocumentId = documentId;
        ContentHash = AccountingDimensionType.Required(contentHash, nameof(contentHash), 128).ToLowerInvariant(); Title = AccountingDimensionType.Required(title, nameof(title), 300);
        CreatedUtc = AccountingDimensionType.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ApplicationId { get; private set; }
    public Guid DocumentId { get; private set; } public string ContentHash { get; private set; } = null!; public string Title { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; } public Company Company { get; private set; } = null!;
    public AccountingAllocationApplication Application { get; private set; } = null!; public CompanyKnowledgeDocument Document { get; private set; } = null!;
}

public sealed class LedgerEntryLineDimension : ICompanyOwnedEntity
{
    private LedgerEntryLineDimension() { }
    public LedgerEntryLineDimension(Guid id, Guid companyId, Guid ledgerEntryLineId, Guid dimensionTypeId,
        Guid dimensionMemberId, string dimensionTypeCodeSnapshot, string dimensionTypeNameSnapshot,
        string memberCodeSnapshot, string memberNameSnapshot, string hierarchyPathSnapshot, DateTime createdUtc)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (ledgerEntryLineId == Guid.Empty || dimensionTypeId == Guid.Empty || dimensionMemberId == Guid.Empty)
            throw new ArgumentException("Ledger line, dimension type, and member are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; LedgerEntryLineId = ledgerEntryLineId;
        DimensionTypeId = dimensionTypeId; DimensionMemberId = dimensionMemberId;
        DimensionTypeCodeSnapshot = AccountingDimensionType.Required(dimensionTypeCodeSnapshot, nameof(dimensionTypeCodeSnapshot), 64);
        DimensionTypeNameSnapshot = AccountingDimensionType.Required(dimensionTypeNameSnapshot, nameof(dimensionTypeNameSnapshot), 120);
        MemberCodeSnapshot = AccountingDimensionType.Required(memberCodeSnapshot, nameof(memberCodeSnapshot), 64);
        MemberNameSnapshot = AccountingDimensionType.Required(memberNameSnapshot, nameof(memberNameSnapshot), 160);
        HierarchyPathSnapshot = AccountingDimensionType.Required(hierarchyPathSnapshot, nameof(hierarchyPathSnapshot), 1000);
        CreatedUtc = AccountingDimensionType.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid LedgerEntryLineId { get; private set; }
    public Guid DimensionTypeId { get; private set; } public Guid DimensionMemberId { get; private set; }
    public string DimensionTypeCodeSnapshot { get; private set; } = null!; public string DimensionTypeNameSnapshot { get; private set; } = null!;
    public string MemberCodeSnapshot { get; private set; } = null!; public string MemberNameSnapshot { get; private set; } = null!;
    public string HierarchyPathSnapshot { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!; public LedgerEntryLine LedgerEntryLine { get; private set; } = null!;
    public AccountingDimensionType DimensionType { get; private set; } = null!; public AccountingDimensionMember DimensionMember { get; private set; } = null!;
}

public sealed class ManualJournalDraftLineDimension : ICompanyOwnedEntity
{
    private ManualJournalDraftLineDimension() { }
    public ManualJournalDraftLineDimension(Guid id, Guid companyId, Guid manualJournalDraftLineId, Guid dimensionMemberId)
    {
        AccountingDimensionType.EnsureCompany(companyId); if (manualJournalDraftLineId == Guid.Empty || dimensionMemberId == Guid.Empty)
            throw new ArgumentException("Manual journal line and dimension member are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ManualJournalDraftLineId = manualJournalDraftLineId; DimensionMemberId = dimensionMemberId;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ManualJournalDraftLineId { get; private set; }
    public Guid DimensionMemberId { get; private set; } public Company Company { get; private set; } = null!;
    public ManualJournalDraftLine ManualJournalDraftLine { get; private set; } = null!; public AccountingDimensionMember DimensionMember { get; private set; } = null!;
}
