using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class TeamsCallControlApiClientTests
{
    [Fact]
    public async Task Join_uses_company_scoped_typed_route()
    {
        var company = Guid.NewGuid(); var session = Guid.NewGuid(); var handler = new Handler(session);
        var client = new TeamsCallControlApiClient(new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }), false);
        var result = await client.JoinAsync(company, session);
        Assert.Equal("requested", result!.State); Assert.Equal(company.ToString("D"), handler.CompanyId);
        Assert.Equal($"/api/sales/meeting-sessions/{session:D}/teams-call/join", handler.Path);
    }

    [Theory]
    [InlineData("start", "/audio/start")]
    [InlineData("stop", "/audio/stop")]
    [InlineData("revoke", "/consent/revoke")]
    public async Task Sensitive_media_controls_use_company_scoped_server_routes(string action, string suffix)
    {
        var company = Guid.NewGuid(); var session = Guid.NewGuid(); var handler = new Handler(session);
        var client = new TeamsCallControlApiClient(new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }), false);

        _ = action switch
        {
            "start" => await client.StartAudioAsync(company, session, 1),
            "stop" => await client.StopAudioAsync(company, session, 1),
            _ => await client.RevokeConsentAsync(company, session, 1)
        };

        Assert.Equal(company.ToString("D"), handler.CompanyId);
        Assert.Equal($"/api/sales/meeting-sessions/{session:D}/teams-call{suffix}", handler.Path);
    }

    private sealed class Handler(Guid session) : HttpMessageHandler
    {
        public string? Path { get; private set; } public string? CompanyId { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath; CompanyId = request.Headers.GetValues("X-Company-Id").Single();
            var value = new TeamsMeetingCallViewModel(Guid.NewGuid(), session, "requested", null, null, "join", 1, "host", null, null,
                DateTime.UtcNow, null, null, null, null, DateTime.UtcNow, 1, false);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") });
        }
    }
}
