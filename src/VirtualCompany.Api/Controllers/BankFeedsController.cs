using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/bank-feeds")]
public sealed class BankFeedsController : ControllerBase
{
    private readonly IBankFeedService _service;
    private readonly ICompanyContextAccessor _companyContext;
    public BankFeedsController(IBankFeedService service, ICompanyContextAccessor companyContext)
    { _service = service; _companyContext = companyContext; }

    [HttpGet]
    public Task<BankFeedHealthResult> GetAsync(Guid companyId, CancellationToken cancellationToken) =>
        _service.GetHealthAsync(companyId, cancellationToken);

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("synchronize")]
    public async Task<ActionResult<BankFeedRequestResult>> SynchronizeAsync(Guid companyId,
        RequestBankFeedSynchronizationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.RequestSynchronizationAsync(new RequestBankFeedSynchronizationCommand(
                companyId, request.CheckpointId, UserId(), CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsHandled(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("{checkpointId:guid}/gaps/{gapId:guid}/backfill")]
    public async Task<ActionResult<BankFeedRequestResult>> BackfillAsync(Guid companyId, Guid checkpointId,
        Guid gapId, RequestBankFeedBackfillRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.RequestBackfillAsync(new RequestBankFeedBackfillCommand(companyId,
                checkpointId, gapId, request.DateFrom, request.DateTo, UserId(), request.ExpectedCheckpointVersion,
                request.Reason, CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsHandled(exception)) { return ProblemFor(exception); }
    }

    private Guid UserId() => _companyContext.UserId is { } id && id != Guid.Empty ? id :
        throw new UnauthorizedAccessException("A resolved user is required.");
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-Id", out var value)
        ? value.ToString() : HttpContext.TraceIdentifier;
    private static bool IsHandled(Exception exception) => exception is BankConnectionOperationException or
        ArgumentException or InvalidOperationException or KeyNotFoundException;
    private ActionResult ProblemFor(Exception exception)
    {
        var operation = exception as BankConnectionOperationException;
        var status = exception is KeyNotFoundException ? StatusCodes.Status404NotFound :
            operation?.IsConflict == true || exception is InvalidOperationException ? StatusCodes.Status409Conflict :
            StatusCodes.Status400BadRequest;
        var details = new ProblemDetails
        {
            Title = "Bank feed action could not be completed",
            Detail = operation?.SafeMessage ?? exception.Message,
            Status = status
        };
        details.Extensions["reasonCode"] = operation?.ReasonCode ?? "invalid_request";
        return StatusCode(status, details);
    }
}

public sealed record RequestBankFeedSynchronizationRequest(Guid? CheckpointId);
public sealed record RequestBankFeedBackfillRequest(DateOnly DateFrom, DateOnly DateTo,
    long ExpectedCheckpointVersion, string Reason);
