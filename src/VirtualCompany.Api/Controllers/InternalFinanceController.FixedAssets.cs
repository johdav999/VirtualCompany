using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
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

public sealed class SaveFixedAssetClassRequest
{
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string BookMethod { get; set; } = "straight_line"; public int UsefulLifeMonths { get; set; } = 60;
    public decimal DefaultResidualPercent { get; set; } public Guid CostAccountId { get; set; }
    public Guid AccumulatedDepreciationAccountId { get; set; } public Guid DepreciationExpenseAccountId { get; set; }
    public Guid AccumulatedImpairmentAccountId { get; set; } public Guid ImpairmentExpenseAccountId { get; set; }
    public Guid DisposalGainAccountId { get; set; } public Guid DisposalLossAccountId { get; set; }
    public string VoucherSeriesCode { get; set; } = "A"; public bool RequiresApproval { get; set; } = true;
    public long ExpectedVersion { get; set; }
    public FixedAssetClassInput ToInput() => new(Code, Name, BookMethod, UsefulLifeMonths,
        DefaultResidualPercent, CostAccountId, AccumulatedDepreciationAccountId, DepreciationExpenseAccountId,
        AccumulatedImpairmentAccountId, ImpairmentExpenseAccountId, DisposalGainAccountId,
        DisposalLossAccountId, VoucherSeriesCode, RequiresApproval);
}
public sealed class RegisterFixedAssetRequest
{
    public Guid AssetClassId { get; set; } public string AssetNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; public string Currency { get; set; } = "SEK";
    public decimal AcquisitionCost { get; set; } public decimal? ResidualValue { get; set; }
    public int? UsefulLifeMonths { get; set; } public DateOnly AcquisitionDate { get; set; }
    public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public Guid? SourceDocumentId { get; set; }
    public Guid? LegacyFinanceAssetId { get; set; } public string? Custodian { get; set; } public string? Location { get; set; }
    public Dictionary<string, string> DimensionFacts { get; set; } = []; public string IdempotencyKey { get; set; } = string.Empty;
    public List<FixedAssetComponentRequest> Components { get; set; } = [];
    public RegisterFixedAssetInput ToInput() => new(AssetClassId, AssetNumber, Name, Currency,
        AcquisitionCost, ResidualValue, UsefulLifeMonths, AcquisitionDate, SourceType, SourceId,
        SourceVersion, SourceDocumentId, LegacyFinanceAssetId, Custodian, Location, DimensionFacts,
        Components.Select(x => x.ToInput()).ToArray());
}
public sealed class FixedAssetComponentRequest
{
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public decimal Cost { get; set; } public decimal ResidualValue { get; set; }
    public int UsefulLifeMonths { get; set; } public DateOnly PlacedInServiceDate { get; set; }
    public FixedAssetComponentInput ToInput() => new(Code, Name, Cost, ResidualValue,
        UsefulLifeMonths, PlacedInServiceDate);
}
public sealed class FixedAssetLifecycleRequest
{
    public DateOnly EffectiveDate { get; set; } public Guid FiscalPeriodId { get; set; }
    public Guid OffsetAccountId { get; set; } public decimal Amount { get; set; } public long ExpectedVersion { get; set; }
    public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty;
    public FixedAssetLifecycleCommand ToCommand(Guid companyId, Guid assetId, Guid actor, string? correlation) =>
        new(companyId, assetId, EffectiveDate, FiscalPeriodId, OffsetAccountId, Amount, ExpectedVersion,
            SourceVersion, IdempotencyKey, actor, correlation);
}
public sealed class PlaceFixedAssetInServiceRequest { public DateOnly PlacedInServiceDate { get; set; } public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class TransferFixedAssetRequest { public string? Custodian { get; set; } public string? Location { get; set; } public Dictionary<string, string> DimensionFacts { get; set; } = []; public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class DisposeFixedAssetRequest { public DateOnly DisposalDate { get; set; } public Guid FiscalPeriodId { get; set; } public Guid ProceedsAccountId { get; set; } public decimal Proceeds { get; set; } public long ExpectedVersion { get; set; } public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class ReverseFixedAssetEventRequest { public Guid FiscalPeriodId { get; set; } public DateOnly PostingDate { get; set; } public string Reason { get; set; } = string.Empty; public string SourceVersion { get; set; } = string.Empty; public string IdempotencyKey { get; set; } = string.Empty; public long ExpectedVersion { get; set; } }
public sealed class RunFixedAssetDepreciationRequest { public Guid FiscalPeriodId { get; set; } public DateOnly PeriodStart { get; set; } public DateOnly PeriodEnd { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
