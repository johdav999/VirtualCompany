using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Common;
using VirtualCompany.Application.Focus;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class DashboardFocusController : ControllerBase
{
    private readonly IFocusEngine _focusEngine;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public DashboardFocusController(
        IFocusEngine focusEngine,
        ICompanyContextAccessor companyContextAccessor,
        ICurrentUserAccessor currentUserAccessor)
    {
        _focusEngine = focusEngine;
        _companyContextAccessor = companyContextAccessor;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpGet("focus")]
    public async Task<ActionResult<IReadOnlyList<FocusItemDto>>> GetAsync(
        [FromQuery] Guid companyId,
        [FromQuery] Guid? userId,
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

        if (userId.HasValue && currentUserId != userId.Value)
        {
            return Forbid();
        }

        var items = await _focusEngine.GetFocusAsync(new GetDashboardFocusQuery(effectiveCompanyId, currentUserId), cancellationToken);
        return Ok(items.Select(DisplayTextMapper.MapFocusItem).ToList());
    }
}
