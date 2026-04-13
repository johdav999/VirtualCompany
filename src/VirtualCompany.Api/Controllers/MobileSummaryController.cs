using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Mobile;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared.Mobile;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/mobile/summary")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class MobileSummaryController : ControllerBase
{
    private readonly IMobileSummaryService _mobileSummaryService;

    public MobileSummaryController(IMobileSummaryService mobileSummaryService)
    {
        _mobileSummaryService = mobileSummaryService;
    }

    [HttpGet]
    public async Task<ActionResult<MobileHomeSummaryResponse>> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mobileSummaryService.GetHomeSummaryAsync(new GetMobileHomeSummaryQuery(companyId), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
