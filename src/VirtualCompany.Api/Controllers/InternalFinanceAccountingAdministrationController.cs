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
public sealed class InternalFinanceAccountingAdministrationController : InternalFinanceControllerBase
{
    private readonly IAccountingAdministrationService _accountingAdministrationService;

    public InternalFinanceAccountingAdministrationController(
        IAccountingAdministrationService accountingAdministrationService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingAdministrationController> logger)
        : base(initializationProblems, logger)
    {
        _accountingAdministrationService = accountingAdministrationService;
    }
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/policy-packs")]
    public async Task<ActionResult<IReadOnlyList<AccountingPolicyPackOptionDto>>> GetAccountingPolicyPacksAsync(
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAdministrationService.GetPolicyPacksAsync(cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/setup/preview")]
    public async Task<ActionResult<AccountingSetupPreviewDto>> PreviewAccountingSetupAsync(
        Guid companyId,
        [FromBody] PreviewAccountingSetupRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingAdministrationService.PreviewSetupAsync(
                new PreviewAccountingSetupQuery(
                    companyId,
                    request.BaseCurrency,
                    request.FiscalYearStart,
                    request.PolicyPackKey,
                    request.PolicyPackVersion,
                    request.ChartTemplateKey,
                    request.AccountRoleCodeAssignments),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/setup/complete")]
    public async Task<ActionResult<AccountingSetupCompletionDto>> CompleteAccountingSetupAsync(
        Guid companyId,
        [FromBody] CompleteAccountingSetupRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingAdministrationService.CompleteSetupAsync(
                new CompleteAccountingSetupCommand(
                    companyId,
                    request.BaseCurrency,
                    request.FiscalYearStart,
                    request.PolicyPackKey,
                    request.PolicyPackVersion,
                    request.ChartTemplateKey,
                    request.AccountRoleCodeAssignments,
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for accounting setup."),
                    request.IdempotencyKey,
                    ResolveCorrelationId()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/accounts")]
    public async Task<ActionResult<IReadOnlyList<AccountingAccountListItemDto>>> GetAccountingAccountsAsync(
        Guid companyId,
        [FromQuery] string? search,
        [FromQuery] string? accountClass,
        [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingAdministrationService.GetAccountsAsync(
                new GetAccountingAccountsQuery(companyId, search, accountClass, status),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/accounts/{accountId:guid}")]
    public async Task<ActionResult<AccountingAccountDetailDto>> GetAccountingAccountAsync(
        Guid companyId,
        Guid accountId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingAdministrationService.GetAccountAsync(
                new GetAccountingAccountQuery(companyId, accountId),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/accounts")]
    public async Task<ActionResult<AccountingAccountDetailDto>> CreateAccountingAccountAsync(
        Guid companyId,
        [FromBody] CreateAccountingAccountRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingAdministrationService.CreateAccountAsync(
                new CreateAccountingAccountCommand(
                    companyId,
                    request.Code,
                    request.Name,
                    request.AccountClass,
                    request.NormalBalance,
                    request.EffectiveFrom,
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for account administration."),
                    ResolveCorrelationId()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/chart-catalogs/{catalogKey}/{catalogVersion}/accounts")]
    public async Task<ActionResult<AccountingChartCatalogPageDto>> GetAccountingChartCatalogAsync(
        Guid companyId,
        string catalogKey,
        string catalogVersion,
        [FromQuery] string? search,
        [FromQuery] string? groupCode,
        [FromQuery] bool k2Only,
        [FromQuery] bool excludeExisting,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        await ExecuteReadAsync(() =>
            _accountingAdministrationService.GetChartCatalogAsync(
                new GetAccountingChartCatalogQuery(
                    companyId,
                    catalogKey,
                    catalogVersion,
                    search,
                    groupCode,
                    k2Only,
                    excludeExisting,
                    skip,
                    take),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/accounts/from-chart-catalog")]
    public async Task<ActionResult<AccountingAccountDetailDto>> CreateAccountingAccountFromChartCatalogAsync(
        Guid companyId,
        [FromBody] CreateAccountingAccountFromChartCatalogRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingAdministrationService.CreateAccountFromCatalogAsync(
                new CreateAccountingAccountFromCatalogCommand(
                    companyId,
                    request.CatalogKey,
                    request.CatalogVersion,
                    request.Code,
                    request.NameSv,
                    request.AccountClass,
                    request.NormalBalance,
                    request.AccountingSemanticsConfirmed,
                    request.CompanySuitabilityConfirmed,
                    request.EffectiveFrom,
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for account administration."),
                    ResolveCorrelationId()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/accounts/{accountId:guid}/name")]
    public async Task<ActionResult<AccountingAccountDetailDto>> RenameAccountingAccountAsync(
        Guid companyId,
        Guid accountId,
        [FromBody] RenameAccountingAccountRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingAdministrationService.RenameAccountAsync(
                new RenameAccountingAccountCommand(
                    companyId,
                    accountId,
                    request.Name,
                    request.ExpectedUpdatedUtc,
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for account administration."),
                    ResolveCorrelationId()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/accounts/{accountId:guid}/deactivate")]
    public async Task<ActionResult<AccountingAccountDetailDto>> DeactivateAccountingAccountAsync(
        Guid companyId,
        Guid accountId,
        [FromBody] DeactivateAccountingAccountRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingAdministrationService.DeactivateAccountAsync(
                new DeactivateAccountingAccountCommand(
                    companyId,
                    accountId,
                    request.EffectiveTo,
                    request.ExpectedUpdatedUtc,
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for account administration."),
                    ResolveCorrelationId()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/accounts/{accountId:guid}/lifecycle/preview")]
    public async Task<ActionResult<AccountingAccountLifecyclePreviewDto>> PreviewAccountingAccountLifecycleAsync(
        Guid companyId, Guid accountId, [FromBody] PreviewAccountingAccountLifecycleRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAdministrationService.PreviewAccountLifecycleAsync(new(
            companyId, accountId, request.EffectiveFrom, request.EffectiveTo, request.ReplacementAccountId,
            request.AccountClass, request.NormalBalance, request.IsReportable, request.PostingRestriction), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/accounts/{accountId:guid}/lifecycle")]
    public async Task<ActionResult<AccountingAccountDetailDto>> ApplyAccountingAccountLifecycleAsync(
        Guid companyId, Guid accountId, [FromBody] ApplyAccountingAccountLifecycleRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAdministrationService.ApplyAccountLifecycleAsync(new(
            companyId, accountId, request.Name, request.AccountClass, request.NormalBalance,
            request.IsReportable, request.PostingRestriction, request.EffectiveFrom, request.EffectiveTo,
            request.ReplacementAccountId, request.Reason, request.ExpectedLifecycleVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for account lifecycle administration."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/series-policies")]
    public async Task<ActionResult<IReadOnlyList<AccountingSeriesPolicyDto>>> GetAccountingSeriesPoliciesAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAdministrationService.GetSeriesPoliciesAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/series-policies")]
    public async Task<ActionResult<AccountingSeriesPolicyDto>> SaveAccountingSeriesPolicyAsync(
        Guid companyId, [FromBody] SaveAccountingSeriesPolicyRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAdministrationService.SaveSeriesPolicyAsync(new(
            companyId, request.PolicyId, request.SeriesKind, request.SeriesId, request.SourceType,
            request.TransactionType, request.FiscalYear, request.LocationDimensionMemberId, request.Jurisdiction,
            request.ProviderKey, request.ProviderSeriesCode, request.IsActive, request.ExpectedVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for series administration."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/voucher-series/{seriesId:guid}/gaps")]
    public async Task<ActionResult<AccountingSeriesPolicyDto>> RecordAccountingVoucherGapAsync(
        Guid companyId, Guid seriesId, [FromBody] RecordVoucherGapEvidenceRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAdministrationService.RecordVoucherGapEvidenceAsync(new(
            companyId, seriesId, request.FiscalYear, request.MissingNumber, request.Reason,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for series administration."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/commerce/capability")]
    public async Task<ActionResult<CommerceAccountingCapabilityDto>> GetCommerceAccountingCapabilityAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAdministrationService.GetCommerceCapabilityAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/commerce/events")]
    public async Task<ActionResult<CommerceAccountingEventResultDto>> SubmitCommerceAccountingEventAsync(
        Guid companyId, [FromBody] SubmitCommerceAccountingEventRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAdministrationService.SubmitCommerceEventAsync(new(
            companyId, request.EventId, request.EventVersion, request.ContractVersion, request.EventType,
            request.SourceSystem, request.OccurredUtc, request.RequiresInventoryAccounting,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for commerce accounting integration."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/fiscal-years")]
    public async Task<ActionResult<IReadOnlyList<AccountingFiscalYearDto>>> GetAccountingFiscalYearsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingAdministrationService.GetFiscalYearsAsync(
                new GetAccountingPeriodsQuery(companyId),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/periods/{periodId:guid}")]
    public async Task<ActionResult<AccountingPeriodDto>> GetAccountingPeriodAsync(
        Guid companyId,
        Guid periodId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingAdministrationService.GetPeriodAsync(
                new GetAccountingPeriodQuery(companyId, periodId),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpPost("accounting/fiscal-years/preview")]
    public async Task<ActionResult<AccountingFiscalYearPreviewDto>> PreviewAccountingFiscalYearAsync(
        Guid companyId,
        [FromBody] PreviewAccountingFiscalYearRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() =>
            _accountingAdministrationService.PreviewFiscalYearAsync(
                new PreviewAccountingFiscalYearQuery(companyId, request.FiscalYearStart),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/fiscal-years")]
    public async Task<ActionResult<AccountingFiscalYearCreationDto>> CreateAccountingFiscalYearAsync(
        Guid companyId,
        [FromBody] CreateAccountingFiscalYearRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() =>
            _accountingAdministrationService.CreateFiscalYearAsync(
                new CreateAccountingFiscalYearCommand(
                    companyId,
                    request.FiscalYearStart,
                    ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required for period administration."),
                    request.IdempotencyKey,
                    ResolveCorrelationId()),
                cancellationToken));

}
