using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/accounting/report-suite")]
public sealed class FinancialReportSuiteController : ControllerBase
{
    private readonly IFinancialReportSuiteService _reports;
    private readonly ICurrentUserAccessor _currentUser;

    public FinancialReportSuiteController(IFinancialReportSuiteService reports, ICurrentUserAccessor currentUser)
    {
        _reports = reports;
        _currentUser = currentUser;
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("{reportKind}")]
    public async Task<ActionResult<CompleteFinancialReportDto>> GetAsync(Guid companyId, string reportKind,
        [FromQuery] Guid fiscalPeriodId, [FromQuery] string cashFlowMethod = CashFlowMethods.Indirect,
        [FromQuery] Guid? comparisonFiscalPeriodId = null, [FromQuery] int rollingPeriodCount = 12,
        [FromQuery] DateOnly? asOfDate = null, [FromQuery] Guid? dimensionTypeId = null,
        [FromQuery] Guid? dimensionMemberId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200, CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _reports.GetAsync(new(companyId, fiscalPeriodId, Normalize(reportKind), cashFlowMethod,
                comparisonFiscalPeriodId, rollingPeriodCount, asOfDate, dimensionTypeId, dimensionMemberId,
                page, pageSize), cancellationToken));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (FinancialReportException ex) { return Problem(ex, ex.IsConflict ? 409 : 400); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("snapshots")]
    public async Task<ActionResult<FinancialReportSnapshotDto>> CaptureAsync(Guid companyId,
        [FromBody] CaptureFinancialReportSnapshotRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var actor = _currentUser.UserId ?? throw new UnauthorizedAccessException();
            var result = await _reports.CaptureSnapshotAsync(new(companyId, request.FiscalPeriodId,
                Normalize(request.ReportKind), request.CashFlowMethod, actor, request.IdempotencyKey,
                request.ComparisonFiscalPeriodId, request.RollingPeriodCount, request.AsOfDate,
                request.DimensionTypeId, request.DimensionMemberId), cancellationToken);
            return result.IsIdempotentReplay ? Ok(result) : CreatedAtAction(nameof(GetSnapshotAsync),
                new { companyId, snapshotId = result.Id }, result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (FinancialReportException ex) { return Problem(ex, ex.IsConflict ? 409 : 400); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("snapshots/{snapshotId:guid}")]
    public async Task<ActionResult<FinancialReportSnapshotDto>> GetSnapshotAsync(Guid companyId, Guid snapshotId,
        CancellationToken cancellationToken)
    {
        try { return Ok(await _reports.GetSnapshotAsync(companyId, snapshotId, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("snapshots/{snapshotId:guid}/export")]
    public async Task<IActionResult> ExportSnapshotAsync(Guid companyId, Guid snapshotId,
        [FromQuery] string format = "csv", CancellationToken cancellationToken = default)
    {
        try
        {
            var export = await _reports.ExportSnapshotAsync(companyId, snapshotId, format, cancellationToken);
            Response.Headers["X-Content-Checksum"] = export.Checksum;
            if (export.ReportDefinitionVersionId.HasValue)
                Response.Headers["X-Report-Definition-Version"] = export.ReportDefinitionVersionId.Value.ToString("D");
            if (!string.IsNullOrWhiteSpace(export.ReportDefinitionHash))
                Response.Headers["X-Report-Definition-Hash"] = export.ReportDefinitionHash;
            return File(export.Content, export.ContentType, export.FileName);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (FinancialReportException ex) { return Problem(ex, 400); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("{reportKind}/lines/{lineKey}/drilldown")]
    public async Task<ActionResult<FinancialReportDrilldownDto>> DrilldownAsync(Guid companyId, string reportKind,
        string lineKey, [FromQuery] Guid fiscalPeriodId, [FromQuery] Guid? snapshotId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _reports.GetDrilldownAsync(new(companyId, fiscalPeriodId, Normalize(reportKind),
                lineKey, snapshotId, page, pageSize), cancellationToken));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (FinancialReportException ex) { return Problem(ex, ex.IsConflict ? 409 : 400); }
    }

    private ObjectResult Problem(FinancialReportException exception, int status) =>
        new(StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == 409 ? "Financial report conflict" : "Financial report request rejected", exception.Message))
        { StatusCode = status };
    private static string Normalize(string value) => value.Trim().Replace('-', '_').ToLowerInvariant();
}

public sealed class CaptureFinancialReportSnapshotRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string ReportKind { get; set; } = string.Empty;
    public string CashFlowMethod { get; set; } = CashFlowMethods.Indirect;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ComparisonFiscalPeriodId { get; set; }
    public int RollingPeriodCount { get; set; } = 12;
    public DateOnly? AsOfDate { get; set; }
    public Guid? DimensionTypeId { get; set; }
    public Guid? DimensionMemberId { get; set; }
}
