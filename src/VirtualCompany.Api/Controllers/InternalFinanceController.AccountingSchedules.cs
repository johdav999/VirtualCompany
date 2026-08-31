using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
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

public sealed class SaveAccountingScheduleRequest
{
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "recurring_fixed"; public string Cadence { get; set; } = "monthly";
    public string AmountBasis { get; set; } = "per_occurrence"; public string ProrationRule { get; set; } = "none";
    public DateOnly StartDate { get; set; } public DateOnly? EndDate { get; set; } public int OccurrenceDay { get; set; } = 1;
    public string TimeZoneId { get; set; } = "Europe/Stockholm"; public string VoucherSeriesCode { get; set; } = "A";
    public string Currency { get; set; } = "SEK"; public string ReversalRule { get; set; } = "none";
    public string Description { get; set; } = string.Empty; public List<AccountingScheduleLineRequest> Lines { get; set; } = [];
    public List<Guid> EvidenceDocumentIds { get; set; } = []; public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public AccountingScheduleInput ToInput() => new(Code, Name, ScheduleType, Cadence, AmountBasis,
        ProrationRule, StartDate, EndDate, OccurrenceDay, TimeZoneId, VoucherSeriesCode, Currency,
        ReversalRule, Description, Lines.Select(x => new AccountingScheduleLineInput(x.FinanceAccountId,
            x.DebitAmount, x.CreditAmount, x.Description, x.DimensionMemberIds)).ToArray(), EvidenceDocumentIds);
}
public sealed class AccountingScheduleLineRequest
{ public Guid FinanceAccountId { get; set; } public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string Description { get; set; } = string.Empty; public List<Guid> DimensionMemberIds { get; set; } = []; }
public class AccountingScheduleVersionRequest { public long ExpectedVersion { get; set; } }
public sealed class AccountingScheduleActionRequest : AccountingScheduleVersionRequest { public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class DecideAccountingScheduleApprovalRequest : AccountingScheduleVersionRequest { public bool Approve { get; set; } public string? Comment { get; set; } public Guid ClientRequestId { get; set; } }
public sealed class ChangeAccountingScheduleStateRequest : AccountingScheduleVersionRequest { public bool GenerateMissed { get; set; } }
