using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class TodayWorkspaceApiClientTests
{
    [Fact]
    public async Task GetAsync_uses_typed_company_transport_with_lens_and_context_headers()
    {
        var companyId = Guid.NewGuid();
        var handler = new RecordingHandler(companyId);
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var client = new TodayWorkspaceApiClient(transport, false);

        var result = await client.GetAsync(companyId, "Sales");

        Assert.NotNull(result);
        Assert.Equal("sales", result!.ActiveLens);
        Assert.Equal($"http://localhost/api/companies/{companyId:D}/workspace/today?lens=sales", handler.Request!.RequestUri!.ToString());
        Assert.Equal(companyId.ToString(), handler.Request.Headers.GetValues("X-Company-Id").Single());
        Assert.True(Guid.TryParseExact(handler.Request.Headers.GetValues("X-Correlation-Id").Single(), "N", out _));
    }

    [Fact]
    public async Task GetAsync_rejects_empty_company_before_transport()
    {
        var handler = new RecordingHandler(Guid.NewGuid());
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        var client = new TodayWorkspaceApiClient(transport, false);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(Guid.Empty, "company"));
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetAsync_preserves_access_failures_for_an_honest_unauthorized_state(HttpStatusCode statusCode)
    {
        var companyId = Guid.NewGuid();
        var transport = new CompanyApiTransport(new HttpClient(new StatusHandler(statusCode)) { BaseAddress = new Uri("http://localhost/") });
        var client = new TodayWorkspaceApiClient(transport, false);

        var exception = await Assert.ThrowsAsync<TodayWorkspaceAccessException>(() => client.GetAsync(companyId));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Fact]
    public async Task RequestReviewAsync_posts_to_authoritative_operating_cycle_endpoint()
    {
        var companyId = Guid.NewGuid();
        var handler = new ReviewHandler();
        var client = new TodayWorkspaceApiClient(new CompanyApiTransport(new HttpClient(handler)
            { BaseAddress = new Uri("http://localhost/") }), false);

        var review = await client.RequestReviewAsync(companyId);

        Assert.Equal("queued", review.State);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"http://localhost/api/companies/{companyId:D}/operating/reviews/request",
            handler.Request.RequestUri!.ToString());
        Assert.Equal(companyId.ToString(), handler.Request.Headers.GetValues("X-Company-Id").Single());
    }

    private sealed class RecordingHandler(Guid companyId) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var json = $$"""
            {
              "companyId": "{{companyId}}",
              "header": { "companyName": "Example", "title": "Sales today", "subtitle": "Summary" },
              "activeLens": "sales",
              "availableLenses": [],
              "situationSummary": { "headline": "Clear", "summary": "No urgent items", "asOfUtc": "2026-09-02T08:00:00Z", "freshness": "fresh", "isDeterministicFallback": true },
              "priorities": [], "metrics": [], "finance": null, "sales": null, "support": null, "marketing": null,
              "decisions": [], "agentUpdates": [], "generatedAtUtc": "2026-09-02T08:00:00Z", "cacheTimestampUtc": null,
              "isPartial": false, "diagnostics": []
            }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class ReviewHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var json = $$"""
            { "canRequest": true, "unavailableReasonCode": null, "unavailableReason": null,
              "requestId": "{{Guid.NewGuid()}}", "operatingCycleId": null, "state": "queued",
              "statusMessage": "Company review queued.", "updatedUtc": "2026-09-02T08:00:00Z" }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
