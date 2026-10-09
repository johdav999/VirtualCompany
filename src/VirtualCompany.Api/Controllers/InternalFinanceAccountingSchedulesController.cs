using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceAccountingSchedulesController : InternalFinanceControllerBase
{
    private readonly IAccountingScheduleService _accountingScheduleService;

    public InternalFinanceAccountingSchedulesController(
        IAccountingScheduleService accountingScheduleService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingSchedulesController> logger)
        : base(initializationProblems, logger)
    {
        _accountingScheduleService = accountingScheduleService;
    }
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/schedules")]
    public Task<ActionResult<AccountingScheduleListResult>> ListAccountingSchedulesAsync(Guid companyId,
        [FromQuery] string? status = null, [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) => ExecuteReadAsync(() =>
        _accountingScheduleService.ListAsync(new(companyId, status, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/schedules/{scheduleId:guid}")]
    public Task<ActionResult<AccountingScheduleDto>> GetAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _accountingScheduleService.GetAsync(new(companyId, scheduleId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/schedules")]
    public Task<ActionResult<AccountingScheduleDto>> CreateAccountingScheduleAsync(Guid companyId,
        [FromBody] SaveAccountingScheduleRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _accountingScheduleService.CreateAsync(new(companyId, request.ToInput(), request.IdempotencyKey,
            RequiredAccountingScheduleActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/schedules/{scheduleId:guid}")]
    public Task<ActionResult<AccountingScheduleDto>> UpdateAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        [FromBody] SaveAccountingScheduleRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _accountingScheduleService.UpdateAsync(new(companyId, scheduleId, request.ExpectedVersion,
            request.ToInput(), request.IdempotencyKey, RequiredAccountingScheduleActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/schedules/{scheduleId:guid}/preview")]
    public Task<ActionResult<AccountingSchedulePreviewDto>> PreviewAccountingScheduleAsync(Guid companyId,
        Guid scheduleId, [FromBody] AccountingScheduleVersionRequest request, CancellationToken cancellationToken) =>
        ExecuteReadAsync(() => _accountingScheduleService.PreviewAsync(new(companyId, scheduleId,
            request.ExpectedVersion, RequiredAccountingScheduleActor()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/schedules/{scheduleId:guid}/submit")]
    public Task<ActionResult<AccountingScheduleDto>> SubmitAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        [FromBody] AccountingScheduleActionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _accountingScheduleService.SubmitAsync(new(companyId, scheduleId, request.ExpectedVersion,
            request.IdempotencyKey, RequiredAccountingScheduleActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    [HttpPost("accounting/schedules/{scheduleId:guid}/approval")]
    public Task<ActionResult<AccountingScheduleDto>> DecideAccountingScheduleApprovalAsync(Guid companyId,
        Guid scheduleId, [FromBody] DecideAccountingScheduleApprovalRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _accountingScheduleService.DecideApprovalAsync(
        new(companyId, scheduleId, request.ExpectedVersion, request.Approve, request.Comment, request.ClientRequestId,
            RequiredAccountingScheduleActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/schedules/{scheduleId:guid}/activate")]
    public Task<ActionResult<AccountingScheduleDto>> ActivateAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        [FromBody] AccountingScheduleVersionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _accountingScheduleService.ActivateAsync(new(companyId, scheduleId, request.ExpectedVersion,
            RequiredAccountingScheduleActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/schedules/{scheduleId:guid}/{stateAction:regex(^(pause|resume|end)$)}")]
    public Task<ActionResult<AccountingScheduleDto>> ChangeAccountingScheduleStateAsync(Guid companyId,
        Guid scheduleId, string stateAction, [FromBody] ChangeAccountingScheduleStateRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _accountingScheduleService.ChangeStateAsync(
        new(companyId, scheduleId, request.ExpectedVersion, stateAction, request.GenerateMissed,
            RequiredAccountingScheduleActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/schedules/{scheduleId:guid}/occurrences/{occurrenceId:guid}/regenerate")]
    public Task<ActionResult<AccountingScheduleDto>> RegenerateAccountingScheduleOccurrenceAsync(Guid companyId,
        Guid scheduleId, Guid occurrenceId, [FromBody] AccountingScheduleVersionRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _accountingScheduleService.RegenerateOccurrenceAsync(
        new(companyId, scheduleId, occurrenceId, request.ExpectedVersion, RequiredAccountingScheduleActor(),
            ResolveCorrelationId()), cancellationToken));

    private Guid RequiredAccountingScheduleActor() => ResolveActorId() ??
        throw new UnauthorizedAccessException("A resolved company user is required.");

}
