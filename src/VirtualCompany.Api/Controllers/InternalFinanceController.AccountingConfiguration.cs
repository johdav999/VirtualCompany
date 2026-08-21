using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
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

public sealed class CreateAccountingConfigurationRequest
{
    public string BaseCurrency { get; set; } = string.Empty;
    public int FiscalYearStartMonth { get; set; } = 1;
    public int FiscalYearStartDay { get; set; } = 1;
    public string? PolicyPackKey { get; set; }
    public string? PolicyPackVersion { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public int RoundingPrecision { get; set; } = 2;
    public string? RoundingMode { get; set; }
    public Dictionary<string, Guid>? AccountRoleAssignments { get; set; }
}

public sealed class PreviewAccountingPolicyPackRequest
{
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public Dictionary<string, Guid>? AccountRoleAssignments { get; set; }
}

public sealed class ApplyAccountingPolicyPackRequest
{
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public long ExpectedVersion { get; set; }
    public Dictionary<string, Guid>? AccountRoleAssignments { get; set; }
}
