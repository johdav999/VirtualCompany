using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Application.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

// Preserve the original test identities while separating this mixed suite by dependency.
public sealed class SalesHumanRoomTests
{
    private static readonly Guid Room = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();
    [Fact]
    public async Task Host_client_reads_actual_API_contract_and_keeps_company_scope()
    {
        var participant = Guid.NewGuid();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(Company.ToString(), Assert.Single(request.Headers.GetValues("X-Company-Id")));
            Assert.False(request.Headers.Contains("X-Sales-Room-Session"));
            Assert.EndsWith($"/browser-rooms/{Room:D}", request.RequestUri!.AbsolutePath);
            return Ok(new SalesBrowserRoomView(Room, null, "lobby", "not_started", 2, DateTime.UtcNow.AddHours(1),
                [new(participant, "Host", "admitted", 1, false, "human-host-1", true)], []));
        })) { BaseAddress = new("https://api.example.test/") };
        var client = new SalesBrowserRoomApiClient(new CompanyApiTransport(http), false);
        var view = await client.StatusAsync(Company, Room, default);
        Assert.True(Assert.Single(view.Participants).IsOrganizer);
        await Assert.ThrowsAsync<ArgumentException>(() => client.StatusAsync(Guid.Empty, Room, default));
    }
    [Fact]
    public async Task Guest_client_sends_only_room_credential_and_reads_public_contract()
    {
        using var client = new SalesBrowserGuestApiClient(new HttpClient(new Handler(request =>
        {
            Assert.Equal("credential", Assert.Single(request.Headers.GetValues("X-Sales-Room-Session")));
            Assert.False(request.Headers.Contains("X-Company-Id")); Assert.Null(request.Headers.Authorization);
            return Ok(new SalesRoomGuestView(Room, Guid.NewGuid(), "lobby", "lobby", 1, DateTime.UtcNow.AddHours(1), false, false));
        })) { BaseAddress = new("https://api.example.test/") });
        var view = await client.StatusAsync(Room, "credential", default); Assert.Empty(view.Participants);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Host_and_guest_clients_read_backend_media_token_contract(bool host)
    {
        var expires = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.EndsWith($"/{Room:D}/media-token", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(host, request.Headers.Contains("X-Company-Id"));
            Assert.Equal(!host, request.Headers.Contains("X-Sales-Room-Session"));
            return Ok(new SalesRoomMediaToken("wss://media.example.test", "synthetic", "human-1", expires));
        })) { BaseAddress = new("https://api.example.test/") };
        BrowserRoomToken token;
        if (host)
        {
            var client = new SalesBrowserRoomApiClient(new CompanyApiTransport(http), false);
            token = await client.TokenAsync(Company, Room, default);
        }
        else
        {
            using var client = new SalesBrowserGuestApiClient(http);
            token = await client.TokenAsync(Room, "credential", default);
        }
        Assert.Equal("wss://media.example.test", token.Url);
        Assert.Equal("synthetic", token.Token);
        Assert.Equal("human-1", token.Identity);
        Assert.Equal(expires, token.ExpiresAt);
        Assert.DoesNotContain("synthetic", token.ToString());
    }

    private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
