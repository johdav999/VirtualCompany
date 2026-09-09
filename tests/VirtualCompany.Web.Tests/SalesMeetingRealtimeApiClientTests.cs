using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingRealtimeApiClientTests
{
    [Fact]
    public async Task Start_uses_company_transport_and_returns_only_session_scoped_connection_data()
    {
        var companyId = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var voiceId = Guid.NewGuid();
        var handler = new Handler(sessionId, voiceId);
        var client = new SalesMeetingRealtimeApiClient(new CompanyApiTransport(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }), false);

        var result = await client.StartAsync(companyId, sessionId, new(Guid.NewGuid(), "offer-sdp"));

        Assert.Equal("answer-sdp", result!.AnswerSdp);
        Assert.Equal($"/api/sales/meeting-sessions/{sessionId:D}/voice/sessions", handler.Path);
        Assert.Equal(companyId.ToString("D"), handler.CompanyId);
        Assert.DoesNotContain("Secret", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offline_voice_failure_is_explicit_about_typed_fallback()
    {
        var client = new SalesMeetingRealtimeApiClient(new CompanyApiTransport(
            new HttpClient(new Handler(Guid.NewGuid(), Guid.NewGuid())) { BaseAddress = new Uri("http://localhost/") }), true);

        var exception = await Assert.ThrowsAsync<SalesMeetingRealtimeApiException>(() => client.GetStatusAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains("Typed meeting controls", exception.Message, StringComparison.Ordinal);
    }

    private sealed class Handler(Guid sessionId, Guid voiceId) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? CompanyId { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            CompanyId = request.Headers.GetValues("X-Company-Id").Single();
            var status = new SalesMeetingRealtimeStatusViewModel(voiceId, sessionId, true, true, true, true, true,
                "active", "Typed workflows remain available.", "openai", "gpt-realtime", "browser_webrtc",
                DateTime.UtcNow.AddMinutes(5), 0, 0, 0, 0, null, null, 2);
            var json = JsonSerializer.Serialize(new SalesMeetingRealtimeStartResultViewModel(status, "answer-sdp"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
