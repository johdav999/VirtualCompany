using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class RiksbankExchangeRateOptions
{
    public const string SectionName = "ExchangeRates:Riksbank";
    public const string HttpClientName = "riksbank-exchange-rates";
    public bool Enabled { get; set; } = true;
    public string ApiBaseUrl { get; set; } = "https://api.riksbank.se/swea/v1/";
    public string SubscriptionKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;
}

public sealed class RiksbankExchangeRateProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<RiksbankExchangeRateOptions> options,
    TimeProvider timeProvider) : IExchangeRateProvider
{
    public ExchangeRateProviderDescriptor Descriptor { get; } = new(
        "riksbank_swea",
        "Sveriges Riksbank SWEA",
        "swea-v1",
        "SEK",
        100,
        false,
        7,
        24,
        "Indicative reference rates from Sveriges Riksbank. Source attribution is required; the rates are not transactional prices.",
        ["EUR", "USD", "GBP", "NOK", "DKK", "CHF"]);

    public async Task<ExchangeRateProviderResponse> FetchAsync(
        ExchangeRateProviderRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ProviderUnavailable,
                "The Sveriges Riksbank exchange-rate source is disabled.", false);

        var currencies = request.Currencies.Count == 0
            ? Descriptor.DefaultCurrencies.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : request.Currencies.Select(NormalizeCurrency).ToHashSet(StringComparer.OrdinalIgnoreCase);
        currencies.Remove(Descriptor.BaseCurrency);

        using var message = new HttpRequestMessage(HttpMethod.Get, "Observations/Latest/ByGroup/130");
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            message.Headers.TryAddWithoutValidation("X-Correlation-ID", request.CorrelationId);
        if (!string.IsNullOrWhiteSpace(options.Value.SubscriptionKey))
            message.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", options.Value.SubscriptionKey.Trim());

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(RiksbankExchangeRateOptions.HttpClientName)
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ProviderFailure,
                "The Sveriges Riksbank rate request timed out and can be retried.", true);
        }
        catch (HttpRequestException exception)
        {
            throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ProviderFailure,
                "The Sveriges Riksbank rate source is temporarily unavailable.", true, innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                var retryAfter = response.Headers.RetryAfter?.Delta;
                throw new ExchangeRateProviderException(
                    retryable ? ExchangeRateReasonCodes.ProviderFailure : ExchangeRateReasonCodes.ProviderUnavailable,
                    retryable
                        ? "The Sveriges Riksbank rate source could not complete the request and can be retried."
                        : "The Sveriges Riksbank rate request was rejected. Review the provider configuration.",
                    retryable,
                    retryAfter);
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            List<RiksbankObservation>? payload;
            try
            {
                payload = JsonSerializer.Deserialize<List<RiksbankObservation>>(raw,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException exception)
            {
                throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ProviderPayloadInvalid,
                    "The Sveriges Riksbank rate response was not in the supported format.", false,
                    innerException: exception);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var observations = (payload ?? [])
                .Select(Parse)
                .Where(x => x is not null && currencies.Contains(x.QuoteCurrency) && x.EffectiveDate <= request.RequestedDate)
                .Select(x => x!)
                .OrderBy(x => x.QuoteCurrency, StringComparer.Ordinal)
                .ToArray();
            if (observations.Length == 0)
                throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ProviderPayloadInvalid,
                    "The Sveriges Riksbank response did not contain an eligible requested currency rate.", false);

            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            var latestDate = observations.Max(x => x.EffectiveDate);
            return new ExchangeRateProviderResponse(
                $"riksbank-swea:{latestDate:yyyyMMdd}:{checksum[..16]}",
                now,
                observations.Select(x => x with { ObservedUtc = now }).ToArray(),
                raw,
                response.Content.Headers.ContentType?.MediaType ?? "application/json");
        }
    }

    private static ExchangeRateProviderObservation? Parse(RiksbankObservation row)
    {
        if (string.IsNullOrWhiteSpace(row.SeriesId) || row.Value <= 0m ||
            !DateOnly.TryParse(row.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        var series = row.SeriesId.Trim().ToUpperInvariant();
        if (series.Length != 9 || !series.StartsWith("SEK", StringComparison.Ordinal) ||
            !series.EndsWith("PMI", StringComparison.Ordinal)) return null;
        var quote = series.Substring(3, 3);
        if (quote.Any(character => character is < 'A' or > 'Z')) return null;
        return new ExchangeRateProviderObservation(
            $"{series}:{date:yyyy-MM-dd}", "SEK", quote, row.Value, DecimalScale(row.Value),
            ExchangeRateQuotationConventions.BaseCurrencyPerQuoteCurrency, date, DateTime.UnixEpoch);
    }

    private static int DecimalScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static string NormalizeCurrency(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ExchangeRateProviderException(ExchangeRateReasonCodes.UnsupportedCurrency,
                "A requested currency code was invalid.", false);
        return normalized;
    }

    private sealed record RiksbankObservation(string SeriesId, string Date, decimal Value);
}

public sealed class ExchangeRateProviderRegistry(IEnumerable<IExchangeRateProvider> providers)
    : IExchangeRateProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IExchangeRateProvider> _providers = providers
        .GroupBy(x => x.Descriptor.ProviderKey, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Exchange-rate provider '{group.Key}' is registered more than once."),
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ExchangeRateProviderDescriptor> GetAll() => _providers.Values
        .Select(x => x.Descriptor).OrderBy(x => x.DisplayName, StringComparer.Ordinal).ToArray();

    public IExchangeRateProvider GetRequired(string providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey) || !_providers.TryGetValue(providerKey.Trim(), out var provider))
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ProviderUnavailable,
                "The selected exchange-rate provider is not available.");
        return provider;
    }
}
