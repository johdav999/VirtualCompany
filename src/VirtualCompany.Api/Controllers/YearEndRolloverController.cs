using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.YearEndGovernance)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/year-end-runs")]
public sealed class YearEndRolloverController(
    IYearEndRolloverService service,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<YearEndRunSummaryDto>>> ListAsync(
        Guid companyId, [FromQuery] int take = 20, CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(new(companyId, take), cancellationToken));

    [HttpGet("{runId:guid}")]
    public Task<ActionResult<YearEndRunDto>> GetAsync(Guid companyId, Guid runId,
        CancellationToken cancellationToken) => ExecuteAsync(() => service.GetAsync(new(companyId, runId), cancellationToken));

    [HttpPost]
    public Task<ActionResult<YearEndRunDto>> PrepareAsync(Guid companyId, PrepareYearEndRunRequest request,
        CancellationToken cancellationToken) => ExecuteAsync(() => service.PrepareAsync(new(companyId,
            request.FiscalYearStart, request.TargetFiscalPeriodId, request.RetainedEarningsAccountId,
            request.OpeningBalanceClearingAccountId, request.VoucherSeriesCode, request.IdempotencyKey,
            UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/readiness/refresh")]
    public Task<ActionResult<YearEndRunDto>> RefreshAsync(Guid companyId, Guid runId,
        YearEndVersionedRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.RefreshReadinessAsync(new(companyId, runId, request.ExpectedVersion, request.IdempotencyKey,
            UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/submit")]
    public Task<ActionResult<YearEndRunDto>> SubmitAsync(Guid companyId, Guid runId,
        YearEndEvidenceRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.SubmitAsync(new(companyId, runId, request.ExpectedVersion, request.ExpectedEvidenceHash,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/review")]
    public Task<ActionResult<YearEndRunDto>> ReviewAsync(Guid companyId, Guid runId,
        ReviewYearEndRunRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.ReviewAsync(new(companyId, runId, request.ExpectedVersion, request.ExpectedEvidenceHash,
            request.Approve, request.Reason, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/execute")]
    public Task<ActionResult<YearEndRunDto>> ExecuteRunAsync(Guid companyId, Guid runId,
        YearEndEvidenceRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.ExecuteAsync(new(companyId, runId, request.ExpectedVersion, request.ExpectedEvidenceHash,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/reconcile")]
    public Task<ActionResult<YearEndRunDto>> ReconcileAsync(Guid companyId, Guid runId,
        YearEndEvidenceRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.ReconcileAsync(new(companyId, runId, request.ExpectedVersion, request.ExpectedEvidenceHash,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/finalize")]
    public Task<ActionResult<YearEndRunDto>> FinalizeAsync(Guid companyId, Guid runId,
        YearEndVersionedRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.FinalizeAsync(new(companyId, runId, request.ExpectedVersion, request.IdempotencyKey,
            UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/subsequent-events")]
    public Task<ActionResult<YearEndRunDto>> RecordEventAsync(Guid companyId, Guid runId,
        RecordYearEndSubsequentEventRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.RecordSubsequentEventAsync(new(companyId, runId, request.EventDate, request.Title,
            request.Description, request.EstimatedAmount, request.Currency, request.Decision,
            request.OwnerUserId, request.EvidenceDocumentId, request.IdempotencyKey,
            UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/subsequent-events/{eventId:guid}/submit")]
    public Task<ActionResult<YearEndRunDto>> SubmitEventAsync(Guid companyId, Guid runId, Guid eventId,
        YearEndVersionedRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.SubmitSubsequentEventAsync(new(companyId, runId, eventId, request.ExpectedVersion,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/subsequent-events/{eventId:guid}/review")]
    public Task<ActionResult<YearEndRunDto>> ReviewEventAsync(Guid companyId, Guid runId, Guid eventId,
        ReviewYearEndSubsequentEventRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.ReviewSubsequentEventAsync(new(companyId, runId, eventId, request.ExpectedVersion,
            request.Approve, request.Reason, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [HttpPost("{runId:guid}/subsequent-events/{eventId:guid}/correction")]
    public Task<ActionResult<YearEndRunDto>> LinkCorrectionAsync(Guid companyId, Guid runId, Guid eventId,
        LinkYearEndCorrectionRequest request, CancellationToken cancellationToken) => ExecuteAsync(() =>
        service.LinkCorrectionAsync(new(companyId, runId, eventId, request.ExpectedVersion,
            request.CorrectionLedgerEntryId, request.ReopenRequestId, request.Reason,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");

    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value)
        ? value.ToString() : HttpContext.TraceIdentifier;

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (YearEndRolloverException exception)
        {
            var status = exception.ReasonCode == YearEndReasonCodes.NotFound
                ? StatusCodes.Status404NotFound
                : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            var problem = new ProblemDetails { Title = "Year-end action could not be completed", Detail = exception.Message,
                Status = status, Instance = HttpContext.Request.Path };
            problem.Extensions["reasonCode"] = exception.ReasonCode;
            if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
            return StatusCode(status, problem);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Year-end request is invalid", Detail = exception.Message,
                Status = StatusCodes.Status400BadRequest, Instance = HttpContext.Request.Path });
        }
    }
}

public sealed record PrepareYearEndRunRequest(DateOnly FiscalYearStart, Guid TargetFiscalPeriodId,
    Guid RetainedEarningsAccountId, Guid OpeningBalanceClearingAccountId, string VoucherSeriesCode,
    string IdempotencyKey);
public sealed record YearEndVersionedRequest(long ExpectedVersion, string IdempotencyKey);
public sealed record YearEndEvidenceRequest(long ExpectedVersion, string ExpectedEvidenceHash, string IdempotencyKey);
public sealed record ReviewYearEndRunRequest(long ExpectedVersion, string ExpectedEvidenceHash,
    bool Approve, string? Reason, string IdempotencyKey);
public sealed record RecordYearEndSubsequentEventRequest(DateOnly EventDate, string Title, string Description,
    decimal? EstimatedAmount, string Currency, string Decision, Guid OwnerUserId,
    Guid? EvidenceDocumentId, string IdempotencyKey);
public sealed record ReviewYearEndSubsequentEventRequest(long ExpectedVersion, bool Approve,
    string? Reason, string IdempotencyKey);
public sealed record LinkYearEndCorrectionRequest(long ExpectedVersion, Guid? CorrectionLedgerEntryId,
    Guid? ReopenRequestId, string Reason, string IdempotencyKey);
