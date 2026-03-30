using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Companies;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/invitations")]
[Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
public sealed class InvitationsController : ControllerBase
{
    private readonly ICompanyMembershipAdministrationService _membershipAdministrationService;

    public InvitationsController(ICompanyMembershipAdministrationService membershipAdministrationService)
    {
        _membershipAdministrationService = membershipAdministrationService;
    }

    [HttpPost("accept")]
    public async Task<ActionResult<AcceptCompanyInvitationResultDto>> AcceptInvitationAsync(
        [FromBody] AcceptCompanyInvitationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _membershipAdministrationService.AcceptInvitationAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (CompanyMembershipAdministrationValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }
}
