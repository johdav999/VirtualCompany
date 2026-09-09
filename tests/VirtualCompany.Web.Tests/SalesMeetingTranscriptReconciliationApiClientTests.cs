using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingTranscriptReconciliationApiClientTests
{
    [Fact]
    public async Task Status_uses_tenant_transport_and_preserves_safe_conflict_and_stale_state()
    {
        var companyId = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        var status = new SalesMeetingTranscriptReconciliationStatusViewModel(sessionId, 2, true, 0, 1, [], [],
            [new(Guid.NewGuid(), Guid.NewGuid(), "cue-1", "Reviewed statement", "Provider statement", "Reviewed speaker", "Provider speaker",
                "Speaker attribution differs from reviewed evidence.", DateTime.UtcNow)]);
        var handler = new Handler(JsonSerializer.Serialize(status));
        var client = new SalesMeetingTranscriptReconciliationApiClient(
            new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new("https://example.test/") }), false);

        var result = await client.GetAsync(companyId, sessionId);

        Assert.True(result!.HasStaleClosingArtifacts);
        Assert.Single(result.Conflicts);
        Assert.Equal(companyId.ToString("D"), handler.CompanyId);
        Assert.Equal($"/api/sales/meeting-sessions/{sessionId:D}/transcript-reconciliation", handler.Path);
    }

    [Fact]
    public async Task Offline_mode_is_explicit()
    {
        var client = new SalesMeetingTranscriptReconciliationApiClient(
            new CompanyApiTransport(new HttpClient(new Handler("{}")) { BaseAddress = new("https://example.test/") }), true);

        await Assert.ThrowsAsync<SalesMeetingTranscriptApiException>(() => client.GetAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string? CompanyId { get; private set; }
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CompanyId = request.Headers.GetValues("X-Company-Id").Single();
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
}
