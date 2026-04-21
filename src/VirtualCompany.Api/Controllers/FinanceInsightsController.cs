using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/insights")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceInsightsController : ControllerBase
{
    private readonly IFinanceReadService _financeReadService;

    public FinanceInsightsController(IFinanceReadService financeReadService)
    {
        _financeReadService = financeReadService;
    }

    [HttpGet]
    public async Task<ActionResult<FinanceInsightsDto>> GetAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] int expenseWindowDays = 90,
        [FromQuery] int trendWindowDays = 30,
        [FromQuery] int payableWindowDays = 14,
        [FromQuery] bool refreshSnapshot = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _financeReadService.GetInsightsAsync(
                new GetFinanceInsightsQuery(companyId, asOfUtc, expenseWindowDays, trendWindowDays, payableWindowDays, PreferSnapshot: !refreshSnapshot),
                cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
