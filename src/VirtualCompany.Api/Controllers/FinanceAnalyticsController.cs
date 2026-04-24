using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/analytics")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAnalyticsController : ControllerBase
{
    private readonly IFinanceReadService _financeReadService;

    public FinanceAnalyticsController(IFinanceReadService financeReadService)
    {
        _financeReadService = financeReadService;
    }

    [HttpGet]
    public async Task<ActionResult<FinanceAnalyticsDto>> GetAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] int expenseWindowDays = 90,
        [FromQuery] int trendWindowDays = 30,
        [FromQuery] int payableWindowDays = 14,
        [FromQuery] int recentAssetPurchaseLimit = 5,
        [FromQuery] bool includeConsistencyCheck = true,
        [FromQuery] bool refreshInsightsSnapshot = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _financeReadService.GetAnalyticsAsync(
                new GetFinanceAnalyticsQuery(
                    companyId,
                    asOfUtc,
                    expenseWindowDays,
                    trendWindowDays,
                    payableWindowDays,
                    recentAssetPurchaseLimit,
                    includeConsistencyCheck,
                    refreshInsightsSnapshot),
                cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
