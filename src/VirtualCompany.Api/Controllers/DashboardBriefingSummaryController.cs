using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class DashboardBriefingSummaryController : ControllerBase
{
    private readonly IDashboardBriefingSummaryService _summaryService;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public DashboardBriefingSummaryController(
        IDashboardBriefingSummaryService summaryService,
        ICompanyContextAccessor companyContextAccessor,
        ICurrentUserAccessor currentUserAccessor)
    {
        _summaryService = summaryService;
        _companyContextAccessor = companyContextAccessor;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet("briefing-summary")]
    public async Task<ActionResult<DashboardBriefingSummaryDto>> GetAsync(
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken)
    {
        var resolvedCompanyId = companyId != Guid.Empty ? companyId : _companyContextAccessor.CompanyId;
        if (resolvedCompanyId is not Guid effectiveCompanyId)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["companyId"] = ["companyId is required."]
            })
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (_currentUserAccessor.UserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        return Ok(await _summaryService.GenerateAsync(
            new GenerateDashboardBriefingSummaryQuery(effectiveCompanyId, currentUserId),
            cancellationToken));
    }
}
