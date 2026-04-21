using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/approvals")]
[Authorize(Policy = CompanyPolicies.FinanceEdit)]
[RequireCompanyContext]
public sealed class InternalFinanceApprovalsController : ControllerBase
{
    private readonly IFinanceApprovalTaskService _approvalTaskService;

    public InternalFinanceApprovalsController(IFinanceApprovalTaskService approvalTaskService)
    {
        _approvalTaskService = approvalTaskService;
    }

    [HttpPost("backfill")]
    public async Task<ActionResult<FinanceApprovalTaskBackfillResultDto>> BackfillAsync(
        Guid companyId,
        [FromBody] RunFinanceApprovalTaskBackfillRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is not null && request.BatchSize <= 0)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(RunFinanceApprovalTaskBackfillRequest.BatchSize)] = ["Batch size must be greater than zero."]
            })
            { Title = "Finance validation failed", Status = StatusCodes.Status400BadRequest, Instance = HttpContext.Request.Path });
        }

        return Ok(await _approvalTaskService.BackfillApprovalTasksAsync(new BackfillFinanceApprovalTasksCommand(companyId, request?.BatchSize ?? 250, request?.CorrelationId, request?.IncludePayments ?? true), cancellationToken));
    }
}

public sealed record RunFinanceApprovalTaskBackfillRequest(int BatchSize = 250, string? CorrelationId = null, bool IncludePayments = true);
