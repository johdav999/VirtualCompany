using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxOAuthClient
{
    public const string ClientName = "Fortnox";

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<FortnoxOptions> _options;

    public FortnoxOAuthClient(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        IOptionsMonitor<FortnoxOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient(ClientName);
        _timeProvider = timeProvider;
        _options = options;
    }

    public Uri GetTokenEndpoint()
    {
        var options = RequireEnabledOptions();
        return new Uri(options.TokenUrl, UriKind.Absolute);
    }

    public Uri GetApiBaseAddress()
    {
        var options = RequireEnabledOptions();
        return new Uri(options.ApiBaseUrl, UriKind.Absolute);
    }

    public Uri BuildAuthorizationUrl(string state, string nonce)
    {
        var options = RequireEnabledOptions();
        var builder = new UriBuilder(options.AuthorizationUrl);
        var scope = string.Join(" ", options.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Select(scope => scope.Trim()));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["nonce"] = nonce
        };

        if (!string.IsNullOrWhiteSpace(scope))
        {
            query["scope"] = scope;
        }

        builder.Query = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
        return builder.Uri;
    }

    public Task<FortnoxOAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken) =>
        SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RequireEnabledOptions().RedirectUri
            },
            cancellationToken);

    public Task<FortnoxOAuthTokenResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken) =>
        SendTokenRequestAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            cancellationToken);

    private async Task<FortnoxOAuthTokenResult> SendTokenRequestAsync(
        Dictionary<string, string> body,
        CancellationToken cancellationToken)
    {
        var options = RequireEnabledOptions();
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(body)
        };
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateSafeTokenException(response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<FortnoxTokenResponse>(stream, FortnoxJson.Options, cancellationToken);
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            string.IsNullOrWhiteSpace(payload.RefreshToken))
        {
            throw new FortnoxOAuthException("Fortnox returned an invalid token response.");
        }

        var expiresUtc = payload.ExpiresIn > 0
            ? _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(payload.ExpiresIn)
            : (DateTime?)null;

        var scopes = string.IsNullOrWhiteSpace(payload.Scope)
            ? Array.Empty<string>()
            : payload.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new FortnoxOAuthTokenResult(payload.AccessToken, payload.RefreshToken, expiresUtc, scopes, payload.TenantId);
    }

    private static FortnoxOAuthException CreateSafeTokenException(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new FortnoxOAuthException("Fortnox authorization has expired or was revoked. Reconnect Fortnox to continue.", requiresReconnect: true)
            : new FortnoxOAuthException("Fortnox authorization is temporarily unavailable. Try again later.", isTransient: true);

    private FortnoxOptions RequireEnabledOptions()
    {
        var options = _options.CurrentValue;
        return options.Enabled
            ? options
            : throw new InvalidOperationException("Fortnox integration is disabled.");
    }
}

internal static class FortnoxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed class FortnoxTokenResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? Scope { get; set; }
    public string? TenantId { get; set; }
}
