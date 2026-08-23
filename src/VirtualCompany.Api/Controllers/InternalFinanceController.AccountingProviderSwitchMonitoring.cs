using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/monitoring")]
    public async Task<ActionResult<AccountingProviderSwitchMonitoringDto>> GetAccountingProviderSwitchMonitoringAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _accountingProviderSwitchMonitoringService.GetAsync(new(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/operations")]
    public async Task<ActionResult<AccountingProviderSwitchOperationsDto>> GetAccountingProviderSwitchOperationsAsync(
        Guid companyId, CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _accountingProviderSwitchMonitoringService.GetOperationsAsync(new(companyId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/monitoring/run")]
    public async Task<ActionResult<AccountingProviderSwitchMonitoringDto>> RunAccountingProviderSwitchMonitoringAsync(
        Guid companyId, Guid switchId, [FromBody] AccountingProviderSwitchMonitoringVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchMonitoringService.RunNowAsync(new(companyId, switchId, request.ExpectedVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/monitoring/retry")]
    public async Task<ActionResult<AccountingProviderSwitchMonitoringDto>> RetryAccountingProviderSwitchMonitoringAsync(
        Guid companyId, Guid switchId, [FromBody] AccountingProviderSwitchMonitoringVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchMonitoringService.RetryAsync(new(companyId, switchId, request.ExpectedVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/monitoring/incidents/{incidentId:guid}/accept-exception")]
    public async Task<ActionResult<AccountingProviderSwitchMonitoringDto>> AcceptAccountingProviderSwitchMonitoringExceptionAsync(
        Guid companyId, Guid switchId, Guid incidentId,
        [FromBody] AcceptAccountingProviderSwitchMonitoringExceptionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchMonitoringService.AcceptExceptionAsync(new(companyId, switchId, incidentId,
            request.ExpectedVersion, request.Explanation, request.Scope, request.FinancialImpact,
            request.EvidenceReference, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/monitoring/closure-approval")]
    public async Task<ActionResult<AccountingProviderSwitchMonitoringDto>> RequestAccountingProviderSwitchMonitoringClosureAsync(
        Guid companyId, Guid switchId, [FromBody] AccountingProviderSwitchMonitoringVersionRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchMonitoringService.RequestClosureAsync(new(companyId, switchId, request.ExpectedVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/monitoring/close")]
    public async Task<ActionResult<AccountingProviderSwitchMonitoringDto>> CloseAccountingProviderSwitchMonitoringAsync(
        Guid companyId, Guid switchId, [FromBody] CloseAccountingProviderSwitchMonitoringRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchMonitoringService.CloseAsync(new(companyId, switchId, request.ExpectedVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            request.Summary, ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/monitoring/corrective-cutover")]
    public async Task<ActionResult<AccountingProviderSwitchMonitoringDto>> CreateCorrectiveAccountingProviderSwitchAsync(
        Guid companyId, Guid switchId, [FromBody] CreateCorrectiveAccountingProviderSwitchRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchMonitoringService.CreateCorrectiveCutoverAsync(new(companyId, switchId,
            request.EffectiveFiscalPeriodId, request.ExpectedVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            request.Reason, ResolveCorrelationId()), cancellationToken));
}

public sealed record AccountingProviderSwitchMonitoringVersionRequest(long ExpectedVersion);
public sealed record AcceptAccountingProviderSwitchMonitoringExceptionRequest(long ExpectedVersion,
    string Explanation, string Scope, decimal FinancialImpact, string EvidenceReference);
public sealed record CloseAccountingProviderSwitchMonitoringRequest(long ExpectedVersion, string Summary);
public sealed record CreateCorrectiveAccountingProviderSwitchRequest(Guid EffectiveFiscalPeriodId,
    long ExpectedVersion, string Reason);
