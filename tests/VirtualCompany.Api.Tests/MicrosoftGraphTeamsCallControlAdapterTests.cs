using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MicrosoftGraphTeamsCallControlAdapterTests
{
    [Fact]
    public async Task Join_normalizes_graph_response_and_sends_exact_tenant_identity()
    {
        var tenant = Guid.NewGuid(); var organizer = Guid.NewGuid(); string? requestJson = null;
        var handler = new DelegateHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("https://graph.microsoft.com/v1.0/communications/calls", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            requestJson = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"call-123\",\"state\":\"establishing\"}", Encoding.UTF8, "application/json") };
        });
        var adapter = Create(handler);
        var join = $"https://teams.microsoft.com/l/meetup-join/{Uri.EscapeDataString("19:meeting_test@thread.v2")}/0?context={Uri.EscapeDataString($"{{\"Tid\":\"{tenant:D}\",\"Oid\":\"{organizer:D}\"}}")}";
        var result = await adapter.JoinAsync(new TeamsCallJoinContext(Guid.NewGuid(), Guid.NewGuid(), tenant,
            [TeamsPresenterApplicationPermissions.JoinGroupCall], join, new Uri("https://api.example.test/api/integrations/teams/calls?callId=1"), "key"), default);
        Assert.Equal(TeamsCallProviderOutcome.Succeeded, result.Outcome); Assert.Equal("call-123", result.ProviderCallId);
        Assert.Contains("\"@odata.type\"", requestJson, StringComparison.Ordinal);
        Assert.Contains(tenant.ToString("D"), requestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", requestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambiguous_create_is_not_reported_as_retryable_create()
    {
        var tenant = Guid.NewGuid(); var organizer = Guid.NewGuid();
        var adapter = Create(new DelegateHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var join = $"https://teams.microsoft.com/l/meetup-join/{Uri.EscapeDataString("19:meeting_test@thread.v2")}/0?context={Uri.EscapeDataString($"{{\"Tid\":\"{tenant:D}\",\"Oid\":\"{organizer:D}\"}}")}";
        var result = await adapter.JoinAsync(new TeamsCallJoinContext(Guid.NewGuid(), Guid.NewGuid(), tenant,
            [TeamsPresenterApplicationPermissions.JoinGroupCall], join, new Uri("https://api.example.test/callback"), "stable"), default);
        Assert.Equal(TeamsCallProviderOutcome.Ambiguous, result.Outcome);
    }

    private static MicrosoftGraphTeamsCallControlAdapter Create(HttpMessageHandler handler) => new(
        new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") }),
        new TokenProvider(), Options.Create(new TeamsPresenterOptions { BotCallingCallbackUrl = "https://api.example.test/callback" }));

    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class TokenProvider : ITeamsAppOnlyTokenProvider
    { public Task<TeamsAppOnlyAccessToken> AcquireAsync(TeamsAppOnlyTokenRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new TeamsAppOnlyAccessToken("token", DateTime.UtcNow.AddHours(1), request.EntraTenantId, request.RequiredPermissions.ToArray())); }
    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request); }
}
