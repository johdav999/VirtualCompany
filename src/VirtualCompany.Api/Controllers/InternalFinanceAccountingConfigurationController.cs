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
public sealed class InternalFinanceAccountingConfigurationController : InternalFinanceControllerBase
{
    private readonly IAccountingConfigurationService _accountingConfigurationService;
    private readonly ICompanyStatutoryProfileService _companyStatutoryProfileService;

    public InternalFinanceAccountingConfigurationController(
        IAccountingConfigurationService accountingConfigurationService,
        ICompanyStatutoryProfileService companyStatutoryProfileService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingConfigurationController> logger)
        : base(initializationProblems, logger)
    {
        _accountingConfigurationService = accountingConfigurationService;
        _companyStatutoryProfileService = companyStatutoryProfileService;
    }
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/statutory-profile")]
    public async Task<ActionResult<CompanyStatutoryProfileStatusDto>> GetCompanyStatutoryProfileAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _companyStatutoryProfileService.GetAsync(
                new GetCompanyStatutoryProfileQuery(companyId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/statutory-profile")]
    public async Task<ActionResult<CompanyStatutoryProfileStatusDto>> CreateCompanyStatutoryProfileAsync(
        Guid companyId,
        [FromBody] SaveCompanyStatutoryProfileRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _companyStatutoryProfileService.CreateAsync(
                new CreateCompanyStatutoryProfileCommand(
                    companyId,
                    request.ToInput(),
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for statutory profile changes."),
                    ResolveCorrelationId()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/statutory-profile")]
    public async Task<ActionResult<CompanyStatutoryProfileStatusDto>> UpdateCompanyStatutoryProfileAsync(
        Guid companyId,
        [FromBody] SaveCompanyStatutoryProfileRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _companyStatutoryProfileService.UpdateAsync(
                new UpdateCompanyStatutoryProfileCommand(
                    companyId,
                    request.ExpectedVersion ?? throw new ArgumentException("ExpectedVersion is required for a statutory profile update."),
                    request.ToInput(),
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for statutory profile changes."),
                    ResolveCorrelationId()),
                cancellationToken));

    [HttpGet("accounting/setup-status")]
    public async Task<ActionResult<AccountingSetupStatusDto>> GetAccountingSetupStatusAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingConfigurationService.GetSetupStatusAsync(
                new GetAccountingSetupStatusQuery(companyId),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("accounting/configuration")]
    public async Task<ActionResult<AccountingSetupStatusDto>> CreateAccountingConfigurationAsync(
        Guid companyId,
        [FromBody] CreateAccountingConfigurationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingConfigurationService.CreateInitialAsync(
                new CreateInitialAccountingConfigurationCommand(
                    companyId,
                    request.BaseCurrency,
                    request.FiscalYearStartMonth,
                    request.FiscalYearStartDay,
                    string.IsNullOrWhiteSpace(request.PolicyPackKey) ? AccountingPolicyPackDefaults.CountryNeutralPackKey : request.PolicyPackKey,
                    string.IsNullOrWhiteSpace(request.PolicyPackVersion) ? AccountingPolicyPackDefaults.CountryNeutralVersion : request.PolicyPackVersion,
                    request.EffectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    request.RoundingPrecision,
                    string.IsNullOrWhiteSpace(request.RoundingMode) ? AccountingRoundingModeValues.MidpointToEven : request.RoundingMode,
                    request.AccountRoleAssignments ?? new Dictionary<string, Guid>(),
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for accounting setup."),
                    ResolveCorrelationId()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("accounting/policy-pack/preview")]
    public async Task<ActionResult<AccountingPolicyPackImpactPreviewDto>> PreviewAccountingPolicyPackAsync(
        Guid companyId,
        [FromBody] PreviewAccountingPolicyPackRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingConfigurationService.PreviewPolicyPackSelectionAsync(
                new PreviewAccountingPolicyPackSelectionQuery(
                    companyId,
                    request.PolicyPackKey,
                    request.PolicyPackVersion,
                    request.EffectiveFrom,
                    request.AccountRoleAssignments),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPut("accounting/policy-pack")]
    public async Task<ActionResult<AccountingSetupStatusDto>> ApplyAccountingPolicyPackAsync(
        Guid companyId,
        [FromBody] ApplyAccountingPolicyPackRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingConfigurationService.ApplyPolicyPackSelectionAsync(
                new ApplyAccountingPolicyPackSelectionCommand(
                    companyId,
                    request.PolicyPackKey,
                    request.PolicyPackVersion,
                    request.EffectiveFrom,
                    request.ExpectedVersion,
                    request.AccountRoleAssignments ?? new Dictionary<string, Guid>(),
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for accounting setup."),
                    ResolveCorrelationId()),
                cancellationToken));

    [HttpGet("accounting/validation")]
    public async Task<ActionResult<AccountingSetupStatusDto>> ValidateAccountingConfigurationAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingConfigurationService.ValidateAsync(
                new ValidateAccountingConfigurationQuery(companyId),
                cancellationToken));

    [HttpGet("accounting/capabilities/{capabilityKey}")]
    public async Task<ActionResult<AccountingCapabilityDecisionDto>> GetAccountingCapabilityAsync(
        Guid companyId,
        string capabilityKey,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingConfigurationService.GetCapabilityAsync(
                new GetAccountingCapabilityQuery(companyId, capabilityKey),
                cancellationToken));

}
