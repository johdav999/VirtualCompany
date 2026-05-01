using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxOAuthClientTests
{
    [Fact]
    public void Authorization_url_uses_configured_endpoint_and_required_oauth_parameters()
    {
        var client = CreateClient(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)));

        var uri = client.BuildAuthorizationUrl("state-handle", "nonce-value");
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal("https://apps.fortnox.se/oauth-v1/auth", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("https://localhost/api/fortnox/callback", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("state-handle", query["state"]);
        Assert.Equal("nonce-value", query["nonce"]);
        Assert.Equal("bookkeeping invoice", query["scope"]);
    }

    [Fact]
    public async Task Refresh_token_request_uses_basic_auth_form_body_and_maps_success_response()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "access_token": "new-access-token",
                  "refresh_token": "new-refresh-token",
                  "expires_in": 3600,
                  "scope": "bookkeeping invoice",
                  "tenant_id": "tenant-1"
                }
                """)
        });
        var client = CreateClient(handler);

        var result = await client.RefreshTokenAsync("old-refresh-token", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://apps.fortnox.se/oauth-v1/token", request.RequestUri!.ToString());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("client-id:client-secret")), request.Headers.Authorization.Parameter);
        Assert.Contains("grant_type=refresh_token", request.Body);
        Assert.Contains("refresh_token=old-refresh-token", request.Body);
        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal("new-refresh-token", result.RefreshToken);
        Assert.Equal(["bookkeeping", "invoice"], result.GrantedScopes);
        Assert.Equal("tenant-1", result.ProviderTenantId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true, "Reconnect Fortnox")]
    [InlineData(HttpStatusCode.Unauthorized, true, "Reconnect Fortnox")]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, "temporarily unavailable")]
    public async Task Refresh_token_failures_are_safe_and_classified(HttpStatusCode statusCode, bool requiresReconnect, string messageFragment)
    {
        var handler = new CapturingHandler(new HttpResponseMessage(statusCode)
        {
            Content = Json("""{"error":"invalid_grant","error_description":"raw provider detail"}""")
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<FortnoxOAuthException>(() =>
            client.RefreshTokenAsync("secret-refresh-token", CancellationToken.None));

        Assert.Equal(requiresReconnect, exception.RequiresReconnect);
        Assert.Contains(messageFragment, exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-refresh-token", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("raw provider detail", exception.Message, StringComparison.Ordinal);
    }

    private static FortnoxOAuthClient CreateClient(CapturingHandler handler) =>
        new(
            new StaticHttpClientFactory(new HttpClient(handler)),
            TimeProvider.System,
            new StaticOptionsMonitor<FortnoxOptions>(new FortnoxOptions
            {
                Enabled = true,
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RedirectUri = "https://localhost/api/fortnox/callback",
                AuthorizationUrl = "https://apps.fortnox.se/oauth-v1/auth",
                TokenUrl = "https://apps.fortnox.se/oauth-v1/token",
                ApiBaseUrl = "https://api.fortnox.se/3",
                Scopes = [" bookkeeping ", "invoice"]
            }));

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public CapturingHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri, request.Headers.Authorization, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, System.Net.Http.Headers.AuthenticationHeaderValue? HeadersAuthorization, string Body)
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue? HeadersAuthorizationValue => HeadersAuthorization;
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization => HeadersAuthorization;
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
