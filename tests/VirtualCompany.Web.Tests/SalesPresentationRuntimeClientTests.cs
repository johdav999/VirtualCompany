using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesPresentationRuntimeClientTests
{
    [Fact]
    public async Task Disconnected_client_uses_company_scoped_http_fallback_for_commands()
    {
        var companyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var handler = new RecordingHandler(sessionId);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var client = new SalesPresentationRuntimeClient(new CompanyApiTransport(http), http, false);

        var result = await client.ExecuteAsync(
            companyId, sessionId, "presentation.next", new(Guid.NewGuid(), 1, 1));

        Assert.Equal("accepted", result.Disposition);
        Assert.Equal($"/api/sales/meeting-sessions/{sessionId:D}/presentation/commands/presentation.next", handler.Path);
        Assert.Equal(companyId.ToString(), handler.CompanyId);
    }

    [Fact]
    public async Task Offline_mode_is_explicit_and_does_not_emit_commands()
    {
        var handler = new RecordingHandler(Guid.NewGuid());
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        await using var client = new SalesPresentationRuntimeClient(new CompanyApiTransport(http), http, true);

        Assert.Equal(SalesPresentationConnectionState.Offline, client.State);
        await Assert.ThrowsAsync<SalesPresentationRuntimeClientException>(() => client.ExecuteAsync(
            Guid.NewGuid(), Guid.NewGuid(), "presentation.next", new(Guid.NewGuid(), 1, 1)));
        Assert.Null(handler.Path);
    }

    private sealed class RecordingHandler(Guid sessionId) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? CompanyId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            CompanyId = request.Headers.GetValues("X-Company-Id").Single();
            var stage = new SalesPresentationStageSnapshotViewModel(
                sessionId, "presenting", 1, 2, Guid.NewGuid(), 1, 1, 2,
                "Opening", "Welcome", null, 1600, 900);
            var json = JsonSerializer.Serialize(new SalesPresentationCommandResultViewModel(
                "accepted", null, new SalesPresentationAuthoritativeSnapshotViewModel(
                    stage, new SalesPresentationPrivateSnapshotViewModel(
                        stage, "manual", 0, null, "private", "Open", 60, "Next", []))));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
