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
public sealed class InternalFinanceFixedAssetsController : InternalFinanceControllerBase
{
    private readonly IFixedAssetService _fixedAssetService;

    public InternalFinanceFixedAssetsController(
        IFixedAssetService fixedAssetService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceFixedAssetsController> logger)
        : base(initializationProblems, logger)
    {
        _fixedAssetService = fixedAssetService;
    }
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/fixed-assets/classes")]
    public Task<ActionResult<IReadOnlyList<FixedAssetClassDto>>> ListFixedAssetClassesAsync(Guid companyId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() => _fixedAssetService.ListClassesAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/fixed-assets/classes/{classId:guid?}")]
    public Task<ActionResult<FixedAssetClassDto>> SaveFixedAssetClassAsync(Guid companyId, Guid? classId,
        [FromBody] SaveFixedAssetClassRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.SaveClassAsync(new(companyId, classId, request.ToInput(), request.ExpectedVersion,
            RequiredFixedAssetActor()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/fixed-assets")]
    public Task<ActionResult<FixedAssetListDto>> ListFixedAssetsAsync(Guid companyId,
        [FromQuery] string? status = null, [FromQuery] Guid? assetClassId = null,
        [FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) => ExecuteReadAsync(() =>
        _fixedAssetService.ListAsync(new(companyId, status, assetClassId, search, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/fixed-assets/{assetId:guid}")]
    public Task<ActionResult<FixedAssetDto>> GetFixedAssetAsync(Guid companyId, Guid assetId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _fixedAssetService.GetAsync(new(companyId, assetId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/fixed-assets")]
    public Task<ActionResult<FixedAssetDto>> RegisterFixedAssetAsync(Guid companyId,
        [FromBody] RegisterFixedAssetRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.RegisterAsync(new(companyId, request.ToInput(), request.IdempotencyKey,
            RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/fixed-assets/{assetId:guid}/capitalize")]
    public Task<ActionResult<FixedAssetDto>> CapitalizeFixedAssetAsync(Guid companyId, Guid assetId,
        [FromBody] FixedAssetLifecycleRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.CapitalizeAsync(request.ToCommand(companyId, assetId, RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/fixed-assets/{assetId:guid}/place-in-service")]
    public Task<ActionResult<FixedAssetDto>> PlaceFixedAssetInServiceAsync(Guid companyId, Guid assetId,
        [FromBody] PlaceFixedAssetInServiceRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.PlaceInServiceAsync(new(companyId, assetId, request.PlacedInServiceDate,
            request.ExpectedVersion, request.IdempotencyKey, RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/fixed-assets/{assetId:guid}/improve")]
    public Task<ActionResult<FixedAssetDto>> ImproveFixedAssetAsync(Guid companyId, Guid assetId,
        [FromBody] FixedAssetLifecycleRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.ImproveAsync(request.ToCommand(companyId, assetId, RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/fixed-assets/{assetId:guid}/impair")]
    public Task<ActionResult<FixedAssetDto>> ImpairFixedAssetAsync(Guid companyId, Guid assetId,
        [FromBody] FixedAssetLifecycleRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.ImpairAsync(request.ToCommand(companyId, assetId, RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/fixed-assets/{assetId:guid}/transfer")]
    public Task<ActionResult<FixedAssetDto>> TransferFixedAssetAsync(Guid companyId, Guid assetId,
        [FromBody] TransferFixedAssetRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.TransferAsync(new(companyId, assetId, request.Custodian, request.Location,
            request.DimensionFacts, request.ExpectedVersion, request.IdempotencyKey, RequiredFixedAssetActor(),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/fixed-assets/{assetId:guid}/dispose")]
    public Task<ActionResult<FixedAssetDto>> DisposeFixedAssetAsync(Guid companyId, Guid assetId,
        [FromBody] DisposeFixedAssetRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.DisposeAsync(new(companyId, assetId, request.DisposalDate, request.FiscalPeriodId,
            request.ProceedsAccountId, request.Proceeds, request.ExpectedVersion, request.SourceVersion,
            request.IdempotencyKey, RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/fixed-assets/{assetId:guid}/events/{eventId:guid}/reverse")]
    public Task<ActionResult<FixedAssetDto>> ReverseFixedAssetEventAsync(Guid companyId, Guid assetId,
        Guid eventId, [FromBody] ReverseFixedAssetEventRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.ReverseEventAsync(new(companyId, assetId, eventId, request.FiscalPeriodId,
            request.PostingDate, request.Reason, request.SourceVersion, request.IdempotencyKey,
            request.ExpectedVersion, RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/fixed-assets/depreciation/preview")]
    public Task<ActionResult<FixedAssetDepreciationPreviewDto>> PreviewFixedAssetDepreciationAsync(Guid companyId,
        [FromQuery] DateOnly periodStart, [FromQuery] DateOnly periodEnd, CancellationToken cancellationToken) =>
        ExecuteReadAsync(() => _fixedAssetService.PreviewDepreciationAsync(new(companyId, periodStart, periodEnd), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/fixed-assets/depreciation/runs")]
    public Task<ActionResult<FixedAssetDepreciationRunDto>> RunFixedAssetDepreciationAsync(Guid companyId,
        [FromBody] RunFixedAssetDepreciationRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _fixedAssetService.RunDepreciationAsync(new(companyId, request.FiscalPeriodId, request.PeriodStart,
            request.PeriodEnd, request.IdempotencyKey, RequiredFixedAssetActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/fixed-assets/reconciliation")]
    public Task<ActionResult<FixedAssetReconciliationDto>> ReconcileFixedAssetsAsync(Guid companyId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() => _fixedAssetService.ReconcileAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/fixed-assets/legacy-conflicts/discover")]
    public Task<ActionResult<int>> DiscoverLegacyFixedAssetConflictsAsync(Guid companyId,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _fixedAssetService.DiscoverLegacyConflictsAsync(companyId, cancellationToken));

    private Guid RequiredFixedAssetActor() => ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.");

}
