using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Context;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/agents/{agentId:guid}/grounded-context")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class GroundedContextController : ControllerBase
{
    private readonly IGroundedPromptContextService _promptContextService;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public GroundedContextController(
        IGroundedPromptContextService promptContextService,
        ICurrentUserAccessor currentUserAccessor)
    {
        _promptContextService = promptContextService;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpPost]
    public async Task<ActionResult<GroundedPromptContextDto>> PrepareAsync(
        Guid companyId,
        Guid agentId,
        [FromBody] PrepareGroundedContextRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Controllers forward scoped identifiers and raw intent only.
            var promptContext = await _promptContextService.PrepareAsync(
                new GroundedPromptContextRequest(
                    companyId,
                    agentId,
                    request.QueryText,
                    _currentUserAccessor.UserId,
                    request.TaskId,
                    request.TaskTitle,
                    request.TaskDescription,
                    request.Limits,
                    request.CorrelationId,
                    request.RetrievalPurpose,
                    request.AsOfUtc),
                cancellationToken);

            return Ok(promptContext);
        }
        catch (GroundedContextRetrievalValidationException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid grounded context request", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public sealed record PrepareGroundedContextRequest(
        string? QueryText,
        Guid? TaskId,
        string? TaskTitle,
        string? TaskDescription,
        RetrievalSourceLimitOptions? Limits,
        string? CorrelationId,
        string? RetrievalPurpose,
        DateTime? AsOfUtc);
}
