using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    private readonly ICurrentUserCompanyService _companyService;

    public AuthController(ICurrentUserCompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var currentUser = await _companyService.GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        return Ok(currentUser);
    }

    [HttpGet("memberships")]
    public async Task<ActionResult<IReadOnlyList<CompanyMembershipDto>>> GetMembershipsAsync(CancellationToken cancellationToken)
    {
        var memberships = await _companyService.GetMembershipsAsync(cancellationToken);
        return Ok(memberships);
    }

    [HttpPost("select-company")]
    public async Task<ActionResult<CompanySelectionDto>> SelectCompanyAsync(
        [FromBody] SelectCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var canAccessCompany = await _companyService.CanAccessCompanyAsync(request.CompanyId, cancellationToken);
        if (!canAccessCompany)
        {
            return Forbid();
        }

        return Ok(new CompanySelectionDto(
            request.CompanyId,
            CompanyContextResolutionMiddleware.CompanyHeaderName));
    }

    public sealed record SelectCompanyRequest(Guid CompanyId);
}