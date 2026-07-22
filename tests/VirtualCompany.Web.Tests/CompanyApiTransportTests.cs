using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class CompanyApiTransportTests
{
    [Fact]
    public async Task SendAsync_rejects_missing_company_context_before_transport()
    {
        var handler = new RecordingHandler();
        var transport = new CompanyApiTransport(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        });

        await Assert.ThrowsAsync<ArgumentException>(() => transport.SendAsync(
            Guid.Empty,
            HttpMethod.Get,
            "internal/finance/bills",
            null,
            CancellationToken.None));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task SendAsync_preserves_route_and_adds_company_and_correlation_headers()
    {
        var companyId = Guid.NewGuid();
        var handler = new RecordingHandler();
        var transport = new CompanyApiTransport(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        });

        using var response = await transport.SendAsync(
            companyId,
            HttpMethod.Get,
            "internal/finance/bills?limit=25",
            null,
            CancellationToken.None);

        Assert.Equal("http://localhost/internal/finance/bills?limit=25", handler.Request!.RequestUri!.ToString());
        Assert.Equal(companyId.ToString(), handler.Request.Headers.GetValues("X-Company-Id").Single());
        Assert.True(Guid.TryParseExact(handler.Request.Headers.GetValues("X-Correlation-Id").Single(), "N", out _));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
