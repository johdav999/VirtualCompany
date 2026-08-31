namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public async Task<IReadOnlyList<CurrencyDefinitionResponse>> GetExchangeRateCurrenciesAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<CurrencyDefinitionResponse>>(companyId,
            $"api/companies/{companyId}/finance/exchange-rates/currencies", allowNotFound: false, cancellationToken) ?? [];

    public async Task<IReadOnlyList<ExchangeRateSourceResponse>> GetExchangeRateSourcesAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<ExchangeRateSourceResponse>>(companyId,
            $"api/companies/{companyId}/finance/exchange-rates/sources", allowNotFound: false, cancellationToken) ?? [];

    public Task<ExchangeRateReadinessResponse?> GetExchangeRateReadinessAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<ExchangeRateReadinessResponse>(companyId,
            $"api/companies/{companyId}/finance/exchange-rates/readiness", allowNotFound: false, cancellationToken);

    public Task<ExchangeRateObservationResponse?> GetExchangeRateObservationAsync(
        Guid companyId, Guid observationId, CancellationToken cancellationToken = default) =>
        GetAsync<ExchangeRateObservationResponse>(companyId,
            $"api/companies/{companyId}/finance/exchange-rates/observations/{observationId:D}", allowNotFound: true, cancellationToken);

    public Task<ExchangeRateLookupResponse?> LookupExchangeRateAsync(Guid companyId, string fromCurrency,
        string toCurrency, DateOnly date, string purpose, CancellationToken cancellationToken = default) =>
        GetAsync<ExchangeRateLookupResponse>(companyId,
            $"api/companies/{companyId}/finance/exchange-rates/lookup?fromCurrency={Uri.EscapeDataString(fromCurrency)}&toCurrency={Uri.EscapeDataString(toCurrency)}&date={date:yyyy-MM-dd}&purpose={Uri.EscapeDataString(purpose)}",
            allowNotFound: false, cancellationToken);

    public Task<ExchangeRateRefreshJobResponse> QueueExchangeRateRefreshAsync(Guid companyId,
        QueueExchangeRateRefreshApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<QueueExchangeRateRefreshApiRequest, ExchangeRateRefreshJobResponse>(companyId,
            HttpMethod.Post, $"api/companies/{companyId}/finance/exchange-rates/provider-refreshes", request,
            cancellationToken);
    }
}

public sealed class CurrencyDefinitionResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MinorUnitPrecision { get; set; }
    public bool IsEnabled { get; set; }
    public long Version { get; set; }
}

public sealed class ExchangeRateSourceResponse
{
    public Guid Id { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool RequiresApproval { get; set; }
    public int MaxStalenessDays { get; set; }
    public int RefreshIntervalHours { get; set; }
    public string LicenseSummary { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime? LastSuccessfulRefreshUtc { get; set; }
    public DateTime? NextRefreshUtc { get; set; }
    public string? LastFailureReasonCode { get; set; }
    public string? LastFailureSummary { get; set; }
    public long Version { get; set; }
}

public sealed class ExchangeRateReadinessResponse
{
    public string Status { get; set; } = string.Empty;
    public string? FunctionalCurrency { get; set; }
    public int EnabledCurrencyCount { get; set; }
    public int EnabledSourceCount { get; set; }
    public int PendingReviewSetCount { get; set; }
    public int FailedRefreshJobCount { get; set; }
    public DateTime? LatestApprovedObservationUtc { get; set; }
    public List<ExchangeRateReadinessIssueResponse> Issues { get; set; } = [];
    public List<ExchangeRateSourceResponse> Sources { get; set; } = [];
}

public sealed class ExchangeRateReadinessIssueResponse
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

public sealed class ExchangeRateObservationResponse
{
    public Guid Id { get; set; }
    public Guid RateSetId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public long SourceSetVersion { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public int RatePrecision { get; set; }
    public string QuotationConvention { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public DateTime ObservedUtc { get; set; }
    public Guid? CorrectsObservationId { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string EvidenceChecksum { get; set; } = string.Empty;
}

public sealed class ExchangeRateLookupResponse
{
    public string Status { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public DateOnly RequestedDate { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public decimal? EffectiveRate { get; set; }
    public DateOnly? SelectedRateDate { get; set; }
    public List<ExchangeRateLookupLegResponse> Legs { get; set; } = [];
}

public sealed class QueueExchangeRateRefreshApiRequest
{
    public string ProviderKey { get; set; } = string.Empty;
    public DateOnly RequestedDate { get; set; }
    public List<string> Currencies { get; set; } = [];
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ExchangeRateRefreshJobResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly RequestedDate { get; set; }
    public List<string> RequestedCurrencies { get; set; } = [];
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? FailureReasonCode { get; set; }
    public string? FailureSummary { get; set; }
    public Guid? RateSetId { get; set; }
    public long Version { get; set; }
}
