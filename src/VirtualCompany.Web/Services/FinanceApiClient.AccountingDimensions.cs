namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingDimensionWorkspaceResponse?> GetAccountingDimensionWorkspaceAsync(Guid companyId,
        CancellationToken cancellationToken = default) => GetAsync<AccountingDimensionWorkspaceResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/dimensions/workspace", false, cancellationToken);

    public Task<AccountingDimensionTypeResponse> SaveAccountingDimensionTypeAsync(Guid companyId,
        SaveAccountingDimensionTypeApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionTypeApiRequest, AccountingDimensionTypeResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/types", request, cancellationToken); }

    public Task<AccountingDimensionMemberResponse> SaveAccountingDimensionMemberAsync(Guid companyId,
        SaveAccountingDimensionMemberApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionMemberApiRequest, AccountingDimensionMemberResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/members", request, cancellationToken); }

    public Task<AccountingDimensionAccountPolicyResponse> SaveAccountingDimensionAccountPolicyAsync(Guid companyId,
        SaveAccountingDimensionAccountPolicyApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionAccountPolicyApiRequest, AccountingDimensionAccountPolicyResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/account-policies", request, cancellationToken); }

    public Task<AccountingDimensionExternalMappingResponse> SaveAccountingDimensionExternalMappingAsync(Guid companyId,
        SaveAccountingDimensionExternalMappingApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionExternalMappingApiRequest, AccountingDimensionExternalMappingResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/external-mappings", request, cancellationToken); }

    public Task<AccountingDimensionCombinationRuleResponse> SaveAccountingDimensionCombinationRuleAsync(Guid companyId,
        SaveAccountingDimensionCombinationRuleApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingDimensionCombinationRuleApiRequest, AccountingDimensionCombinationRuleResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/combination-rules", request, cancellationToken); }

    public Task<AccountingAllocationTemplateResponse> SaveAccountingAllocationTemplateAsync(Guid companyId,
        SaveAccountingAllocationTemplateApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveAccountingAllocationTemplateApiRequest, AccountingAllocationTemplateResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/allocation-templates", request, cancellationToken); }

    public Task<AccountingAllocationPreviewResponse> PreviewAccountingAllocationAsync(Guid companyId,
        PreviewAccountingAllocationApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PreviewAccountingAllocationApiRequest, AccountingAllocationPreviewResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/allocations/preview", request, cancellationToken);

    public Task<AccountingAllocationApplicationResponse> ApplyAccountingAllocationAsync(Guid companyId,
        ApplyAccountingAllocationApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<ApplyAccountingAllocationApiRequest, AccountingAllocationApplicationResponse>(companyId,
        HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/dimensions/allocations", request, cancellationToken); }

    public Task<AccountingDimensionReportResponse?> GetAccountingDimensionReportAsync(Guid companyId, Guid memberId,
        DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingDimensionReportResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/dimensions/members/{memberId}/report" + BuildQuery(
                ("from", from?.ToString("yyyy-MM-dd")), ("to", to?.ToString("yyyy-MM-dd"))), false, cancellationToken);
}

public sealed class AccountingDimensionWorkspaceResponse
{
    public List<AccountingDimensionTypeResponse> DimensionTypes { get; set; } = [];
    public List<AccountingDimensionAccountPolicyResponse> AccountPolicies { get; set; } = [];
    public List<AccountingDimensionCombinationRuleResponse> CombinationRules { get; set; } = [];
    public List<AccountingDimensionExternalMappingResponse> ExternalMappings { get; set; } = [];
    public List<AccountingDimensionMappingConflictResponse> MappingConflicts { get; set; } = [];
    public List<AccountingAllocationTemplateResponse> AllocationTemplates { get; set; } = [];
    public int ActiveDimensionCount { get; set; } public int ActiveMemberCount { get; set; }
    public int RequiredAccountRuleCount { get; set; } public int OpenMappingConflictCount { get; set; }
}
public sealed class AccountingDimensionTypeResponse
{
    public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string? Description { get; set; } public bool AllowsHierarchy { get; set; } public string Status { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long Version { get; set; }
    public List<AccountingDimensionMemberResponse> Members { get; set; } = [];
}
public sealed class AccountingDimensionMemberResponse
{
    public Guid Id { get; set; } public Guid DimensionTypeId { get; set; } public Guid? ParentMemberId { get; set; }
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Status { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public string HierarchyPath { get; set; } = string.Empty;
    public long Version { get; set; }
}
public sealed class AccountingDimensionAccountPolicyResponse
{
    public Guid Id { get; set; } public Guid FinanceAccountId { get; set; } public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty; public Guid DimensionTypeId { get; set; } public string DimensionTypeCode { get; set; } = string.Empty;
    public string Requirement { get; set; } = string.Empty; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long Version { get; set; }
}
public sealed class AccountingDimensionCombinationRuleResponse
{
    public Guid Id { get; set; } public Guid LeftMemberId { get; set; } public string LeftDisplay { get; set; } = string.Empty;
    public Guid RightMemberId { get; set; } public string RightDisplay { get; set; } = string.Empty; public bool IsAllowed { get; set; }
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long Version { get; set; }
}
public sealed class AccountingDimensionExternalMappingResponse
{
    public Guid Id { get; set; } public string ProviderKey { get; set; } = string.Empty; public string ExternalDimensionType { get; set; } = string.Empty;
    public string ExternalValue { get; set; } = string.Empty; public Guid DimensionTypeId { get; set; } public Guid DimensionMemberId { get; set; }
    public string MemberDisplay { get; set; } = string.Empty; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long Version { get; set; }
}
public sealed class AccountingDimensionMappingConflictResponse
{
    public Guid Id { get; set; } public string ProviderKey { get; set; } = string.Empty; public string ExternalDimensionType { get; set; } = string.Empty;
    public string ExternalValue { get; set; } = string.Empty; public string ReasonCode { get; set; } = string.Empty; public string Explanation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; public Guid? ResolvedDimensionMemberId { get; set; } public DateTime CreatedUtc { get; set; } public DateTime? ResolvedUtc { get; set; }
}
public sealed class AccountingAllocationTemplateResponse
{
    public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; public decimal? ApprovalThreshold { get; set; } public long Version { get; set; }
    public Guid? CurrentVersionId { get; set; } public int? CurrentVersionNumber { get; set; } public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; } public int RoundingPrecision { get; set; } public List<AccountingAllocationTemplateLineResponse> Lines { get; set; } = [];
}
public sealed class AccountingAllocationTemplateLineResponse
{
    public Guid Id { get; set; } public int Sequence { get; set; } public Guid DimensionMemberId { get; set; }
    public string DimensionDisplay { get; set; } = string.Empty; public string AllocationKind { get; set; } = string.Empty;
    public decimal Value { get; set; } public string? Basis { get; set; }
}
public sealed class AccountingAllocationPreviewResponse
{
    public Guid TemplateId { get; set; } public Guid TemplateVersionId { get; set; } public int TemplateVersionNumber { get; set; }
    public decimal SourceAmount { get; set; } public decimal AllocatedAmount { get; set; } public decimal Difference { get; set; }
    public string Currency { get; set; } = string.Empty; public int RoundingPrecision { get; set; } public bool RequiresApproval { get; set; }
    public List<AccountingAllocationPreviewLineResponse> Lines { get; set; } = []; public List<AccountingPostingIssueResponse> Issues { get; set; } = [];
}
public sealed class AccountingAllocationPreviewLineResponse
{
    public int Sequence { get; set; } public Guid DimensionMemberId { get; set; } public string DimensionDisplay { get; set; } = string.Empty;
    public string AllocationKind { get; set; } = string.Empty; public decimal DriverValue { get; set; } public decimal RawAmount { get; set; }
    public decimal RoundedAmount { get; set; } public decimal RoundingResidual { get; set; }
}
public sealed class AccountingAllocationApplicationResponse
{
    public Guid Id { get; set; } public Guid TemplateId { get; set; } public Guid TemplateVersionId { get; set; }
    public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty; public decimal SourceAmount { get; set; } public decimal AllocatedAmount { get; set; }
    public string Currency { get; set; } = string.Empty; public Guid? ApprovalRequestId { get; set; }
    public bool IsIdempotentReplay { get; set; } public DateTime CreatedUtc { get; set; }
    public List<AccountingAllocationPreviewLineResponse> Lines { get; set; } = [];
}
public sealed class AccountingDimensionReportResponse
{
    public Guid DimensionTypeId { get; set; } public Guid DimensionMemberId { get; set; } public string DimensionTypeCode { get; set; } = string.Empty;
    public string DimensionMemberCode { get; set; } = string.Empty; public string DimensionMemberName { get; set; } = string.Empty;
    public decimal TotalDebit { get; set; } public decimal TotalCredit { get; set; } public decimal NetAmount { get; set; }
    public int TotalLineCount { get; set; } public List<AccountingDimensionReportLineResponse> Lines { get; set; } = [];
}
public sealed class AccountingDimensionReportLineResponse
{
    public Guid LedgerEntryId { get; set; } public Guid LedgerEntryLineId { get; set; } public string EntryNumber { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; } public string AccountCode { get; set; } = string.Empty; public string AccountName { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string Currency { get; set; } = string.Empty;
    public string? Description { get; set; } public string DimensionTypeCodeSnapshot { get; set; } = string.Empty;
    public string DimensionMemberCodeSnapshot { get; set; } = string.Empty; public string DimensionMemberNameSnapshot { get; set; } = string.Empty;
    public string HierarchyPathSnapshot { get; set; } = string.Empty;
}

public sealed class SaveAccountingDimensionTypeApiRequest
{
    public Guid? Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string? Description { get; set; } public bool AllowsHierarchy { get; set; } public string Status { get; set; } = "active";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionMemberApiRequest
{
    public Guid? Id { get; set; } public Guid DimensionTypeId { get; set; } public Guid? ParentMemberId { get; set; }
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Status { get; set; } = "active";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionAccountPolicyApiRequest
{
    public Guid? Id { get; set; } public Guid FinanceAccountId { get; set; } public Guid DimensionTypeId { get; set; }
    public string Requirement { get; set; } = "optional"; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionExternalMappingApiRequest
{
    public Guid? Id { get; set; } public string ProviderKey { get; set; } = string.Empty; public string ExternalDimensionType { get; set; } = string.Empty;
    public string ExternalValue { get; set; } = string.Empty; public Guid DimensionTypeId { get; set; } public Guid DimensionMemberId { get; set; }
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionCombinationRuleApiRequest
{
    public Guid? Id { get; set; } public Guid LeftMemberId { get; set; } public Guid RightMemberId { get; set; }
    public bool IsAllowed { get; set; } = true; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; }
    public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingAllocationTemplateApiRequest
{
    public Guid? Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Status { get; set; } = "active";
    public decimal? ApprovalThreshold { get; set; } public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; }
    public int RoundingPrecision { get; set; } = 2; public List<SaveAccountingAllocationTemplateLineApiRequest> Lines { get; set; } = []; public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingAllocationTemplateLineApiRequest
{
    public Guid DimensionMemberId { get; set; } public string AllocationKind { get; set; } = "percentage"; public decimal Value { get; set; } public string? Basis { get; set; }
}
public class PreviewAccountingAllocationApiRequest
{
    public Guid TemplateId { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
}
public sealed class ApplyAccountingAllocationApiRequest : PreviewAccountingAllocationApiRequest
{
    public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; } public List<AccountingAllocationEvidenceApiRequest> Evidence { get; set; } = [];
}
public sealed class AccountingAllocationEvidenceApiRequest
{
    public Guid DocumentId { get; set; } public string ContentHash { get; set; } = string.Empty; public string Title { get; set; } = string.Empty;
}
