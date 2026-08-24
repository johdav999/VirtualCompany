using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/worker-operations")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceWorkerOperationsController(IFinanceWorkerOperationsService operations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceWorkerOperationsReadModel>> GetAsync(Guid companyId,
        [FromQuery] string? status, [FromQuery] string? workerKey, [FromQuery] int skip = 0,
        [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        Ok(await operations.GetAsync(new GetFinanceWorkerOperationsQuery(companyId, status, workerKey, skip, take), cancellationToken));

    [HttpPost("background-executions/{executionId:guid}/retry")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<FinanceWorkerWorkItemDto>> RetryAsync(Guid companyId, Guid executionId,
        [FromBody] FinanceWorkerOperatorActionRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => operations.RetryAsync(new RetryFinanceWorkerExecutionCommand(companyId, executionId,
            request.ExpectedVersion, ResolveActorUserId(), request.Reason, request.CorrelationId), cancellationToken));

    [HttpPost("background-executions/{executionId:guid}/stop")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<FinanceWorkerWorkItemDto>> StopAsync(Guid companyId, Guid executionId,
        [FromBody] FinanceWorkerOperatorActionRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => operations.StopAsync(new StopFinanceWorkerExecutionCommand(companyId, executionId,
            request.ExpectedVersion, ResolveActorUserId(), request.Reason, request.CorrelationId), cancellationToken));

    [HttpPost("background-executions/{executionId:guid}/acknowledge")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<FinanceWorkerWorkItemDto>> AcknowledgeAsync(Guid companyId, Guid executionId,
        [FromBody] FinanceWorkerOperatorActionRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => operations.AcknowledgeAsync(new AcknowledgeFinanceWorkerExecutionCommand(companyId, executionId,
            request.ExpectedVersion, ResolveActorUserId(), request.Reason, request.CorrelationId), cancellationToken));

    private async Task<ActionResult<FinanceWorkerWorkItemDto>> ExecuteAsync(Func<Task<FinanceWorkerWorkItemDto>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (FinanceWorkerOperationException exception)
        {
            var status = exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status404NotFound;
            var problem = new ProblemDetails
            {
                Status = status,
                Title = exception.IsConflict ? "Finance work changed" : "Finance work not found",
                Detail = exception.Message
            };
            problem.Extensions["code"] = exception.ReasonCode;
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return StatusCode(status, problem);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Finance work action",
                Detail = exception.Message,
                Extensions = { ["traceId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    private Guid ResolveActorUserId()
    {
        var value = User.FindFirstValue(CurrentUserClaimTypes.UserId) ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("An authenticated user id is required for Finance worker actions.");
    }
}

public sealed record FinanceWorkerOperatorActionRequest(long ExpectedVersion, string Reason, string? CorrelationId = null);
