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
public sealed class AccountingCloseController(IAccountingCloseService service, ICompanyContextAccessor companyContext)
    : ControllerBase
{
    [HttpGet("templates")]
    public Task<AccountingCloseTemplateListResult> ListTemplatesAsync(Guid companyId, [FromQuery] string? status,
        [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default) =>
        service.ListTemplatesAsync(new(companyId, status, skip, take), cancellationToken);

    [HttpGet("templates/{templateId:guid}")]
    public async Task<ActionResult<AccountingCloseTemplateDto>> GetTemplateAsync(Guid companyId, Guid templateId,
        CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.GetTemplateAsync(new(companyId, templateId), cancellationToken));

    [HttpGet("templates/{templateId:guid}/versions/{templateVersionId:guid}/preview")]
    public async Task<ActionResult<AccountingCloseTemplatePreviewDto>> PreviewTemplateAsync(Guid companyId,
        Guid templateId, Guid templateVersionId, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.PreviewTemplateAsync(new(companyId, templateId, templateVersionId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("templates")]
    public async Task<ActionResult<AccountingCloseTemplateDto>> CreateTemplateAsync(Guid companyId,
        CreateAccountingCloseTemplateRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.CreateTemplateAsync(new(companyId, request.Template, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("templates/{templateId:guid}/versions")]
    public async Task<ActionResult<AccountingCloseTemplateDto>> VersionTemplateAsync(Guid companyId, Guid templateId,
        CreateAccountingCloseTemplateVersionRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.CreateTemplateVersionAsync(new(companyId, templateId, request.ExpectedVersion, request.Template,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("templates/{templateId:guid}/versions/{templateVersionId:guid}/copy")]
    public async Task<ActionResult<AccountingCloseTemplateDto>> CopyTemplateAsync(Guid companyId, Guid templateId,
        Guid templateVersionId, CopyAccountingCloseTemplateRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CopyTemplateAsync(new(companyId, templateId, templateVersionId,
            request.NewCode, request.NewName, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("templates/{templateId:guid}/versions/{templateVersionId:guid}/activate")]
    public async Task<ActionResult<AccountingCloseTemplateDto>> ActivateTemplateAsync(Guid companyId, Guid templateId,
        Guid templateVersionId, AccountingCloseVersionedRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.ActivateTemplateAsync(new(companyId, templateId, templateVersionId,
            request.ExpectedVersion, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("templates/{templateId:guid}/retire")]
    public async Task<ActionResult<AccountingCloseTemplateDto>> RetireTemplateAsync(Guid companyId, Guid templateId,
        RetireAccountingCloseTemplateRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.RetireTemplateAsync(new(companyId, templateId, request.TemplateVersionId, request.ExpectedVersion,
            request.Reason, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [HttpGet("instances")]
    public Task<AccountingCloseListResult> ListAsync(Guid companyId, [FromQuery] Guid? fiscalPeriodId,
        [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) => service.ListAsync(
        new(companyId, fiscalPeriodId, status, skip, take), cancellationToken);

    [HttpGet("instances/{closeInstanceId:guid}")]
    public async Task<ActionResult<AccountingCloseDto>> GetAsync(Guid companyId, Guid closeInstanceId,
        CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.GetAsync(new(companyId, closeInstanceId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances")]
    public async Task<ActionResult<AccountingCloseDto>> StartAsync(Guid companyId,
        StartAccountingCloseRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.StartAsync(new(companyId, request.FiscalPeriodId, request.TemplateId, request.TemplateVersionId,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("instances/{closeInstanceId:guid}/tasks/{closeTaskId:guid}/assign")]
    public async Task<ActionResult<AccountingCloseDto>> AssignAsync(Guid companyId, Guid closeInstanceId,
        Guid closeTaskId, AssignAccountingCloseTaskRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.AssignTaskAsync(new(companyId, closeInstanceId, closeTaskId,
            request.ExpectedVersion, request.OwnerUserId, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("instances/{closeInstanceId:guid}/tasks/{closeTaskId:guid}/complete")]
    public async Task<ActionResult<AccountingCloseDto>> CompleteAsync(Guid companyId, Guid closeInstanceId,
        Guid closeTaskId, CompleteAccountingCloseTaskRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CompleteTaskAsync(new(companyId, closeInstanceId, closeTaskId,
            request.ExpectedVersion, request.ReportedAmount, request.Evidence, request.Note,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/tasks/{closeTaskId:guid}/reopen")]
    public async Task<ActionResult<AccountingCloseDto>> ReopenAsync(Guid companyId, Guid closeInstanceId,
        Guid closeTaskId, AccountingCloseTaskReasonRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.ReopenTaskAsync(new(companyId, closeInstanceId, closeTaskId,
            request.ExpectedVersion, request.Reason, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/tasks/{closeTaskId:guid}/cancel")]
    public async Task<ActionResult<AccountingCloseDto>> CancelTaskAsync(Guid companyId, Guid closeInstanceId,
        Guid closeTaskId, AccountingCloseTaskReasonRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CancelTaskAsync(new(companyId, closeInstanceId, closeTaskId,
            request.ExpectedVersion, request.Reason, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/cancel")]
    public async Task<ActionResult<AccountingCloseDto>> CancelAsync(Guid companyId, Guid closeInstanceId,
        AccountingCloseInstanceReasonRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() =>
        service.CancelAsync(new(companyId, closeInstanceId, request.ExpectedVersion, request.Reason,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("instances/{closeInstanceId:guid}/tasks/{closeTaskId:guid}/blockers")]
    public async Task<ActionResult<AccountingCloseDto>> AddBlockerAsync(Guid companyId, Guid closeInstanceId,
        Guid closeTaskId, AddAccountingCloseTaskBlockerRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.AddBlockerAsync(new(companyId, closeInstanceId, closeTaskId,
            request.ExpectedVersion, request.ReasonCode, request.Explanation, request.SafeNextAction,
            request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("instances/{closeInstanceId:guid}/tasks/{closeTaskId:guid}/blockers/{blockerId:guid}/resolve")]
    public async Task<ActionResult<AccountingCloseDto>> ResolveBlockerAsync(Guid companyId, Guid closeInstanceId,
        Guid closeTaskId, Guid blockerId, AccountingCloseVersionedRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.ResolveBlockerAsync(new(companyId, closeInstanceId, closeTaskId,
            blockerId, request.ExpectedVersion, request.IdempotencyKey, UserId(), CorrelationId()), cancellationToken));

    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value)
        ? value.ToString() : HttpContext.TraceIdentifier;

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (AccountingCloseException exception)
        {
            var status = exception.ReasonCode == AccountingCloseReasonCodes.NotFound ? StatusCodes.Status404NotFound
                : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            var problem = new ProblemDetails { Title = "Accounting close action could not be completed",
                Detail = exception.Message, Status = status, Instance = HttpContext.Request.Path };
            problem.Extensions["reasonCode"] = exception.ReasonCode;
            if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
            return StatusCode(status, problem);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Accounting close request is invalid",
                Detail = exception.Message, Status = StatusCodes.Status400BadRequest, Instance = HttpContext.Request.Path });
        }
    }
}

public sealed record CreateAccountingCloseTemplateRequest(AccountingCloseTemplateInput Template, string IdempotencyKey);
public sealed record CreateAccountingCloseTemplateVersionRequest(long ExpectedVersion,
    AccountingCloseTemplateInput Template, string IdempotencyKey);
public sealed record CopyAccountingCloseTemplateRequest(string NewCode, string NewName, string IdempotencyKey);
public sealed record AccountingCloseVersionedRequest(long ExpectedVersion, string IdempotencyKey);
public sealed record RetireAccountingCloseTemplateRequest(Guid? TemplateVersionId, long ExpectedVersion,
    string Reason, string IdempotencyKey);
public sealed record StartAccountingCloseRequest(Guid FiscalPeriodId, Guid TemplateId, Guid? TemplateVersionId,
    string IdempotencyKey);
public sealed record AssignAccountingCloseTaskRequest(long ExpectedVersion, Guid OwnerUserId, string IdempotencyKey);
public sealed record CompleteAccountingCloseTaskRequest(long ExpectedVersion, decimal? ReportedAmount,
    IReadOnlyList<AccountingCloseEvidenceInput>? Evidence, string? Note, string IdempotencyKey);
public sealed record AccountingCloseTaskReasonRequest(long ExpectedVersion, string Reason, string IdempotencyKey);
public sealed record AccountingCloseInstanceReasonRequest(long ExpectedVersion, string Reason, string IdempotencyKey);
public sealed record AddAccountingCloseTaskBlockerRequest(long ExpectedVersion, string ReasonCode,
    string Explanation, string SafeNextAction, string IdempotencyKey);
