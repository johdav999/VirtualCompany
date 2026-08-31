using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.AccountingView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/accounting-close")]
public sealed class AccountingCloseGovernanceController(
    IAccountingCloseGovernanceService service, ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet("governance/policy")]
    public Task<AccountingClosePolicyDto> GetPolicyAsync(Guid companyId, CancellationToken cancellationToken) =>
        service.GetPolicyAsync(new(companyId), cancellationToken);

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("governance/policy")]
    public async Task<ActionResult<AccountingClosePolicyDto>> ConfigurePolicyAsync(Guid companyId,
        ConfigureAccountingClosePolicyRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.ConfigurePolicyAsync(new(companyId, request.ExpectedVersion, request.MaterialityThreshold,
            request.Currency, request.WaiverValidityHours, UserId(), CorrelationId()), cancellationToken));

    [HttpGet("instances/{closeInstanceId:guid}/governance")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> GetAsync(Guid companyId, Guid closeInstanceId,
        CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.GetAsync(new(companyId, closeInstanceId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("instances/{closeInstanceId:guid}/readiness/prepare")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> PrepareAsync(Guid companyId, Guid closeInstanceId,
        PrepareAccountingCloseReadinessRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.PrepareAsync(new(companyId, closeInstanceId, request.ExpectedInstanceVersion, request.Refresh,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/readiness/{snapshotId:guid}/submit")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> SubmitAsync(Guid companyId, Guid closeInstanceId,
        Guid snapshotId, AccountingCloseReadinessVersionedRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.SubmitAsync(new(companyId, closeInstanceId, snapshotId,
            request.ExpectedVersion, request.ExpectedEvidenceHash, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/readiness/{snapshotId:guid}/review")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> ReviewAsync(Guid companyId, Guid closeInstanceId,
        Guid snapshotId, ReviewAccountingCloseReadinessRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.ReviewAsync(new(companyId, closeInstanceId, snapshotId,
            request.ExpectedVersion, request.ExpectedEvidenceHash, request.Approve, request.Reason,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/readiness/{snapshotId:guid}/cancel")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> CancelAsync(Guid companyId, Guid closeInstanceId,
        Guid snapshotId, CancelAccountingCloseReadinessRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CancelAsync(new(companyId, closeInstanceId, snapshotId,
            request.ExpectedVersion, request.ExpectedEvidenceHash, request.Reason,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/readiness/{snapshotId:guid}/lock")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> LockAsync(Guid companyId, Guid closeInstanceId,
        Guid snapshotId, LockAccountingCloseRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.LockAsync(new(companyId, closeInstanceId, snapshotId,
            request.ExpectedVersion, request.ExpectedEvidenceHash, request.Reason,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/readiness/{snapshotId:guid}/waivers")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> ProposeWaiverAsync(Guid companyId,
        Guid closeInstanceId, Guid snapshotId, ProposeAccountingCloseWaiverRequest request,
        CancellationToken cancellationToken) => await ExecuteAsync(() => service.ProposeWaiverAsync(new(
            companyId, closeInstanceId, snapshotId, request.CheckCode, request.ExpectedCheckEvidenceHash,
            request.Reason, request.Amount, request.EvidenceDocumentId, request.ExpiresUtc,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/waivers/{waiverId:guid}/review")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> ReviewWaiverAsync(Guid companyId,
        Guid closeInstanceId, Guid waiverId, ReviewAccountingCloseWaiverRequest request,
        CancellationToken cancellationToken) => await ExecuteAsync(() => service.ReviewWaiverAsync(new(
            companyId, closeInstanceId, waiverId, request.Approve, request.Comment,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/reopen-requests")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> RequestReopenAsync(Guid companyId,
        Guid closeInstanceId, RequestAccountingCloseReopenRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.RequestReopenAsync(new(companyId, closeInstanceId,
            request.PriorSnapshotId, request.ExpectedSnapshotHash, request.Reason, request.Scope,
            request.CorrectionPath, request.ExpiresUtc, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/reopen-requests/{reopenRequestId:guid}/review")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> ReviewReopenAsync(Guid companyId,
        Guid closeInstanceId, Guid reopenRequestId, ReviewAccountingCloseReopenRequest request,
        CancellationToken cancellationToken) => await ExecuteAsync(() => service.ReviewReopenAsync(new(
            companyId, closeInstanceId, reopenRequestId, request.ExpectedVersion, request.Approve,
            request.Comment, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/reopen-requests/{reopenRequestId:guid}/execute")]
    public async Task<ActionResult<AccountingCloseGovernanceDto>> ExecuteReopenAsync(Guid companyId,
        Guid closeInstanceId, Guid reopenRequestId, ExecuteAccountingCloseReopenRequest request,
        CancellationToken cancellationToken) => await ExecuteAsync(() => service.ExecuteReopenAsync(new(
            companyId, closeInstanceId, reopenRequestId, request.ExpectedVersion, request.ExpectedSnapshotHash,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value)
        ? value.ToString() : HttpContext.TraceIdentifier;

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (AccountingCloseGovernanceException exception)
        {
            var status = exception.ReasonCode == AccountingCloseGovernanceReasonCodes.NotFound
                ? StatusCodes.Status404NotFound : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            var problem = new ProblemDetails { Title = "Accounting close governance action could not be completed",
                Detail = exception.Message, Status = status, Instance = HttpContext.Request.Path };
            problem.Extensions["reasonCode"] = exception.ReasonCode;
            if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
            return StatusCode(status, problem);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Accounting close governance request is invalid",
                Detail = exception.Message, Status = StatusCodes.Status400BadRequest, Instance = HttpContext.Request.Path });
        }
    }
}

public sealed record ConfigureAccountingClosePolicyRequest(long? ExpectedVersion, decimal MaterialityThreshold,
    string Currency, int WaiverValidityHours);
public sealed record PrepareAccountingCloseReadinessRequest(long ExpectedInstanceVersion, bool Refresh, string IdempotencyKey);
public sealed record AccountingCloseReadinessVersionedRequest(long ExpectedVersion, string ExpectedEvidenceHash, string IdempotencyKey);
public sealed record ReviewAccountingCloseReadinessRequest(long ExpectedVersion, string ExpectedEvidenceHash,
    bool Approve, string? Reason, string IdempotencyKey);
public sealed record CancelAccountingCloseReadinessRequest(long ExpectedVersion, string ExpectedEvidenceHash,
    string Reason, string IdempotencyKey);
public sealed record LockAccountingCloseRequest(long ExpectedVersion, string ExpectedEvidenceHash, string Reason, string IdempotencyKey);
public sealed record ProposeAccountingCloseWaiverRequest(string CheckCode, string ExpectedCheckEvidenceHash,
    string Reason, decimal? Amount, Guid EvidenceDocumentId, DateTime? ExpiresUtc, string IdempotencyKey);
public sealed record ReviewAccountingCloseWaiverRequest(bool Approve, string? Comment, string IdempotencyKey);
public sealed record RequestAccountingCloseReopenRequest(Guid PriorSnapshotId, string ExpectedSnapshotHash,
    string Reason, string Scope, string CorrectionPath, DateTime? ExpiresUtc, string IdempotencyKey);
public sealed record ReviewAccountingCloseReopenRequest(long ExpectedVersion, bool Approve, string? Comment, string IdempotencyKey);
public sealed record ExecuteAccountingCloseReopenRequest(long ExpectedVersion, string ExpectedSnapshotHash, string IdempotencyKey);
