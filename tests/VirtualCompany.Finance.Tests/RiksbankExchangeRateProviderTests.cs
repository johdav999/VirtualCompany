using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class RiksbankExchangeRateProviderTests
{
    [Fact]
    public async Task Adapter_parses_requested_SWEA_observations_with_explicit_quotation_and_raw_evidence()
    {
        const string payload = """
            [
              { "seriesId": "SEKEURPMI", "date": "2026-08-28", "value": 11.1234 },
              { "seriesId": "SEKUSDPMI", "date": "2026-08-28", "value": 9.8765 },
              { "seriesId": "NOT_A_RATE", "date": "2026-08-28", "value": 1 }
            ]
            """;
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler, "test-subscription-key");

        var result = await provider.FetchAsync(new ExchangeRateProviderRequest(Guid.NewGuid(),
            new DateOnly(2026, 8, 29), ["EUR"], "correlation-1"), default);

        var observation = Assert.Single(result.Observations);
        Assert.Equal("SEK", observation.BaseCurrency);
        Assert.Equal("EUR", observation.QuoteCurrency);
        Assert.Equal(11.1234m, observation.Rate);
        Assert.Equal(4, observation.RatePrecision);
        Assert.Equal(ExchangeRateQuotationConventions.BaseCurrencyPerQuoteCurrency, observation.QuotationConvention);
        Assert.Equal(new DateOnly(2026, 8, 28), observation.EffectiveDate);
        Assert.Contains("SEKEURPMI", result.RawEvidence, StringComparison.Ordinal);
        Assert.StartsWith("riksbank-swea:20260828:", result.ImportIdentity, StringComparison.Ordinal);
        Assert.Equal("test-subscription-key", handler.LastRequest!.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
        Assert.Equal("correlation-1", handler.LastRequest.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("/swea/v1/Observations/Latest/ByGroup/130", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Adapter_translates_provider_failures_without_exposing_response_or_credentials()
    {
        const string secret = "provider diagnostic containing subscription-secret";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(secret)
        });
        var provider = CreateProvider(handler, "subscription-secret");

        var error = await Assert.ThrowsAsync<ExchangeRateProviderException>(() => provider.FetchAsync(
            new ExchangeRateProviderRequest(Guid.NewGuid(), new DateOnly(2026, 8, 29), ["EUR"], "correlation-2"), default));

        Assert.Equal(ExchangeRateReasonCodes.ProviderFailure, error.ReasonCode);
        Assert.True(error.IsTransient);
        Assert.DoesNotContain(secret, error.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("subscription-secret", error.SafeMessage, StringComparison.Ordinal);
    }

    private static RiksbankExchangeRateProvider CreateProvider(HttpMessageHandler handler, string subscriptionKey)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.riksbank.test/swea/v1/") };
        return new RiksbankExchangeRateProvider(new FixedHttpClientFactory(client),
            Options.Create(new RiksbankExchangeRateOptions
            {
                Enabled = true,
                ApiBaseUrl = client.BaseAddress.ToString(),
                SubscriptionKey = subscriptionKey
            }), new FixedTimeProvider(new DateTime(2026, 8, 29, 9, 0, 0, DateTimeKind.Utc)));
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
