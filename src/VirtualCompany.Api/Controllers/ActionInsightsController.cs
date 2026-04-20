using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Common;
using VirtualCompany.Application.Insights;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/action-insights")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class ActionInsightsController : ControllerBase
{
    private readonly IActionInsightService _insights;

    private const int DefaultTopActionCount = 5;
    private const int DefaultPageSize = 25;
    public ActionInsightsController(IActionInsightService insights)
    {
        _insights = insights;
    }

    [HttpGet("queue")]
    public async Task<ActionResult<IReadOnlyList<ActionQueueItemDto>>> GetQueueAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await _insights.GetActionQueueAsync(companyId, cancellationToken);
            return Ok(items.Select(DisplayTextMapper.MapActionItem).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("top")]
    public async Task<ActionResult<IReadOnlyList<ActionQueueItemDto>>> GetTopActionsAsync(
        Guid companyId,
        [FromQuery] int count = DefaultTopActionCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await _insights.GetActionQueueAsync(companyId, cancellationToken);
            var distinctItems = DisplayTextMapper
                .DistinctActionItemsForDisplay(items)
                .Take(Math.Max(1, count))
                .ToList();
            return Ok(distinctItems);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet]
    public async Task<ActionResult<ActionQueuePageDto>> GetAllActionsAsync(
        Guid companyId,
        [FromQuery(Name = "page")] int? page = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedPageNumber = page ?? pageNumber;

            var pageResult = await _insights.GetAllActionsAsync(companyId, resolvedPageNumber, pageSize, cancellationToken);
            return Ok(DisplayTextMapper.MapActionPage(pageResult));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("{insightKey}/acknowledgment")]
    public async Task<ActionResult<ActionQueueItemDto?>> AcknowledgeAsync(
        Guid companyId,
        string insightKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insightKey))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(insightKey)] = ["Insight key is required."]
            }));
        }

        try
        {
            var item = await _insights.AcknowledgeAsync(companyId, Uri.UnescapeDataString(insightKey), cancellationToken);
            return Ok(item is null ? null : DisplayTextMapper.MapActionItem(item));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
