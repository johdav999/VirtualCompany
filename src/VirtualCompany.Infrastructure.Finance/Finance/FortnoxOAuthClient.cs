using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxOAuthClient
{
    public const string ClientName = "Fortnox";

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly IOptionsMonitor<FortnoxOptions> _options;
    private readonly IFinanceIntegrationRuntimeSettingsProvider? _runtimeSettings;

    public FortnoxOAuthClient(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        IOptionsMonitor<FortnoxOptions> options,
        IFinanceIntegrationRuntimeSettingsProvider? runtimeSettings = null)
    {
        _httpClient = httpClientFactory.CreateClient(ClientName);
        _timeProvider = timeProvider;
        _options = options;
        _runtimeSettings = runtimeSettings;
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
        return BuildAuthorizationUrl(
            options.ClientId,
            options.RedirectUri,
            FortnoxScopeDefaults.Resolve(options.Scopes),
            options.AccountType,
            options.AuthorizationUrl,
            state,
            nonce);
    }

    public async Task<Uri> BuildAuthorizationUrlAsync(
        string state,
        string nonce,
        CancellationToken cancellationToken)
    {
        var runtime = await ResolveRuntimeAsync(cancellationToken);
        var options = _options.CurrentValue;
        return BuildAuthorizationUrl(
            runtime.ClientId,
            runtime.RedirectUri,
            runtime.Scopes,
            options.AccountType,
            options.AuthorizationUrl,
            state,
            nonce);
    }

    private static Uri BuildAuthorizationUrl(
        string clientId,
        string redirectUri,
        IReadOnlyCollection<string> scopes,
        string accountType,
        string authorizationUrl,
        string state,
        string nonce)
    {
        var builder = new UriBuilder(authorizationUrl);
        var scope = string.Join(" ", scopes);
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["nonce"] = nonce,
            ["access_type"] = "offline"
        };

        if (!string.IsNullOrWhiteSpace(scope))
        {
            query["scope"] = scope;
        }

        if (!string.IsNullOrWhiteSpace(accountType))
        {
            query["account_type"] = accountType.Trim();
        }

        builder.Query = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
        return builder.Uri;
    }

    public async Task<FortnoxOAuthTokenResult> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var runtime = await ResolveRuntimeAsync(cancellationToken);
        return await SendTokenRequestAsync(
            runtime,
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = runtime.RedirectUri
            },
            cancellationToken);
    }

    public async Task<FortnoxOAuthTokenResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var runtime = await ResolveRuntimeAsync(cancellationToken);
        return await SendTokenRequestAsync(
            runtime,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            cancellationToken);
    }

    private async Task<FortnoxOAuthTokenResult> SendTokenRequestAsync(
        FinanceIntegrationRuntimeSettings runtime,
        Dictionary<string, string> body,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(body)
        };
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{runtime.ClientId}:{runtime.ClientSecret}"));
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

    private async Task<FinanceIntegrationRuntimeSettings> ResolveRuntimeAsync(
        CancellationToken cancellationToken)
    {
        if (_runtimeSettings is not null)
        {
            return await _runtimeSettings.GetRequiredAsync(
                FinanceIntegrationProviderKeys.Fortnox,
                cancellationToken);
        }

        var options = RequireEnabledOptions();
        return new FinanceIntegrationRuntimeSettings(
            FinanceIntegrationProviderKeys.Fortnox,
            options.Enabled,
            options.ClientId,
            options.ClientSecret,
            options.RedirectUri,
            FortnoxScopeDefaults.Resolve(options.Scopes));
    }
}

internal static class FortnoxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed class FortnoxTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}
