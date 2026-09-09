using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingCaptureApiClientTests
{
    [Fact]
    public async Task Autosave_uses_company_transport_and_preserves_authoritative_version()
    {
        var companyId = Guid.NewGuid(); var sessionId = Guid.NewGuid();
        var handler = new Handler(sessionId); var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new SalesMeetingCaptureApiClient(new CompanyApiTransport(http), false);

        var result = await client.AutosaveAsync(companyId, sessionId,
            new(Guid.NewGuid(), 3, Observations: [new(Guid.NewGuid(), 1, "pain_point", "Slow close")]), CancellationToken.None);

        Assert.Equal("accepted", result!.Disposition); Assert.Equal(4, result.Snapshot.CaptureVersion);
        Assert.Equal($"/api/sales/meeting-sessions/{sessionId:D}/capture/autosave", handler.Path);
        Assert.Equal(companyId.ToString("D"), handler.CompanyId);
    }

    [Fact]
    public async Task Offline_mode_is_explicit_and_stage_contract_has_no_private_evidence()
    {
        var http = new HttpClient(new Handler(Guid.NewGuid())) { BaseAddress = new Uri("http://localhost/") };
        var client = new SalesMeetingCaptureApiClient(new CompanyApiTransport(http), true);
        await Assert.ThrowsAsync<SalesMeetingCaptureApiException>(() => client.ListQuestionsAsync(Guid.NewGuid(), Guid.NewGuid()));
        var stage = new SalesMeetingStageAnswerViewModel(Guid.NewGuid(), 1, "Question", "Approved answer", DateTime.UtcNow);
        var json = JsonSerializer.Serialize(stage);
        Assert.DoesNotContain("Evidence", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Confidence", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Handler(Guid sessionId) : HttpMessageHandler
    {
        public string? Path { get; private set; } public string? CompanyId { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath; CompanyId = request.Headers.GetValues("X-Company-Id").Single();
            var snapshot = new SalesMeetingCaptureSnapshotViewModel(sessionId, 4, Guid.NewGuid(), DateTime.UtcNow, [], [], [], []);
            var json = JsonSerializer.Serialize(new SalesMeetingCaptureSaveResultViewModel("accepted", snapshot));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
