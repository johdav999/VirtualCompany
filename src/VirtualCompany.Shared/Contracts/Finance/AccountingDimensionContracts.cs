namespace VirtualCompany.Application.Finance;
public sealed record AccountingAllocationApplicationDto(Guid Id, Guid TemplateId, Guid TemplateVersionId, string SourceType, string SourceId, string SourceVersion, string IdempotencyKey, string PayloadHash, decimal SourceAmount, decimal AllocatedAmount, string Currency, Guid? ApprovalRequestId, bool IsIdempotentReplay, DateTime CreatedUtc, IReadOnlyList<AccountingAllocationPreviewLineDto> Lines)
{
    public Guid Id { get; set; } = Id;
    public Guid TemplateId { get; set; } = TemplateId;
    public Guid TemplateVersionId { get; set; } = TemplateVersionId;
    public string SourceType { get; set; } = SourceType;
    public string SourceId { get; set; } = SourceId;
    public string SourceVersion { get; set; } = SourceVersion;
    public string IdempotencyKey { get; set; } = IdempotencyKey;
    public string PayloadHash { get; set; } = PayloadHash;
    public decimal SourceAmount { get; set; } = SourceAmount;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public string Currency { get; set; } = Currency;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public bool IsIdempotentReplay { get; set; } = IsIdempotentReplay;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public IReadOnlyList<AccountingAllocationPreviewLineDto> Lines { get; set; } = Lines;

