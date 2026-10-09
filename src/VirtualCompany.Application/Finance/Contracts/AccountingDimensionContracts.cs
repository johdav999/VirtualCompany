namespace VirtualCompany.Application.Finance;

public static class AccountingDimensionCodes
{
    public const string CostCenter = "cost_center";
    public const string Project = "project";
}

public static class AccountingDimensionLifecycleStatuses
{
    public const string Active = "active";
    public const string Archived = "archived";
}

public static class AccountingDimensionRequirementValues
{
    public const string Optional = "optional";
    public const string Required = "required";
    public const string Prohibited = "prohibited";
}

public static class AccountingAllocationKindValues
{
    public const string Percentage = "percentage";
    public const string Fixed = "fixed";
}

public static class AccountingDimensionReasonCodes
{
    public const string NotFound = "accounting_dimension_not_found";
    public const string Invalid = "accounting_dimension_invalid";
    public const string Inactive = "accounting_dimension_inactive";
    public const string Required = "accounting_dimension_required";
    public const string Prohibited = "accounting_dimension_prohibited";
    public const string CombinationInvalid = "accounting_dimension_combination_invalid";
    public const string MappingConflict = "accounting_dimension_mapping_conflict";
    public const string HierarchyInvalid = "accounting_dimension_hierarchy_invalid";
    public const string VersionConflict = "accounting_dimension_version_conflict";
    public const string IdempotencyConflict = "accounting_allocation_idempotency_conflict";
    public const string AllocationInvalid = "accounting_allocation_invalid";
    public const string AllocationApprovalRequired = "accounting_allocation_approval_required";
}

public sealed record AccountingDimensionPostingDecision(
    IReadOnlyList<AccountingPostingIssue> Issues,
    IReadOnlyDictionary<int, IReadOnlyList<ResolvedAccountingDimensionAssignment>> AssignmentsByLine);

public interface IAccountingDimensionPostingPolicy
{
    Task<AccountingDimensionPostingDecision> EvaluateAsync(
        ProposedAccountingEntry entry,
        CancellationToken cancellationToken);
}

public sealed record SaveAccountingDimensionTypeCommand(
    Guid CompanyId,
    Guid? DimensionTypeId,
    string Code,
    string Name,
    string? Description,
    bool AllowsHierarchy,
    string Status,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long? ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record SaveAccountingDimensionMemberCommand(
    Guid CompanyId,
    Guid DimensionTypeId,
    Guid? DimensionMemberId,
    Guid? ParentMemberId,
    string Code,
    string Name,
    string Status,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long? ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record SaveAccountingDimensionAccountPolicyCommand(
    Guid CompanyId,
    Guid? PolicyId,
    Guid FinanceAccountId,
    Guid DimensionTypeId,
    string Requirement,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long? ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record SaveAccountingDimensionCombinationRuleCommand(
    Guid CompanyId,
    Guid? RuleId,
    Guid LeftMemberId,
    Guid RightMemberId,
    bool IsAllowed,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long? ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record SaveAccountingDimensionExternalMappingCommand(
    Guid CompanyId,
    Guid? MappingId,
    string ProviderKey,
    string ExternalDimensionType,
    string ExternalValue,
    Guid DimensionTypeId,
    Guid DimensionMemberId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long? ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record AccountingAllocationTemplateLineInput(
    Guid DimensionMemberId,
    string AllocationKind,
    decimal Value,
    string? Basis = null);

public sealed record SaveAccountingAllocationTemplateVersionCommand(
    Guid CompanyId,
    Guid? TemplateId,
    string Code,
    string Name,
    string Status,
    decimal? ApprovalThreshold,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int RoundingPrecision,
    IReadOnlyList<AccountingAllocationTemplateLineInput> Lines,
    long? ExpectedTemplateVersion,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record PreviewAccountingAllocationQuery(
    Guid CompanyId,
    Guid TemplateId,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate);

public sealed record ApplyAccountingAllocationCommand(
    Guid CompanyId,
    Guid TemplateId,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate,
    string SourceType,
    string SourceId,
    string SourceVersion,
    string IdempotencyKey,
    Guid ActorUserId,
    Guid? ApprovalRequestId = null,
    IReadOnlyList<ProposedAccountingEvidence>? Evidence = null,
    string? CorrelationId = null);

public sealed record GetAccountingDimensionReportQuery(
    Guid CompanyId,
    Guid DimensionMemberId,
    DateOnly? From = null,
    DateOnly? To = null,
    int Skip = 0,
    int Take = 250);

public interface IAccountingDimensionService
{
    Task<AccountingDimensionWorkspaceDto> GetWorkspaceAsync(Guid companyId, CancellationToken cancellationToken);
    Task<AccountingDimensionTypeDto> SaveTypeAsync(SaveAccountingDimensionTypeCommand command, CancellationToken cancellationToken);
    Task<AccountingDimensionMemberDto> SaveMemberAsync(SaveAccountingDimensionMemberCommand command, CancellationToken cancellationToken);
    Task<AccountingDimensionAccountPolicyDto> SaveAccountPolicyAsync(SaveAccountingDimensionAccountPolicyCommand command, CancellationToken cancellationToken);
    Task<AccountingDimensionCombinationRuleDto> SaveCombinationRuleAsync(SaveAccountingDimensionCombinationRuleCommand command, CancellationToken cancellationToken);
    Task<AccountingDimensionExternalMappingDto> SaveExternalMappingAsync(SaveAccountingDimensionExternalMappingCommand command, CancellationToken cancellationToken);
    Task<AccountingAllocationTemplateDto> SaveAllocationTemplateVersionAsync(SaveAccountingAllocationTemplateVersionCommand command, CancellationToken cancellationToken);
    Task<AccountingAllocationPreviewDto> PreviewAllocationAsync(PreviewAccountingAllocationQuery query, CancellationToken cancellationToken);
    Task<AccountingAllocationApplicationDto> ApplyAllocationAsync(ApplyAccountingAllocationCommand command, CancellationToken cancellationToken);
    Task<AccountingDimensionReportDto> GetReportAsync(GetAccountingDimensionReportQuery query, CancellationToken cancellationToken);
}

public sealed class AccountingDimensionException : Exception
{
    public AccountingDimensionException(string reasonCode, string message, bool isConflict = false, long? currentVersion = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        IsConflict = isConflict;
        CurrentVersion = currentVersion;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
