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
public sealed class InternalFinanceAccountingProviderSwitchCutoverController : InternalFinanceControllerBase
{
    private readonly IAccountingProviderSwitchCutoverService _accountingProviderSwitchCutoverService;

    public InternalFinanceAccountingProviderSwitchCutoverController(
        IAccountingProviderSwitchCutoverService accountingProviderSwitchCutoverService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingProviderSwitchCutoverController> logger)
        : base(initializationProblems, logger)
    {
        _accountingProviderSwitchCutoverService = accountingProviderSwitchCutoverService;
    }
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
