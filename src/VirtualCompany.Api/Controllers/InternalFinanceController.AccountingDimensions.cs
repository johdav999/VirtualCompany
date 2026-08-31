using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/dimensions/workspace")]
    public async Task<ActionResult<AccountingDimensionWorkspaceDto>> GetAccountingDimensionWorkspaceAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingDimensionService.GetWorkspaceAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/dimensions/types")]
    public async Task<ActionResult<AccountingDimensionTypeDto>> SaveAccountingDimensionTypeAsync(
        Guid companyId, [FromBody] SaveAccountingDimensionTypeRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingDimensionService.SaveTypeAsync(new(companyId, request.Id,
            request.Code, request.Name, request.Description, request.AllowsHierarchy, request.Status,
            request.EffectiveFrom, request.EffectiveTo, request.ExpectedVersion, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/dimensions/members")]
    public async Task<ActionResult<AccountingDimensionMemberDto>> SaveAccountingDimensionMemberAsync(
        Guid companyId, [FromBody] SaveAccountingDimensionMemberRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingDimensionService.SaveMemberAsync(new(companyId, request.DimensionTypeId,
            request.Id, request.ParentMemberId, request.Code, request.Name, request.Status, request.EffectiveFrom,
            request.EffectiveTo, request.ExpectedVersion, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/dimensions/account-policies")]
    public async Task<ActionResult<AccountingDimensionAccountPolicyDto>> SaveAccountingDimensionAccountPolicyAsync(
        Guid companyId, [FromBody] SaveAccountingDimensionAccountPolicyRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingDimensionService.SaveAccountPolicyAsync(new(companyId, request.Id,
            request.FinanceAccountId, request.DimensionTypeId, request.Requirement, request.EffectiveFrom,
            request.EffectiveTo, request.ExpectedVersion, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/dimensions/combination-rules")]
    public async Task<ActionResult<AccountingDimensionCombinationRuleDto>> SaveAccountingDimensionCombinationRuleAsync(
        Guid companyId, [FromBody] SaveAccountingDimensionCombinationRuleRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingDimensionService.SaveCombinationRuleAsync(new(companyId, request.Id,
            request.LeftMemberId, request.RightMemberId, request.IsAllowed, request.EffectiveFrom, request.EffectiveTo,
            request.ExpectedVersion, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/dimensions/external-mappings")]
    public async Task<ActionResult<AccountingDimensionExternalMappingDto>> SaveAccountingDimensionExternalMappingAsync(
        Guid companyId, [FromBody] SaveAccountingDimensionExternalMappingRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingDimensionService.SaveExternalMappingAsync(new(companyId, request.Id,
            request.ProviderKey, request.ExternalDimensionType, request.ExternalValue, request.DimensionTypeId,
            request.DimensionMemberId, request.EffectiveFrom, request.EffectiveTo, request.ExpectedVersion,
            RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/dimensions/allocation-templates")]
    public async Task<ActionResult<AccountingAllocationTemplateDto>> SaveAccountingAllocationTemplateAsync(
        Guid companyId, [FromBody] SaveAccountingAllocationTemplateRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingDimensionService.SaveAllocationTemplateVersionAsync(new(companyId,
            request.Id, request.Code, request.Name, request.Status, request.ApprovalThreshold, request.EffectiveFrom,
            request.EffectiveTo, request.RoundingPrecision, request.Lines.Select(x =>
                new AccountingAllocationTemplateLineInput(x.DimensionMemberId, x.AllocationKind, x.Value, x.Basis)).ToArray(),
            request.ExpectedVersion, RequiredActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/dimensions/allocations/preview")]
    public async Task<ActionResult<AccountingAllocationPreviewDto>> PreviewAccountingDimensionAllocationAsync(
        Guid companyId, [FromBody] PreviewAccountingAllocationRequest request, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingDimensionService.PreviewAllocationAsync(new(companyId,
            request.TemplateId, request.Amount, request.Currency, request.EffectiveDate), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/dimensions/allocations")]
    public async Task<ActionResult<AccountingAllocationApplicationDto>> ApplyAccountingDimensionAllocationAsync(
        Guid companyId, [FromBody] ApplyAccountingAllocationRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingDimensionService.ApplyAllocationAsync(new(companyId, request.TemplateId,
            request.Amount, request.Currency, request.EffectiveDate, request.SourceType, request.SourceId,
            request.SourceVersion, request.IdempotencyKey, RequiredActor(), request.ApprovalRequestId,
            request.Evidence.Select(x => new ProposedAccountingEvidence(x.DocumentId, x.ContentHash, x.Title)).ToArray(),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/dimensions/members/{dimensionMemberId:guid}/report")]
    public async Task<ActionResult<AccountingDimensionReportDto>> GetAccountingDimensionReportAsync(Guid companyId,
        Guid dimensionMemberId, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int skip = 0, [FromQuery] int take = 250, CancellationToken cancellationToken = default) =>
        await ExecuteReadAsync(() => _accountingDimensionService.GetReportAsync(new(companyId, dimensionMemberId,
            from, to, skip, take), cancellationToken));

}

public sealed class SaveAccountingDimensionTypeRequest
{
    public Guid? Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string? Description { get; set; } public bool AllowsHierarchy { get; set; } public string Status { get; set; } = "active";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionMemberRequest
{
    public Guid? Id { get; set; } public Guid DimensionTypeId { get; set; } public Guid? ParentMemberId { get; set; }
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Status { get; set; } = "active";
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionAccountPolicyRequest
{
    public Guid? Id { get; set; } public Guid FinanceAccountId { get; set; } public Guid DimensionTypeId { get; set; }
    public string Requirement { get; set; } = "optional"; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; }
    public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionCombinationRuleRequest
{
    public Guid? Id { get; set; } public Guid LeftMemberId { get; set; } public Guid RightMemberId { get; set; }
    public bool IsAllowed { get; set; } = true; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; }
    public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingDimensionExternalMappingRequest
{
    public Guid? Id { get; set; } public string ProviderKey { get; set; } = string.Empty;
    public string ExternalDimensionType { get; set; } = string.Empty; public string ExternalValue { get; set; } = string.Empty;
    public Guid DimensionTypeId { get; set; } public Guid DimensionMemberId { get; set; }
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingAllocationTemplateRequest
{
    public Guid? Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "active"; public decimal? ApprovalThreshold { get; set; }
    public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public int RoundingPrecision { get; set; } = 2;
    public List<SaveAccountingAllocationTemplateLineRequest> Lines { get; set; } = []; public long? ExpectedVersion { get; set; }
}
public sealed class SaveAccountingAllocationTemplateLineRequest
{
    public Guid DimensionMemberId { get; set; } public string AllocationKind { get; set; } = "percentage";
    public decimal Value { get; set; } public string? Basis { get; set; }
}
public class PreviewAccountingAllocationRequest
{
    public Guid TemplateId { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
}
public sealed class ApplyAccountingAllocationRequest : PreviewAccountingAllocationRequest
{
    public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; } public List<AccountingAllocationEvidenceRequest> Evidence { get; set; } = [];
}
public sealed class AccountingAllocationEvidenceRequest
{
    public Guid DocumentId { get; set; } public string ContentHash { get; set; } = string.Empty; public string Title { get; set; } = string.Empty;
}
