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
public sealed class InternalFinanceAccountingProviderSwitchMonitoringController : InternalFinanceControllerBase
{
    private readonly IAccountingProviderSwitchMonitoringService _accountingProviderSwitchMonitoringService;

    public InternalFinanceAccountingProviderSwitchMonitoringController(
        IAccountingProviderSwitchMonitoringService accountingProviderSwitchMonitoringService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingProviderSwitchMonitoringController> logger)
        : base(initializationProblems, logger)
    {
        _accountingProviderSwitchMonitoringService = accountingProviderSwitchMonitoringService;
    }
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
