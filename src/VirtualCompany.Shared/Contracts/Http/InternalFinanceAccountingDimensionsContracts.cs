using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;



public sealed class AccountingAllocationEvidenceRequest
{
    public Guid DocumentId { get; set; } public string ContentHash { get; set; } = string.Empty; public string Title { get; set; } = string.Empty;
}



public sealed class ApplyAccountingAllocationRequest : PreviewAccountingAllocationRequest
{
    public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; } public List<AccountingAllocationEvidenceRequest> Evidence { get; set; } = [];
}



public class PreviewAccountingAllocationRequest
{
    public Guid TemplateId { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
}



public sealed class SaveAccountingAllocationTemplateLineRequest
{
    public Guid DimensionMemberId { get; set; } public string AllocationKind { get; set; } = "percentage";
    public decimal Value { get; set; } public string? Basis { get; set; }
}



public sealed class SaveAccountingAllocationTemplateRequest
{
    public Guid? Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "active"; public decimal? ApprovalThreshold { get; set; }
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public int RoundingPrecision { get; set; } = 2;
    public List<SaveAccountingAllocationTemplateLineRequest> Lines { get; set; } = []; public long? ExpectedVersion { get; set; }
}



public sealed class SaveAccountingDimensionExternalMappingRequest
{
    public Guid? Id { get; set; } public string ProviderKey { get; set; } = string.Empty;
    public string ExternalDimensionType { get; set; } = string.Empty; public string ExternalValue { get; set; } = string.Empty;
    public Guid DimensionTypeId { get; set; } public Guid DimensionMemberId { get; set; }
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}



public sealed class SaveAccountingDimensionCombinationRuleRequest
{
    public Guid? Id { get; set; } public Guid LeftMemberId { get; set; } public Guid RightMemberId { get; set; }
    public bool IsAllowed { get; set; } = true; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; }
    public long? ExpectedVersion { get; set; }
}



public sealed class SaveAccountingDimensionAccountPolicyRequest
{
    public Guid? Id { get; set; } public Guid FinanceAccountId { get; set; } public Guid DimensionTypeId { get; set; }
    public string Requirement { get; set; } = "optional"; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; }
    public long? ExpectedVersion { get; set; }
}



public sealed class SaveAccountingDimensionMemberRequest
{
    public Guid? Id { get; set; } public Guid DimensionTypeId { get; set; } public Guid? ParentMemberId { get; set; }
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Status { get; set; } = "active";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}


public sealed class SaveAccountingDimensionTypeRequest
{
    public Guid? Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string? Description { get; set; } public bool AllowsHierarchy { get; set; } public string Status { get; set; } = "active";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
