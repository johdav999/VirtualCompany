using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/auth/preferences")]
[Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
public sealed class UserPreferencesController : ControllerBase
{
    private readonly IUserPreferenceService _preferences;

    public UserPreferencesController(IUserPreferenceService preferences)
    {
        _preferences = preferences;
    }

    [HttpGet]
    public async Task<ActionResult<UserPreferenceDto>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _preferences.GetCurrentAsync(cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpPut]
    public async Task<ActionResult<UserPreferenceDto>> UpdateAsync(
        [FromBody] UpdateUserPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _preferences.UpdateCurrentAsync(command, cancellationToken));
        }
        catch (UserPreferenceValidationException ex)
        {
            var problem = new ValidationProblemDetails(
                ex.Errors.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The language preference is invalid."
            };
            problem.Extensions["code"] = ex.Code;
            return BadRequest(problem);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }
}
