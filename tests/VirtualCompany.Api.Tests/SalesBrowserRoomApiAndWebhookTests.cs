using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;
public sealed class SalesBrowserRoomApiAndWebhookTests
{
    [Fact]
    public void Webhook_requires_valid_signature_issuer_and_exact_body_checksum()
    {
        var options = new SalesRoomMediaOptions { ApiKey = "room-key", ApiSecret = new string('x', 32) };
        var verifier = new LiveKitSalesRoomWebhookVerifier(Options.Create(options), TimeProvider.System);
        var body = JsonSerializer.Serialize(new { id = "event-one", @event = "participant_joined", createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), room = new { name = "vc-test" }, participant = new { identity = "human-test" } });
        string Token(string issuer, string secret) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer: issuer,
            claims: new[] { new Claim("sha256", Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)))) },
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)));
        Assert.Equal("event-one", verifier.Verify(body, Token(options.ApiKey, options.ApiSecret)).EventId);
        Assert.Throws<SalesRoomAccessException>(() => verifier.Verify(body + " ", Token(options.ApiKey, options.ApiSecret)));
        Assert.Throws<SalesRoomAccessException>(() => verifier.Verify(body, Token("wrong-issuer", options.ApiSecret)));
        Assert.Throws<SalesRoomAccessException>(() => verifier.Verify(body, Token(options.ApiKey, new string('y', 32))));
        Assert.Throws<SalesRoomAccessException>(() => verifier.Verify(body, ""));
    }
    [Fact]
    public async Task Guest_capability_is_not_company_authentication_and_missing_guest_access_is_denied()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Sales-Room-Session", new string('x', 86));
        using var host = await client.GetAsync($"/api/sales/browser-rooms/{Guid.NewGuid()}"); Assert.Equal(HttpStatusCode.Unauthorized, host.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Company-Id");
        using var guest = await client.GetAsync($"/api/sales/browser-room-guests/{Guid.NewGuid()}"); Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
        Assert.True(guest.Headers.CacheControl?.NoStore); Assert.Equal("no-referrer", Assert.Single(guest.Headers.GetValues("Referrer-Policy")));
        using var webhook = await client.PostAsync("/api/sales/browser-room-provider/livekit", new StringContent("{}", Encoding.UTF8, "application/webhook+json"));
        Assert.Equal(HttpStatusCode.Unauthorized, webhook.StatusCode);
    }
}
