using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}")]
[Authorize(Policy = CompanyPolicies.CompanyOwnerOrAdmin)]
[RequireCompanyContext]
public sealed class CompanyMembershipAdministrationController : ControllerBase
{
    private readonly ICompanyMembershipAdministrationService _membershipAdministrationService;

    public CompanyMembershipAdministrationController(ICompanyMembershipAdministrationService membershipAdministrationService)
    {
        _membershipAdministrationService = membershipAdministrationService;
    }

    [HttpGet("memberships")]
    public async Task<ActionResult<IReadOnlyList<CompanyMemberDirectoryEntryDto>>> GetMembershipsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var memberships = await _membershipAdministrationService.GetMembershipsAsync(companyId, cancellationToken);
        return Ok(memberships);
    }

    [HttpGet("invitations")]
    public async Task<ActionResult<IReadOnlyList<CompanyInvitationDto>>> GetInvitationsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var invitations = await _membershipAdministrationService.GetInvitationsAsync(companyId, cancellationToken);
        return Ok(invitations);
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("invitations")]
    public async Task<ActionResult<CompanyInvitationDeliveryDto>> InviteUserAsync(
        Guid companyId,
        [FromBody] InviteUserToCompanyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var invitation = await _membershipAdministrationService.InviteUserAsync(companyId, request, cancellationToken);
            return Ok(invitation);
        }
        catch (CompanyMembershipAdministrationValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("invitations/{invitationId:guid}/resend")]
    public async Task<ActionResult<CompanyInvitationDeliveryDto>> ReinviteUserAsync(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var invitation = await _membershipAdministrationService.ReinviteUserAsync(companyId, invitationId, cancellationToken);
            return Ok(invitation);
        }
        catch (CompanyMembershipAdministrationValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPost("invitations/{invitationId:guid}/revoke")]
    public async Task<ActionResult<CompanyInvitationDto>> RevokeInvitationAsync(
        Guid companyId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var invitation = await _membershipAdministrationService.RevokeInvitationAsync(companyId, invitationId, cancellationToken);
            return Ok(invitation);
        }
        catch (CompanyMembershipAdministrationValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    [HttpPatch("memberships/{membershipId:guid}/role")]
    public async Task<ActionResult<CompanyMemberDirectoryEntryDto>> ChangeMembershipRoleAsync(
        Guid companyId,
        Guid membershipId,
        [FromBody] ChangeCompanyMembershipRoleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var membership = await _membershipAdministrationService.ChangeMembershipRoleAsync(companyId, membershipId, request, cancellationToken);
            return Ok(membership);
        }
        catch (CompanyMembershipAdministrationValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
