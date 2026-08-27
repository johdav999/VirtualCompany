using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/vat/filing-periods")]
    public Task<ActionResult<VatFilingPeriodDto>> CreateVatFilingPeriodAsync(Guid companyId,
        [FromBody] CreateVatFilingPeriodRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(() => _vatReturnService.CreateFilingPeriodAsync(new CreateVatFilingPeriodCommand(
            companyId, request.PeriodCode, request.StartDate, request.EndDate, request.Currency,
            request.FiscalPeriodId, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required.")), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/vat/filing-periods")]
    public Task<ActionResult<IReadOnlyList<VatFilingPeriodDto>>> ListVatFilingPeriodsAsync(Guid companyId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _vatReturnService.ListFilingPeriodsAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/vat/returns/calculate")]
    public Task<ActionResult<VatReturnDto>> CalculateVatReturnAsync(Guid companyId,
        [FromBody] CalculateVatReturnRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(() => _vatReturnService.CalculateAsync(new CalculateVatReturnCommand(
            companyId, request.FilingPeriodId, request.VatReturnId, request.IdempotencyKey,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.")),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/vat/returns")]
    public Task<ActionResult<IReadOnlyList<VatReturnDto>>> ListVatReturnsAsync(Guid companyId,
        [FromQuery] Guid? filingPeriodId, CancellationToken cancellationToken) =>
        ExecuteReadAsync(() => _vatReturnService.ListAsync(new ListVatReturnsQuery(companyId, filingPeriodId),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/vat/returns/{vatReturnId:guid}")]
    public Task<ActionResult<VatReturnDto>> GetVatReturnAsync(Guid companyId, Guid vatReturnId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _vatReturnService.GetAsync(new GetVatReturnQuery(companyId, vatReturnId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/vat/returns/{vatReturnId:guid}/approval")]
    public Task<ActionResult<VatReturnDto>> RequestVatReturnApprovalAsync(Guid companyId, Guid vatReturnId,
        [FromBody] VatReturnEvidenceRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(() => _vatReturnService.RequestApprovalAsync(new RequestVatReturnApprovalCommand(
            companyId, vatReturnId, request.ExpectedInputHash,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.")),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/vat/returns/{vatReturnId:guid}/finalize")]
    public Task<ActionResult<VatReturnDto>> FinalizeVatReturnAsync(Guid companyId, Guid vatReturnId,
        [FromBody] VatReturnEvidenceRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(() => _vatReturnService.FinalizeAsync(new FinalizeVatReturnCommand(
            companyId, vatReturnId, request.ExpectedInputHash,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.")),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/vat/returns/{vatReturnId:guid}/corrections")]
    public Task<ActionResult<VatReturnDto>> CreateVatReturnCorrectionAsync(Guid companyId, Guid vatReturnId,
        [FromBody] CreateVatReturnCorrectionRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(() => _vatReturnService.CreateCorrectionAsync(new CreateVatReturnCorrectionCommand(
            companyId, vatReturnId, request.Reason, request.EvidenceReference, request.IdempotencyKey,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.")),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/vat/returns/{vatReturnId:guid}/package")]
    public async Task<IActionResult> DownloadVatReturnPackageAsync(Guid companyId, Guid vatReturnId,
        CancellationToken cancellationToken)
    {
        try
        {
            var package = await _vatReturnService.DownloadPackageAsync(
                new GetVatReturnPackageQuery(companyId, vatReturnId), cancellationToken);
            Response.Headers.ETag = $"\"{package.Checksum}\"";
            return File(package.Content, package.MediaType, package.FileName);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (VatReturnOperationException exception) { return Conflict(CreateVatReturnProblemDetails(exception)); }
    }
}

public sealed class CreateVatFilingPeriodRequest
{
    public string PeriodCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Currency { get; set; } = "SEK";
    public Guid? FiscalPeriodId { get; set; }
}

public sealed class CalculateVatReturnRequest
{
    public Guid FilingPeriodId { get; set; }
    public Guid? VatReturnId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class VatReturnEvidenceRequest
{
    public string ExpectedInputHash { get; set; } = string.Empty;
}

public sealed class CreateVatReturnCorrectionRequest
{
    public string Reason { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}