    public AccountingAllocationApplicationDto() : this(default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingDimensionAccountPolicyDto(Guid Id, Guid FinanceAccountId, string AccountCode, string AccountName, Guid DimensionTypeId, string DimensionTypeCode, string Requirement, DateOnly EffectiveFrom, DateOnly? EffectiveTo, long Version)
{
    public Guid Id { get; set; } = Id;
    public Guid FinanceAccountId { get; set; } = FinanceAccountId;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public Guid DimensionTypeId { get; set; } = DimensionTypeId;
    public string DimensionTypeCode { get; set; } = DimensionTypeCode;
    public string Requirement { get; set; } = Requirement;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public long Version { get; set; } = Version;

    public AccountingDimensionAccountPolicyDto() : this(default !, default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record AccountingDimensionMappingConflictDto(Guid Id, string ProviderKey, string ExternalDimensionType, string ExternalValue, string ReasonCode, string Explanation, string Status, Guid? ResolvedDimensionMemberId, DateTime CreatedUtc, DateTime? ResolvedUtc)
{
    public Guid Id { get; set; } = Id;
    public string ProviderKey { get; set; } = ProviderKey;
    public string ExternalDimensionType { get; set; } = ExternalDimensionType;
    public string ExternalValue { get; set; } = ExternalValue;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string Status { get; set; } = Status;
    public Guid? ResolvedDimensionMemberId { get; set; } = ResolvedDimensionMemberId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? ResolvedUtc { get; set; } = ResolvedUtc;

    public AccountingDimensionMappingConflictDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record ResolvedAccountingDimensionAssignment(Guid DimensionTypeId, string DimensionTypeCode, string DimensionTypeName, Guid DimensionMemberId, string MemberCode, string MemberName, string HierarchyPath)
{
    public Guid DimensionTypeId { get; set; } = DimensionTypeId;
    public string DimensionTypeCode { get; set; } = DimensionTypeCode;
    public string DimensionTypeName { get; set; } = DimensionTypeName;
    public Guid DimensionMemberId { get; set; } = DimensionMemberId;
    public string MemberCode { get; set; } = MemberCode;
    public string MemberName { get; set; } = MemberName;
    public string HierarchyPath { get; set; } = HierarchyPath;

    public ResolvedAccountingDimensionAssignment() : this(default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record AccountingAllocationTemplateDto(Guid Id, string Code, string Name, string Status, decimal? ApprovalThreshold, long Version, Guid? CurrentVersionId, int? CurrentVersionNumber, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, int RoundingPrecision, IReadOnlyList<AccountingAllocationTemplateLineDto> Lines)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string Status { get; set; } = Status;
    public decimal? ApprovalThreshold { get; set; } = ApprovalThreshold;
    public long Version { get; set; } = Version;
    public Guid? CurrentVersionId { get; set; } = CurrentVersionId;
    public int? CurrentVersionNumber { get; set; } = CurrentVersionNumber;
    public DateOnly? EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public int RoundingPrecision { get; set; } = RoundingPrecision;
    public IReadOnlyList<AccountingAllocationTemplateLineDto> Lines { get; set; } = Lines;

    public AccountingAllocationTemplateDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingDimensionTypeDto(Guid Id, string Code, string Name, string? Description, bool AllowsHierarchy, string Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo, long Version, IReadOnlyList<AccountingDimensionMemberDto> Members)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string? Description { get; set; } = Description;
    public bool AllowsHierarchy { get; set; } = AllowsHierarchy;
    public string Status { get; set; } = Status;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public long Version { get; set; } = Version;
    public IReadOnlyList<AccountingDimensionMemberDto> Members { get; set; } = Members;

    public AccountingDimensionTypeDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingAllocationPreviewDto(Guid TemplateId, Guid TemplateVersionId, int TemplateVersionNumber, decimal SourceAmount, decimal AllocatedAmount, decimal Difference, string Currency, int RoundingPrecision, bool RequiresApproval, IReadOnlyList<AccountingAllocationPreviewLineDto> Lines, IReadOnlyList<AccountingPostingIssue> Issues)
{
    public Guid TemplateId { get; set; } = TemplateId;
    public Guid TemplateVersionId { get; set; } = TemplateVersionId;
    public int TemplateVersionNumber { get; set; } = TemplateVersionNumber;
    public decimal SourceAmount { get; set; } = SourceAmount;
    public decimal AllocatedAmount { get; set; } = AllocatedAmount;
    public decimal Difference { get; set; } = Difference;
    public string Currency { get; set; } = Currency;
    public int RoundingPrecision { get; set; } = RoundingPrecision;
    public bool RequiresApproval { get; set; } = RequiresApproval;
    public IReadOnlyList<AccountingAllocationPreviewLineDto> Lines { get; set; } = Lines;
    public IReadOnlyList<AccountingPostingIssue> Issues { get; set; } = Issues;

    public AccountingAllocationPreviewDto() : this(default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, [], [])
    {
    }
}

public sealed record AccountingAllocationTemplateLineDto(Guid Id, int Sequence, Guid DimensionMemberId, string DimensionDisplay, string AllocationKind, decimal Value, string? Basis)
{
    public Guid Id { get; set; } = Id;
    public int Sequence { get; set; } = Sequence;
    public Guid DimensionMemberId { get; set; } = DimensionMemberId;
    public string DimensionDisplay { get; set; } = DimensionDisplay;
    public string AllocationKind { get; set; } = AllocationKind;
    public decimal Value { get; set; } = Value;
    public string? Basis { get; set; } = Basis;

    public AccountingAllocationTemplateLineDto() : this(default !, default !, default !, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingDimensionCombinationRuleDto(Guid Id, Guid LeftMemberId, string LeftDisplay, Guid RightMemberId, string RightDisplay, bool IsAllowed, DateOnly EffectiveFrom, DateOnly? EffectiveTo, long Version)
{
    public Guid Id { get; set; } = Id;
    public Guid LeftMemberId { get; set; } = LeftMemberId;
    public string LeftDisplay { get; set; } = LeftDisplay;
    public Guid RightMemberId { get; set; } = RightMemberId;
    public string RightDisplay { get; set; } = RightDisplay;
    public bool IsAllowed { get; set; } = IsAllowed;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public long Version { get; set; } = Version;

    public AccountingDimensionCombinationRuleDto() : this(default !, default !, string.Empty, default !, string.Empty, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingDimensionWorkspaceDto(IReadOnlyList<AccountingDimensionTypeDto> DimensionTypes, IReadOnlyList<AccountingDimensionAccountPolicyDto> AccountPolicies, IReadOnlyList<AccountingDimensionCombinationRuleDto> CombinationRules, IReadOnlyList<AccountingDimensionExternalMappingDto> ExternalMappings, IReadOnlyList<AccountingDimensionMappingConflictDto> MappingConflicts, IReadOnlyList<AccountingAllocationTemplateDto> AllocationTemplates, int ActiveDimensionCount, int ActiveMemberCount, int RequiredAccountRuleCount, int OpenMappingConflictCount)
{
    public IReadOnlyList<AccountingDimensionTypeDto> DimensionTypes { get; set; } = DimensionTypes;
    public IReadOnlyList<AccountingDimensionAccountPolicyDto> AccountPolicies { get; set; } = AccountPolicies;
    public IReadOnlyList<AccountingDimensionCombinationRuleDto> CombinationRules { get; set; } = CombinationRules;
    public IReadOnlyList<AccountingDimensionExternalMappingDto> ExternalMappings { get; set; } = ExternalMappings;
    public IReadOnlyList<AccountingDimensionMappingConflictDto> MappingConflicts { get; set; } = MappingConflicts;
    public IReadOnlyList<AccountingAllocationTemplateDto> AllocationTemplates { get; set; } = AllocationTemplates;
    public int ActiveDimensionCount { get; set; } = ActiveDimensionCount;
    public int ActiveMemberCount { get; set; } = ActiveMemberCount;
    public int RequiredAccountRuleCount { get; set; } = RequiredAccountRuleCount;
    public int OpenMappingConflictCount { get; set; } = OpenMappingConflictCount;

    public AccountingDimensionWorkspaceDto() : this([], [], [], [], [], [], default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingDimensionMemberDto(Guid Id, Guid DimensionTypeId, Guid? ParentMemberId, string Code, string Name, string Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string HierarchyPath, long Version)
{
    public Guid Id { get; set; } = Id;
    public Guid DimensionTypeId { get; set; } = DimensionTypeId;
    public Guid? ParentMemberId { get; set; } = ParentMemberId;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string Status { get; set; } = Status;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public string HierarchyPath { get; set; } = HierarchyPath;
    public long Version { get; set; } = Version;

    public AccountingDimensionMemberDto() : this(default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record AccountingDimensionReportDto(Guid DimensionTypeId, Guid DimensionMemberId, string DimensionTypeCode, string DimensionMemberCode, string DimensionMemberName, decimal TotalDebit, decimal TotalCredit, decimal NetAmount, int TotalLineCount, int Skip, int Take, IReadOnlyList<AccountingDimensionReportLineDto> Lines)
{
    public Guid DimensionTypeId { get; set; } = DimensionTypeId;
    public Guid DimensionMemberId { get; set; } = DimensionMemberId;
    public string DimensionTypeCode { get; set; } = DimensionTypeCode;
    public string DimensionMemberCode { get; set; } = DimensionMemberCode;
    public string DimensionMemberName { get; set; } = DimensionMemberName;
    public decimal TotalDebit { get; set; } = TotalDebit;
    public decimal TotalCredit { get; set; } = TotalCredit;
    public decimal NetAmount { get; set; } = NetAmount;
    public int TotalLineCount { get; set; } = TotalLineCount;
    public int Skip { get; set; } = Skip;
    public int Take { get; set; } = Take;
    public IReadOnlyList<AccountingDimensionReportLineDto> Lines { get; set; } = Lines;

    public AccountingDimensionReportDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingDimensionExternalMappingDto(Guid Id, string ProviderKey, string ExternalDimensionType, string ExternalValue, Guid DimensionTypeId, Guid DimensionMemberId, string MemberDisplay, DateOnly EffectiveFrom, DateOnly? EffectiveTo, long Version)
{
    public Guid Id { get; set; } = Id;
    public string ProviderKey { get; set; } = ProviderKey;
    public string ExternalDimensionType { get; set; } = ExternalDimensionType;
    public string ExternalValue { get; set; } = ExternalValue;
    public Guid DimensionTypeId { get; set; } = DimensionTypeId;
    public Guid DimensionMemberId { get; set; } = DimensionMemberId;
    public string MemberDisplay { get; set; } = MemberDisplay;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public long Version { get; set; } = Version;

    public AccountingDimensionExternalMappingDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record AccountingDimensionReportLineDto(Guid LedgerEntryId, Guid LedgerEntryLineId, string EntryNumber, DateOnly PostingDate, string AccountCode, string AccountName, decimal DebitAmount, decimal CreditAmount, string Currency, string? Description, string DimensionTypeCodeSnapshot, string DimensionMemberCodeSnapshot, string DimensionMemberNameSnapshot, string HierarchyPathSnapshot)
{
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public Guid LedgerEntryLineId { get; set; } = LedgerEntryLineId;
    public string EntryNumber { get; set; } = EntryNumber;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public string AccountCode { get; set; } = AccountCode;
    public string AccountName { get; set; } = AccountName;
    public decimal DebitAmount { get; set; } = DebitAmount;
    public decimal CreditAmount { get; set; } = CreditAmount;
    public string Currency { get; set; } = Currency;
    public string? Description { get; set; } = Description;
    public string DimensionTypeCodeSnapshot { get; set; } = DimensionTypeCodeSnapshot;
    public string DimensionMemberCodeSnapshot { get; set; } = DimensionMemberCodeSnapshot;
    public string DimensionMemberNameSnapshot { get; set; } = DimensionMemberNameSnapshot;
    public string HierarchyPathSnapshot { get; set; } = HierarchyPathSnapshot;

    public AccountingDimensionReportLineDto() : this(default !, default !, string.Empty, default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record AccountingAllocationPreviewLineDto(int Sequence, Guid DimensionMemberId, string DimensionDisplay, string AllocationKind, decimal DriverValue, decimal RawAmount, decimal RoundedAmount, decimal RoundingResidual)
{
    public int Sequence { get; set; } = Sequence;
    public Guid DimensionMemberId { get; set; } = DimensionMemberId;
    public string DimensionDisplay { get; set; } = DimensionDisplay;
    public string AllocationKind { get; set; } = AllocationKind;
    public decimal DriverValue { get; set; } = DriverValue;
    public decimal RawAmount { get; set; } = RawAmount;
    public decimal RoundedAmount { get; set; } = RoundedAmount;
    public decimal RoundingResidual { get; set; } = RoundingResidual;

    public AccountingAllocationPreviewLineDto() : this(default !, default !, string.Empty, string.Empty, default !, default !, default !, default !)
    {
    }
}
