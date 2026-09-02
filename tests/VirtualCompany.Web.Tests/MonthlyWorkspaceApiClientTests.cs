using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class MonthlyWorkspaceApiClientTests
{
    [Fact]
    public async Task GetAsync_preserves_company_lens_and_explicit_month()
    {
        var company = Guid.NewGuid();
        var handler = new Handler(company);
        var client = new MonthlyWorkspaceApiClient(new CompanyApiTransport(new HttpClient(handler)
            { BaseAddress = new Uri("http://localhost/") }), false);

        var result = await client.GetAsync(company, "Sales", 2026, 8);

        Assert.NotNull(result);
        Assert.Equal("sales", result!.ActiveLens);
        Assert.Equal($"http://localhost/api/companies/{company:D}/workspace/monthly?lens=sales&year=2026&month=8", handler.Request!.RequestUri!.ToString());
        Assert.Equal(company.ToString(), handler.Request.Headers.GetValues("X-Company-Id").Single());
    }

    [Fact]
    public async Task GetAsync_rejects_incomplete_or_invalid_period_before_transport()
    {
        var handler = new Handler(Guid.NewGuid());
        var client = new MonthlyWorkspaceApiClient(new CompanyApiTransport(new HttpClient(handler)
            { BaseAddress = new Uri("http://localhost/") }), false);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(Guid.NewGuid(), year: 2026));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetAsync(Guid.NewGuid(), year: 2026, month: 13));
        Assert.Null(handler.Request);
    }

    private sealed class Handler(Guid company) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var json = $$"""
            {"companyId":"{{company}}","header":{"companyName":"Example","title":"Sales monthly review","subtitle":"Summary"},
            "activeLens":"sales","availableLenses":[],"period":{"year":2026,"month":8,"timezone":"UTC","startUtc":"2026-08-01T00:00:00Z","endUtc":"2026-09-01T00:00:00Z","comparisonStartUtc":"2026-07-01T00:00:00Z","comparisonEndUtc":"2026-08-01T00:00:00Z","label":"August 1–31, 2026","comparisonLabel":"July 2026"},
            "managementSummary":{"headline":"Clear","summary":"Summary","coverageSummary":"1 of 1 sources current","isDeterministicFallback":true},
            "results":[],"priorities":[],"sections":[],"decisions":[],"agentOutcomes":[],"sourceCoverage":[],
            "generatedAtUtc":"2026-09-02T08:00:00Z","cacheTimestampUtc":null,"isPartial":false,"diagnostics":[]}
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
