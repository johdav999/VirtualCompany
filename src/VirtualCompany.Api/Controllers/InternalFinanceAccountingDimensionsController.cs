using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceAccountingDimensionsController : InternalFinanceControllerBase
{
    private readonly IAccountingDimensionService _accountingDimensionService;

    public InternalFinanceAccountingDimensionsController(
        IAccountingDimensionService accountingDimensionService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingDimensionsController> logger)
        : base(initializationProblems, logger)
    {
        _accountingDimensionService = accountingDimensionService;
    }
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
