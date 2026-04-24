using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

    [HttpGet("dashboard")]
    public Task<ActionResult<NormalizedFinanceInsightsDto>> GetDashboardAsync(
        Guid companyId,
        [FromQuery] string? status = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? checkCode = null,
        [FromQuery] DateTime? createdFromUtc = null,
        [FromQuery] DateTime? createdToUtc = null,
        [FromQuery] DateTime? updatedFromUtc = null,
        [FromQuery] DateTime? updatedToUtc = null,
        [FromQuery] string sortBy = FinanceInsightSortFields.UpdatedAt,
        [FromQuery] string sortDirection = FinanceInsightSortDirections.Desc,
        CancellationToken cancellationToken = default) =>
        GetNormalizedAsync(
            new GetNormalizedFinanceInsightsQuery(
                companyId,
                EntityType: null,
                EntityId: null,
                Status: status,
                Severity: severity,
                CheckCode: checkCode,
                CreatedFromUtc: createdFromUtc,
                CreatedToUtc: createdToUtc,
                UpdatedFromUtc: updatedFromUtc,
                UpdatedToUtc: updatedToUtc,
                SortBy: sortBy,
                SortDirection: sortDirection),
            cancellationToken);

    [HttpGet("entities/{entityType}/{entityId}")]
    public Task<ActionResult<NormalizedFinanceInsightsDto>> GetByEntityAsync(
        Guid companyId,
        string entityType,
        string entityId,
        [FromQuery] string? status = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? checkCode = null,
        [FromQuery] DateTime? createdFromUtc = null,
        [FromQuery] DateTime? createdToUtc = null,
        [FromQuery] DateTime? updatedFromUtc = null,
        [FromQuery] DateTime? updatedToUtc = null,
        [FromQuery] string sortBy = FinanceInsightSortFields.UpdatedAt,
        [FromQuery] string sortDirection = FinanceInsightSortDirections.Desc,
        CancellationToken cancellationToken = default) =>
        GetNormalizedAsync(
            new GetNormalizedFinanceInsightsQuery(
                companyId,
                entityType,
                entityId,
                Status: status,
                Severity: severity,
                CheckCode: checkCode,
                CreatedFromUtc: createdFromUtc,
                CreatedToUtc: createdToUtc,
                UpdatedFromUtc: updatedFromUtc,
                UpdatedToUtc: updatedToUtc,
                SortBy: sortBy,
                SortDirection: sortDirection),
            cancellationToken);

    [HttpGet]
    public async Task<ActionResult<FinanceInsightsDto>> GetAsync(
        Guid companyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] int expenseWindowDays = 90,
        [FromQuery] int trendWindowDays = 30,
        [FromQuery] int payableWindowDays = 14,
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] bool includeResolved = true,
        [FromQuery] bool refreshSnapshot = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _financeReadService.GetInsightsAsync(
                new GetFinanceInsightsQuery(
                    companyId,
                    asOfUtc,
                    expenseWindowDays,
                    trendWindowDays,
                    payableWindowDays,
                    entityType,
                    entityId,
                    includeResolved,
                    PreferSnapshot: !refreshSnapshot),
                cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private async Task<ActionResult<NormalizedFinanceInsightsDto>> GetNormalizedAsync(
        GetNormalizedFinanceInsightsQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _financeReadService.GetNormalizedInsightsAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid finance insight query.",
                Detail = exception.Message
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
