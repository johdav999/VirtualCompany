using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Support;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController, Route("api/support/reports")]
[Authorize(Policy = CompanyPolicies.CompanyMember), RequireCompanyContext]
[TypeFilter(typeof(SupportResponsibilityFilter))]
public sealed class SupportOperationalReportsController(ISupportOperationalReportService reports, ICompanyContextAccessor context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SupportOperationalReport>> GetAsync([FromQuery] SupportOperationalReportQuery query,
        [FromQuery] bool assignedToMe, CancellationToken cancellationToken)
    {
        try { return Ok(await reports.GetAsync(context.CompanyId!.Value,
            assignedToMe ? query with { AssignedUserId = context.UserId } : query, cancellationToken)); }
        catch (SupportValidationException ex) { return BadRequest(new { message = ex.Message, errors = ex.Errors }); }
    }
}
