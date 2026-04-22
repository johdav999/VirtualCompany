using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance-summary")]
[Route("api/companies/{companyId:guid}/finance/dashboard/summary")]
[Route("api/companies/{companyId:guid}/finance/agent-context/summary")]
[Route("internal/companies/{companyId:guid}/finance/debug/summary")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceSummaryController : ControllerBase
{
    private readonly IFinanceSummaryQueryService _financeSummaryQueryService;

    public FinanceSummaryController(IFinanceSummaryQueryService financeSummaryQueryService)
    {
        _financeSummaryQueryService = financeSummaryQueryService;
    }

    [HttpGet]
    public async Task<ActionResult<FinanceSummaryDto>> GetAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] bool includeConsistencyCheck = false,
        [FromQuery] int recentAssetPurchaseLimit = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _financeSummaryQueryService.GetAsync(
                new GetFinanceSummaryQuery(companyId, asOfUtc, recentAssetPurchaseLimit, includeConsistencyCheck),
                cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}