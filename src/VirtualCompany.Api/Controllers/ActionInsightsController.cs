using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
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
            return Ok(await _insights.GetActionQueueAsync(companyId, cancellationToken));
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
            return Ok(await _insights.AcknowledgeAsync(companyId, Uri.UnescapeDataString(insightKey), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}