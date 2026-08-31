using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/currency-revaluations")]
    public Task<ActionResult<CurrencyRevaluationRunListDto>> ListCurrencyRevaluationsAsync(Guid companyId,
        [FromQuery] Guid? fiscalPeriodId, [FromQuery] int skip = 0, [FromQuery] int take = 50,
        CancellationToken cancellationToken = default) => ExecuteReadAsync(() =>
        _currencyRevaluationService.ListAsync(new(companyId, fiscalPeriodId, skip, take), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/currency-revaluations/{runId:guid}")]
    public Task<ActionResult<CurrencyRevaluationRunDto>> GetCurrencyRevaluationAsync(Guid companyId, Guid runId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _currencyRevaluationService.GetAsync(new(companyId, runId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/currency-revaluations/preview")]
    public Task<ActionResult<CurrencyRevaluationRunDto>> PreviewCurrencyRevaluationAsync(Guid companyId,
        [FromBody] PreviewCurrencyRevaluationRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _currencyRevaluationService.PreviewAsync(new(companyId, request.FiscalPeriodId, request.VoucherSeriesCode,
            request.IdempotencyKey, RequiredCurrencyRevaluationActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/currency-revaluations/{runId:guid}/population/{populationItemId:guid}/review")]
    public Task<ActionResult<CurrencyRevaluationRunDto>> ReviewCurrencyRevaluationItemAsync(Guid companyId,
        Guid runId, Guid populationItemId, [FromBody] ReviewCurrencyRevaluationItemRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _currencyRevaluationService.ReviewItemAsync(
        new(companyId, runId, populationItemId, request.Action, request.Reason, request.ExpectedVersion,
            RequiredCurrencyRevaluationActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/currency-revaluations/{runId:guid}/submit")]
    public Task<ActionResult<CurrencyRevaluationRunDto>> SubmitCurrencyRevaluationAsync(Guid companyId, Guid runId,
        [FromBody] CurrencyRevaluationVersionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _currencyRevaluationService.SubmitAsync(new(companyId, runId, request.ExpectedVersion,
            RequiredCurrencyRevaluationActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/currency-revaluations/{runId:guid}/post")]
    public Task<ActionResult<CurrencyRevaluationRunDto>> PostCurrencyRevaluationAsync(Guid companyId, Guid runId,
        [FromBody] CurrencyRevaluationActionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _currencyRevaluationService.PostAsync(new(companyId, runId, request.ExpectedVersion, request.IdempotencyKey,
            RequiredCurrencyRevaluationActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/currency-revaluations/{runId:guid}/reverse")]
    public Task<ActionResult<CurrencyRevaluationRunDto>> ReverseCurrencyRevaluationAsync(Guid companyId, Guid runId,
        [FromBody] CurrencyRevaluationActionRequest request, CancellationToken cancellationToken) => ExecuteWriteAsync(() =>
        _currencyRevaluationService.ReverseAsync(new(companyId, runId, request.ExpectedVersion, request.IdempotencyKey,
            RequiredCurrencyRevaluationActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/currency-revaluation-accounts")]
    public Task<ActionResult<IReadOnlyList<CurrencyRevaluationAccountPolicyDto>>> ListCurrencyRevaluationAccountsAsync(
        Guid companyId, CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _currencyRevaluationService.ListAccountPoliciesAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/currency-revaluation-accounts/{financeAccountId:guid}")]
    public Task<ActionResult<CurrencyRevaluationAccountPolicyDto>> ConfigureCurrencyRevaluationAccountAsync(
        Guid companyId, Guid financeAccountId, [FromBody] ConfigureCurrencyRevaluationAccountRequest request,
        CancellationToken cancellationToken) => ExecuteWriteAsync(() => _currencyRevaluationService.ConfigureAccountAsync(
        new(companyId, financeAccountId, request.MonetaryClass, request.IsEnabled, request.ExpectedVersion,
            RequiredCurrencyRevaluationActor(), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/currency-revaluation-schedule")]
    public Task<ActionResult<CurrencyRevaluationScheduleDto?>> GetCurrencyRevaluationScheduleAsync(Guid companyId,
        CancellationToken cancellationToken) => ExecuteReadAsync(() =>
        _currencyRevaluationService.GetScheduleAsync(companyId, cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/currency-revaluation-schedule")]
    public Task<ActionResult<CurrencyRevaluationScheduleDto>> ConfigureCurrencyRevaluationScheduleAsync(Guid companyId,
        [FromBody] ConfigureCurrencyRevaluationScheduleRequest request, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(() => _currencyRevaluationService.ConfigureScheduleAsync(new(companyId, request.IsEnabled,
            request.DaysBeforePeriodEnd, request.AutomaticReversal, request.VoucherSeriesCode,
            request.ExpectedVersion, RequiredCurrencyRevaluationActor(), ResolveCorrelationId()), cancellationToken));

    private Guid RequiredCurrencyRevaluationActor() => ResolveActorId() ??
        throw new UnauthorizedAccessException("A resolved company user is required.");
}

public sealed class PreviewCurrencyRevaluationRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ReviewCurrencyRevaluationItemRequest
{
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
}

public class CurrencyRevaluationVersionRequest { public long ExpectedVersion { get; set; } }
public sealed class CurrencyRevaluationActionRequest : CurrencyRevaluationVersionRequest
{ public string IdempotencyKey { get; set; } = string.Empty; }

public sealed class ConfigureCurrencyRevaluationAccountRequest
{
    public string MonetaryClass { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public long? ExpectedVersion { get; set; }
}

public sealed class ConfigureCurrencyRevaluationScheduleRequest
{
    public bool IsEnabled { get; set; }
    public int DaysBeforePeriodEnd { get; set; }
    public bool AutomaticReversal { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public long? ExpectedVersion { get; set; }
}
