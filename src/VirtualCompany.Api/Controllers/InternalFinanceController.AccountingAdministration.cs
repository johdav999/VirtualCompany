using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
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

public class PreviewAccountingSetupRequest
{
    public string BaseCurrency { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public string ChartTemplateKey { get; set; } = string.Empty;
    public Dictionary<string, string> AccountRoleCodeAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CompleteAccountingSetupRequest : PreviewAccountingSetupRequest
{
    public string? IdempotencyKey { get; set; }
}

public sealed class CreateAccountingAccountRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
}

public sealed class CreateAccountingAccountFromChartCatalogRequest
{
    public string CatalogKey { get; set; } = AccountingChartCatalogDefaults.Bas2026CatalogKey;
    public string CatalogVersion { get; set; } = AccountingChartCatalogDefaults.Bas2026CatalogVersion;
    public string Code { get; set; } = string.Empty;
    public string? NameSv { get; set; }
    public string? AccountClass { get; set; }
    public string? NormalBalance { get; set; }
    public bool AccountingSemanticsConfirmed { get; set; }
    public bool CompanySuitabilityConfirmed { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}

public sealed class RenameAccountingAccountRequest
{
    public string Name { get; set; } = string.Empty;
    public DateTime ExpectedUpdatedUtc { get; set; }
}

public sealed class DeactivateAccountingAccountRequest
{
    public DateOnly EffectiveTo { get; set; }
    public DateTime ExpectedUpdatedUtc { get; set; }
}

public class PreviewAccountingFiscalYearRequest
{
    public DateOnly FiscalYearStart { get; set; }
}

public sealed class CreateAccountingFiscalYearRequest : PreviewAccountingFiscalYearRequest
{
    public string? IdempotencyKey { get; set; }
}
