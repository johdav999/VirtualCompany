using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.AccountingView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/exchange-rates")]
public sealed class ExchangeRatesController(
    IExchangeRateService service,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet("currencies")]
    public Task<IReadOnlyList<CurrencyDefinitionResult>> GetCurrenciesAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        service.GetCurrenciesAsync(companyId, cancellationToken);

    [HttpGet("sources")]
    public Task<IReadOnlyList<ExchangeRateSourceResult>> GetSourcesAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        service.GetSourcesAsync(companyId, cancellationToken);

    [HttpGet("observations/{observationId:guid}")]
    public async Task<ActionResult<ExchangeRateObservationResult>> GetObservationAsync(
        Guid companyId, Guid observationId, CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetObservationAsync(companyId, observationId, cancellationToken)); }
        catch (KeyNotFoundException exception) { return NotFound(Problem(exception.Message, StatusCodes.Status404NotFound)); }
    }

    [HttpGet("lookup")]
    public Task<ExchangeRateLookupResult> LookupAsync(Guid companyId, [FromQuery] string fromCurrency,
        [FromQuery] string toCurrency, [FromQuery] DateOnly date, [FromQuery] string purpose,
        CancellationToken cancellationToken) =>
        service.LookupAsync(new ExchangeRateLookupQuery(companyId, fromCurrency, toCurrency, date, purpose), cancellationToken);

    [HttpGet("readiness")]
    public Task<ExchangeRateReadinessResult> GetReadinessAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        service.GetReadinessAsync(companyId, cancellationToken);

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("currencies/{code}")]
    public async Task<ActionResult<CurrencyDefinitionResult>> ConfigureCurrencyAsync(Guid companyId,
        string code, ConfigureCurrencyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ConfigureCurrencyAsync(new ConfigureCurrencyCommand(companyId, UserId(), code,
                request.Name, request.MinorUnitPrecision, request.IsEnabled, request.ExpectedVersion,
                CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsOperation(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("sources/{sourceKey}")]
    public async Task<ActionResult<ExchangeRateSourceResult>> ConfigureSourceAsync(Guid companyId,
        string sourceKey, ConfigureExchangeRateSourceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ConfigureSourceAsync(new ConfigureExchangeRateSourceCommand(companyId,
                UserId(), sourceKey, request.Priority, request.RequiresApproval, request.MaxStalenessDays,
                request.RefreshIntervalHours, request.IsEnabled, request.ExpectedVersion, CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsOperation(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("manual-imports")]
    public async Task<ActionResult<ExchangeRateSetResult>> ImportManualAsync(Guid companyId,
        ImportManualExchangeRateSetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ImportManualAsync(new ImportManualExchangeRateSetCommand(companyId, UserId(),
                request.SourceKey, request.SourceDisplayName, request.ImportIdentity, request.PublishedUtc,
                request.Observations, request.EvidenceDescription, request.CorrectsRateSetId,
                CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsOperation(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("sets/{rateSetId:guid}/review")]
    public async Task<ActionResult<ExchangeRateSetResult>> ReviewSetAsync(Guid companyId, Guid rateSetId,
        ReviewExchangeRateSetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ReviewSetAsync(new ReviewExchangeRateSetCommand(companyId, UserId(), rateSetId,
                request.ExpectedVersion, request.Approve, request.ReviewNote, CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsOperation(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("provider-refreshes")]
    public async Task<ActionResult<ExchangeRateRefreshJobResult>> QueueRefreshAsync(Guid companyId,
        QueueExchangeRateRefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Accepted(await service.QueueRefreshAsync(new QueueExchangeRateRefreshCommand(companyId,
                UserId(), request.ProviderKey, request.RequestedDate, request.Currencies ?? [],
                request.IdempotencyKey, CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsOperation(exception)) { return ProblemFor(exception); }
    }

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("conversions")]
    public async Task<ActionResult<ExchangeRateConversionResult>> ConvertAsync(Guid companyId,
        ConvertCurrencyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ConvertAsync(new ConvertCurrencyCommand(companyId, UserId(), request.Amount,
                request.FromCurrency, request.ToCurrency, request.Date, request.Purpose,
                request.IdempotencyKey, CorrelationId()), cancellationToken));
        }
        catch (Exception exception) when (IsOperation(exception)) { return ProblemFor(exception); }
    }

    private Guid UserId() => companyContext.UserId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved user is required.");
    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value)
        ? value.ToString() : HttpContext.TraceIdentifier;
    private static bool IsOperation(Exception exception) => exception is ExchangeRateOperationException or
        ArgumentException or InvalidOperationException or KeyNotFoundException;

    private ActionResult ProblemFor(Exception exception)
    {
        if (exception is KeyNotFoundException) return NotFound(Problem(exception.Message, StatusCodes.Status404NotFound));
        var operation = exception as ExchangeRateOperationException;
        var status = operation?.IsConflict == true ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var details = Problem(operation?.SafeMessage ?? exception.Message, status);
        details.Extensions["reasonCode"] = operation?.ReasonCode ?? "invalid_request";
        return StatusCode(status, details);
    }

    private static ProblemDetails Problem(string detail, int status) => new()
    {
        Title = "Exchange-rate action could not be completed",
        Detail = detail,
        Status = status
    };
}

public sealed record ConfigureCurrencyRequest(string Name, int MinorUnitPrecision, bool IsEnabled, long? ExpectedVersion);
public sealed record ConfigureExchangeRateSourceRequest(int Priority, bool RequiresApproval,
    int MaxStalenessDays, int RefreshIntervalHours, bool IsEnabled, long? ExpectedVersion);
public sealed record ImportManualExchangeRateSetRequest(string SourceKey, string SourceDisplayName,
    string ImportIdentity, DateTime PublishedUtc, IReadOnlyList<ManualExchangeRateObservationInput> Observations,
    string EvidenceDescription, Guid? CorrectsRateSetId);
public sealed record ReviewExchangeRateSetRequest(long ExpectedVersion, bool Approve, string ReviewNote);
public sealed record QueueExchangeRateRefreshRequest(string ProviderKey, DateOnly RequestedDate,
    IReadOnlyCollection<string>? Currencies, string IdempotencyKey);
public sealed record ConvertCurrencyRequest(decimal Amount, string FromCurrency, string ToCurrency,
    DateOnly Date, string Purpose, string IdempotencyKey);
