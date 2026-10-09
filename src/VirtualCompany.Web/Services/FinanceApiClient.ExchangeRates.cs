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
