using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

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
            return BadRequest(new ProblemDetails { Title = "Invalid company context", Detail = "Company context is required for this endpoint.", Status = StatusCodes.Status400BadRequest });
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
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid company context",
                Detail = "Company context is required for this endpoint.",
                Status = StatusCodes.Status400BadRequest
            });
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Finance approval task conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }
}

public sealed record FinanceApprovalTaskActionRequest(string? Comment = null);
