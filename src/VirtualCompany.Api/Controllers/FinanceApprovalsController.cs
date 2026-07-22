using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Api.ProblemHandling;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/finance/approvals")]
[Authorize(Policy = CompanyPolicies.FinanceApproval)]
[RequireCompanyContext]
public sealed class FinanceApprovalsController : ControllerBase
{
    private readonly IFinanceApprovalTaskService _approvalTaskService;
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public FinanceApprovalsController(
        IFinanceApprovalTaskService approvalTaskService,
        ICompanyContextAccessor companyContextAccessor)
    {
        _approvalTaskService = approvalTaskService;
        _companyContextAccessor = companyContextAccessor;
    }

    [HttpGet("pending")]
    public async Task<ActionResult<IReadOnlyList<FinancePendingApprovalTaskDto>>> GetPendingAsync(
        CancellationToken cancellationToken)
    {
        if (_companyContextAccessor.CompanyId is not Guid companyId || companyId == Guid.Empty)
        {
            return BadRequest(StableProblemDetails.Create(HttpContext, StatusCodes.Status400BadRequest, ApiProblemCodes.CompanyContextRequired, "Invalid company context", "Company context is required for this endpoint."));
        }

        var result = await _approvalTaskService.GetPendingTasksAsync(new GetPendingFinanceApprovalTasksQuery(companyId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<FinancePendingApprovalTaskDto>> ApproveAsync(
        Guid id,
        [FromBody] FinanceApprovalTaskActionRequest? request,
        CancellationToken cancellationToken) =>
        ActOnTaskAsync(id, ApprovalTaskStatus.Approved, request, cancellationToken);

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<FinancePendingApprovalTaskDto>> RejectAsync(
        Guid id,
        [FromBody] FinanceApprovalTaskActionRequest? request,
        CancellationToken cancellationToken) =>
        ActOnTaskAsync(id, ApprovalTaskStatus.Rejected, request, cancellationToken);

    [HttpPost("{id:guid}/escalate")]
    public Task<ActionResult<FinancePendingApprovalTaskDto>> EscalateAsync(
        Guid id,
        [FromBody] FinanceApprovalTaskActionRequest? request,
        CancellationToken cancellationToken) =>
        ActOnTaskAsync(id, ApprovalTaskStatus.Escalated, request, cancellationToken);

    private async Task<ActionResult<FinancePendingApprovalTaskDto>> ActOnTaskAsync(
        Guid id,
        ApprovalTaskStatus action,
        FinanceApprovalTaskActionRequest? request,
        CancellationToken cancellationToken)
    {
        if (_companyContextAccessor.CompanyId is not Guid companyId || companyId == Guid.Empty)
        {
            return BadRequest(StableProblemDetails.Create(HttpContext, StatusCodes.Status400BadRequest, ApiProblemCodes.CompanyContextRequired, "Invalid company context", "Company context is required for this endpoint."));
        }

        try
        {
            return Ok(await _approvalTaskService.ActOnTaskAsync(
                new ActOnFinanceApprovalTaskCommand(companyId, id, action, request?.Comment),
                cancellationToken));
        }
        catch (FinanceValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest,
                Extensions = { ["code"] = ApiProblemCodes.FinanceApprovalValidation, ["arguments"] = new Dictionary<string, object?>(), ["traceId"] = HttpContext.TraceIdentifier }
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
        catch (InvalidOperationException ex)
        {
            return Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, ApiProblemCodes.FinanceApprovalConflict, "Finance approval task conflict", ex.Message));
        }
    }
}

public sealed record FinanceApprovalTaskActionRequest(string? Comment = null);
