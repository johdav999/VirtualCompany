using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutovers")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> ScheduleAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, [FromBody] ScheduleAccountingProviderSwitchCutoverRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchCutoverService.ScheduleAsync(new(companyId, switchId, request.PlanId,
            request.ExpectedSwitchVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), request.IdempotencyKey, ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/cutovers/latest")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> GetLatestAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _accountingProviderSwitchCutoverService.GetAsync(new(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/cutovers/{executionId:guid}")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> GetAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, Guid executionId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchCutoverService.GetAsync(
            new(companyId, switchId, executionId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutovers/{executionId:guid}/freeze")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> StartAccountingProviderSwitchFreezeAsync(
        Guid companyId, Guid switchId, Guid executionId, [FromBody] AccountingProviderSwitchCutoverVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchCutoverService.StartFreezeAsync(new(companyId, switchId, executionId,
            request.ExpectedVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutovers/{executionId:guid}/activation-approval")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> RequestAccountingProviderSwitchActivationApprovalAsync(
        Guid companyId, Guid switchId, Guid executionId, [FromBody] AccountingProviderSwitchCutoverVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchCutoverService.RequestActivationApprovalAsync(new(companyId, switchId,
            executionId, request.ExpectedVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutovers/{executionId:guid}/activate")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> ActivateAccountingProviderSwitchAsync(
        Guid companyId, Guid switchId, Guid executionId, [FromBody] AccountingProviderSwitchCutoverVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchCutoverService.ActivateAsync(new(companyId, switchId, executionId,
            request.ExpectedVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutovers/{executionId:guid}/cancel")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> CancelAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, Guid executionId, [FromBody] AccountingProviderSwitchCutoverRecoveryRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchCutoverService.CancelAsync(new(companyId, switchId, executionId,
            request.Reason, request.ExpectedVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutovers/{executionId:guid}/retry")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> ResumeAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, Guid executionId, [FromBody] AccountingProviderSwitchCutoverVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchCutoverService.ResumeAsync(new(companyId, switchId, executionId,
            request.ExpectedVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutovers/{executionId:guid}/recover")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverDto>> RecoverAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, Guid executionId, [FromBody] AccountingProviderSwitchCutoverRecoveryRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchCutoverService.RecoverAsync(new(companyId, switchId, executionId,
            request.Reason, request.ExpectedVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));
}

public sealed record ScheduleAccountingProviderSwitchCutoverRequest(Guid PlanId, long ExpectedSwitchVersion,
    string IdempotencyKey);
public sealed record AccountingProviderSwitchCutoverVersionRequest(long ExpectedVersion);
public sealed record AccountingProviderSwitchCutoverRecoveryRequest(string Reason, long ExpectedVersion);
