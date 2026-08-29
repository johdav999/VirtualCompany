using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance/payment-executions")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalPaymentExecutionsController(
    IPaymentBatchExecutionService service,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet("{executionId:guid}")]
    public async Task<ActionResult<PaymentBatchExecutionDto>> GetAsync(Guid companyId, Guid executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetAsync(new(companyId, executionId), cancellationToken);
            return result is null ? NotFound(Problem("Payment execution was not found.", 404)) : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("batch/{batchId:guid}")]
    public async Task<ActionResult<PaymentBatchExecutionDto>> GetForBatchAsync(Guid companyId, Guid batchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetForBatchAsync(new(companyId, batchId), cancellationToken);
            return result is null ? NotFound(Problem("No execution exists for this payment batch.", 404)) : Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("batch/{batchId:guid}/queue")]
    public Task<ActionResult<PaymentBatchExecutionDto>> QueueAsync(Guid companyId, Guid batchId,
        [FromBody] QueuePaymentExecutionRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            service.QueueAsync(new(companyId, batchId, request.ExpectedBatchVersion,
                request.BankConnectionId, request.CompanyBankAccountId, request.IdempotencyKey,
                Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{executionId:guid}/cancel")]
    public Task<ActionResult<PaymentBatchExecutionDto>> CancelAsync(Guid companyId, Guid executionId,
        [FromBody] CancelPaymentExecutionRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            service.CancelAsync(new(companyId, executionId, request.ExpectedVersion, request.Reason,
                request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{executionId:guid}/reconcile")]
    public Task<ActionResult<PaymentBatchExecutionDto>> ReconcileAsync(Guid companyId, Guid executionId,
        [FromBody] ReconcilePaymentExecutionRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            service.ReconcileAsync(new(companyId, executionId, request.ExpectedVersion,
                request.ProviderPaymentId, request.Reason, request.IdempotencyKey, Actor(),
                HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{executionId:guid}/settle")]
    public Task<ActionResult<PaymentBatchExecutionDto>> SettleAsync(Guid companyId, Guid executionId,
        [FromBody] SettlePaymentExecutionRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
            service.SettleAsync(new(companyId, executionId, request.ExpectedVersion,
                request.BankTransactionId, request.ExpectedBankTransactionSourceVersion,
                request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("{executionId:guid}/remittances/{remittanceId:guid}/retry")]
    public Task<ActionResult<PaymentBatchExecutionDto>> RetryRemittanceAsync(Guid companyId, Guid executionId,
        Guid remittanceId, [FromBody] RetryPaymentRemittanceRequest request,
        CancellationToken cancellationToken) => ExecuteAsync(() => service.RetryRemittanceAsync(new(
            companyId, executionId, remittanceId, request.ExpectedExecutionVersion,
            request.IdempotencyKey, Actor(), HttpContext.TraceIdentifier), cancellationToken));

    private async Task<ActionResult<PaymentBatchExecutionDto>> ExecuteAsync(
        Func<Task<PaymentBatchExecutionDto>> action)
    {
        try { return Ok(await action()); }
        catch (PaymentExecutionException exception)
        {
            var status = exception.ReasonCode == PaymentExecutionReasonCodes.NotFound
                ? StatusCodes.Status404NotFound
                : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            var problem = Problem(exception.Message, status);
            problem.Extensions["reasonCode"] = exception.ReasonCode;
            if (exception.CurrentVersion.HasValue)
                problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
            return StatusCode(status, problem);
        }
        catch (DbUpdateConcurrencyException)
        { return Conflict(Problem("The payment execution changed after it was opened.", 409)); }
        catch (DbUpdateException)
        { return Conflict(Problem("The request conflicts with retained payment execution evidence.", 409)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(Problem(exception.Message, 400)); }
        catch (InvalidOperationException exception) { return Conflict(Problem(exception.Message, 409)); }
    }

    private Guid Actor() => currentUser.UserId
        ?? throw new UnauthorizedAccessException("A resolved company user is required.");
    private ProblemDetails Problem(string detail, int status) => new()
    {
        Title = "Payment execution request failed",
        Detail = detail,
        Status = status,
        Instance = HttpContext.Request.Path
    };
}

public sealed record QueuePaymentExecutionRequest(long ExpectedBatchVersion, Guid BankConnectionId,
    Guid CompanyBankAccountId, string IdempotencyKey);
public sealed record CancelPaymentExecutionRequest(long ExpectedVersion, string Reason, string IdempotencyKey);
public sealed record ReconcilePaymentExecutionRequest(long ExpectedVersion, string? ProviderPaymentId,
    string Reason, string IdempotencyKey);
public sealed record SettlePaymentExecutionRequest(long ExpectedVersion, Guid BankTransactionId,
    long ExpectedBankTransactionSourceVersion, string IdempotencyKey);
public sealed record RetryPaymentRemittanceRequest(long ExpectedExecutionVersion, string IdempotencyKey);
