using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/approvals")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class ApprovalsController : ControllerBase
{
    private readonly IApprovalRequestService _approvalRequestService;

    public ApprovalsController(IApprovalRequestService approvalRequestService)
    {
        _approvalRequestService = approvalRequestService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApprovalRequestDto>>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        [FromQuery] string? status = null)
    {
        try
        {
            return Ok(await _approvalRequestService.ListAsync(companyId, status, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{approvalId:guid}")]
    public async Task<ActionResult<ApprovalRequestDto>> GetAsync(
        Guid companyId,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _approvalRequestService.GetAsync(companyId, approvalId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApprovalRequestDto>> CreateAsync(
        Guid companyId,
        [FromBody] CreateApprovalRequestCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _approvalRequestService.CreateAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(CreateAsync), new { companyId, approvalId = result.Id }, result);
        }
        catch (ApprovalValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("{approvalId:guid}/decisions")]
    public async Task<ActionResult<ApprovalDecisionResultDto>> DecideAsync(
        Guid companyId,
        Guid approvalId,
        [FromBody] ApprovalDecisionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ApprovalId == Guid.Empty)
        {
            command = command with { ApprovalId = approvalId };
        }

        if (command.ApprovalId != approvalId)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(command.ApprovalId)] = ["Approval id must match the route."]
            })
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            return Ok(await _approvalRequestService.DecideAsync(companyId, command, cancellationToken));
        }
        catch (ApprovalValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (ApprovalDecisionForbiddenException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = "Approval decision conflict", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
        }
    }
}
