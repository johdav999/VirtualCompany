using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController, Authorize(Policy = CompanyPolicies.FinanceView), RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/operational-report")]
public sealed class FinanceOperationalReportsController(IFinanceOperationalReportService reports) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceOperationalReportDto>> Get(Guid companyId, [FromQuery] DateOnly? asOfDate,
        [FromQuery] int horizonDays = 14, [FromQuery] string? currency = null, [FromQuery] string? bucket = null, [FromQuery] string? source = null, CancellationToken token = default)
    {
        try { return Ok(await reports.GetAsync(new(companyId, asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow), horizonDays, currency, bucket, source), token)); }
        catch (ArgumentException ex) { return Problem(title: "Report filters need review", detail: ex.Message, statusCode: 400); }
    }
}
