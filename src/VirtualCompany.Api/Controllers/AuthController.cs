using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ICurrentUserCompanyService _companyService;
    private readonly ICompanySelectionService _companySelectionService;

    public AuthController(
        IAuthorizationService authorizationService,
        ICurrentUserCompanyService companyService,
        ICompanySelectionService companySelectionService)
    {
        _authorizationService = authorizationService;
        _companyService = companyService;
        _companySelectionService = companySelectionService;
    }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserContextDto>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var currentUserContext = await _companyService.GetCurrentUserContextAsync(cancellationToken);
        if (currentUserContext is null)
        {
            return Unauthorized();
        }

        return Ok(currentUserContext);
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
        var authorizationResult = await _authorizationService.AuthorizeAsync(
            User,
            request.CompanyId,
            CompanyPolicies.CompanyMember);

        if (!authorizationResult.Succeeded)
        {
            return Forbid();
        }

        var activeCompany = await _companySelectionService.SelectAsync(request.CompanyId, cancellationToken);
        if (activeCompany is null)
        {
            return Forbid();
        }

        return Ok(new CompanySelectionDto(
            request.CompanyId,
            CompanyContextResolutionMiddleware.CompanyHeaderName,
            request.CompanyId.ToString(),
            activeCompany));
    }

    public sealed record SelectCompanyRequest(Guid CompanyId);
}
