using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class MailboxIntegrationOptions
{
    public const string SectionName = "MailboxIntegrations";

    public OAuthProviderOptions Gmail { get; init; } = new()
    {
        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint = "https://oauth2.googleapis.com/token",
        ProfileEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/profile",
        MessagesEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages"
    };

    public OAuthProviderOptions Microsoft365 { get; init; } = new()
    {
        AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        ProfileEndpoint = "https://graph.microsoft.com/v1.0/me",
        MessagesEndpoint = "https://graph.microsoft.com/v1.0/me/mailFolders/{folderId}/messages"
    };

    public sealed class OAuthProviderOptions
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public string AuthorizationEndpoint { get; init; } = string.Empty;
        public string TokenEndpoint { get; init; } = string.Empty;
        public string ProfileEndpoint { get; init; } = string.Empty;
        public string MessagesEndpoint { get; init; } = string.Empty;
    }
}

public sealed class MailboxProviderRegistry : IMailboxProviderRegistry
{
    private readonly IReadOnlyDictionary<MailboxProvider, IMailboxProviderClient> _providers;

    public MailboxProviderRegistry(IEnumerable<IMailboxProviderClient> providers)
    {
        _providers = providers.ToDictionary(x => x.Provider);
    }

    public IMailboxProviderClient Resolve(MailboxProvider provider) =>
        _providers.TryGetValue(provider, out var client)
            ? client
            : throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported mailbox provider.");
}

public sealed class GmailMailboxProviderClient : IMailboxProviderClient
{
    public const string ClientName = "gmail-mailbox";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MailboxIntegrationOptions> _options;

    public GmailMailboxProviderClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<MailboxIntegrationOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public MailboxProvider Provider => MailboxProvider.Gmail;

    // Gmail readonly supports message and attachment retrieval without send/modify/delete permissions.
    public IReadOnlyCollection<string> DefaultScopes { get; } =
    [
        "openid",
        "email",
        "profile",
        "https://www.googleapis.com/auth/gmail.readonly"
    ];

    public Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request)
    {
        EnsureConfigured();
        var options = Options;
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = request.CallbackUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', DefaultScopes),
            ["state"] = request.State,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        return new Uri(QueryHelpers.AddQueryString(options.AuthorizationEndpoint, query));
    }

    public async Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var options = Options;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["code"] = request.Code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = request.CallbackUri.ToString()
        };

        return await SendTokenRequestAsync(form, cancellationToken);
    }

    public async Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var options = Options;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["refresh_token"] = request.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        return await SendTokenRequestAsync(form, cancellationToken);
    }

    public async Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.ProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var email = root.TryGetProperty("emailAddress", out var emailElement) ? emailElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Gmail profile did not include an email address.");
        }

        var id = root.TryGetProperty("messagesTotal", out var messagesTotal) ? messagesTotal.GetRawText() : email;
        return new MailboxAccountProfile(email, email, id ?? email);
    }

    public async Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken)
    {
        var result = new List<MailboxMessageSummary>();
        var after = new DateTimeOffset(query.FromUtc).ToUnixTimeSeconds();
        var before = new DateTimeOffset(query.ToUtc).ToUnixTimeSeconds();
        foreach (var folder in query.Folders)
        {
            var listUri = QueryHelpers.AddQueryString(Options.MessagesEndpoint, new Dictionary<string, string?>
            {
                ["labelIds"] = folder.ProviderFolderId,
                ["q"] = $"after:{after} before:{before}"
            });

            using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var message in messages.EnumerateArray())
            {
                if (!message.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                result.Add(await FetchMessageSummaryAsync(accessToken, idElement.GetString()!, cancellationToken));
            }
        }

        return result;
    }

    private async Task<MailboxMessageSummary> FetchMessageSummaryAsync(string accessToken, string messageId, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString($"{Options.MessagesEndpoint}/{Uri.EscapeDataString(messageId)}", "format", "metadata");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var subject = TryReadGmailHeader(root, "Subject");
        var from = TryReadGmailHeader(root, "From");
        var date = TryReadGmailHeader(root, "Date");
        var snippet = root.TryGetProperty("snippet", out var snippetElement) ? snippetElement.GetString() : null;
        var receivedUtc = TryParseGmailReceivedUtc(root, date);
        var labels = ReadGmailLabelIds(root).ToArray();
        var attachments = ReadGmailAttachments(root).ToArray();
        var attachmentNames = attachments
            .Select(x => x.FileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();

        return new MailboxMessageSummary(
            messageId,
            subject,
            snippet,
            null,
            attachmentNames,
            from,
            null,
            receivedUtc,
            labels.Length == 0 ? null : string.Join(",", labels),
            labels.Length == 0 ? null : string.Join(", ", labels),
            null,
            attachments);
    }

    private async Task<MailboxOAuthTokenResult> SendTokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await _httpClientFactory.CreateClient(ClientName)
            .PostAsync(Options.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        await MailboxOAuthHttpResponse.EnsureOAuthSuccessAsync(response, "Gmail", cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseTokenResult(json.RootElement, DefaultScopes);
    }

    private void EnsureConfigured()
    {
        var options = Options;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException("Gmail mailbox OAuth client settings are not configured.");
        }
    }

    private MailboxIntegrationOptions.OAuthProviderOptions Options => _options.CurrentValue.Gmail;

    private static string? TryReadGmailHeader(JsonElement root, string name)
    {
        if (!root.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("headers", out var headers) ||
            headers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return headers.EnumerateArray()
            .FirstOrDefault(x => x.TryGetProperty("name", out var headerName) &&
                string.Equals(headerName.GetString(), name, StringComparison.OrdinalIgnoreCase))
            .TryGetProperty("value", out var value)
                ? value.GetString()
                : null;
    }

    private static DateTime? TryParseGmailReceivedUtc(JsonElement root, string? dateHeader)
    {
        if (root.TryGetProperty("internalDate", out var internalDate) &&
            long.TryParse(internalDate.GetString(), out var internalDateMilliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(internalDateMilliseconds).UtcDateTime;
        }

        return DateTimeOffset.TryParse(dateHeader, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static IEnumerable<string> ReadGmailLabelIds(JsonElement root)
    {
        if (!root.TryGetProperty("labelIds", out var labels) || labels.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var label in labels.EnumerateArray())
        {
            var value = label.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<MailboxAttachmentSummary> ReadGmailAttachments(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("parts", out var parts))
        {
            yield break;
        }

        foreach (var attachment in ReadGmailAttachmentsFromParts(parts))
        {
            yield return attachment;
        }
    }

    private static IEnumerable<MailboxAttachmentSummary> ReadGmailAttachmentsFromParts(JsonElement parts)
    {
        if (parts.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("filename", out var filename) && !string.IsNullOrWhiteSpace(filename.GetString()))
            {
                var attachmentId = part.TryGetProperty("body", out var body) &&
                    body.TryGetProperty("attachmentId", out var attachmentIdElement)
                        ? attachmentIdElement.GetString()
                        : null;
                var size = part.TryGetProperty("body", out body) &&
                    body.TryGetProperty("size", out var sizeElement) &&
                    sizeElement.TryGetInt64(out var parsedSize)
                        ? parsedSize
                        : (long?)null;
                var mimeType = part.TryGetProperty("mimeType", out var mimeTypeElement)
                    ? mimeTypeElement.GetString()
                    : null;

                yield return new MailboxAttachmentSummary(
                    string.IsNullOrWhiteSpace(attachmentId) ? filename.GetString()! : attachmentId,
                    filename.GetString(),
                    mimeType,
                    size);
            }

            if (part.TryGetProperty("parts", out var childParts))
            {
                foreach (var childAttachment in ReadGmailAttachmentsFromParts(childParts))
                {
                    yield return childAttachment;
                }
            }
        }
    }

    private static MailboxOAuthTokenResult ParseTokenResult(JsonElement root, IReadOnlyCollection<string> fallbackScopes)
    {
        var accessToken = root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("OAuth token response did not include an access token.");
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement) ? refreshElement.GetString() : null;
        var expiresUtc = root.TryGetProperty("expires_in", out var expiresElement)
            ? DateTime.UtcNow.AddSeconds(expiresElement.GetInt32())
            : (DateTime?)null;
        var scopes = root.TryGetProperty("scope", out var scopeElement) && !string.IsNullOrWhiteSpace(scopeElement.GetString())
            ? scopeElement.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : fallbackScopes;
        return new MailboxOAuthTokenResult(accessToken, refreshToken, expiresUtc, scopes);
    }
}

public sealed class Microsoft365MailboxProviderClient : IMailboxProviderClient
{
    public const string ClientName = "microsoft365-mailbox";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MailboxIntegrationOptions> _options;

    public Microsoft365MailboxProviderClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<MailboxIntegrationOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public MailboxProvider Provider => MailboxProvider.Microsoft365;

    // Mail.Read reads message and attachment metadata; User.Read binds the signed-in mailbox; offline_access enables refresh tokens.
    public IReadOnlyCollection<string> DefaultScopes { get; } = ["offline_access", "User.Read", "Mail.Read"];

    public Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request)
    {
        EnsureConfigured();
        var options = Options;
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = request.CallbackUri.ToString(),
            ["response_type"] = "code",
            ["response_mode"] = "query",
            ["scope"] = string.Join(' ', DefaultScopes),
            ["state"] = request.State
        };

        return new Uri(QueryHelpers.AddQueryString(options.AuthorizationEndpoint, query));
    }

    public async Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var options = Options;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["code"] = request.Code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = request.CallbackUri.ToString(),
            ["scope"] = string.Join(' ', DefaultScopes)
        };

        return await SendTokenRequestAsync(form, cancellationToken);
    }

    public async Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var options = Options;
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["refresh_token"] = request.RefreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = string.Join(' ', DefaultScopes)
        };

        return await SendTokenRequestAsync(form, cancellationToken);
    }

    public async Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.ProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var email = root.TryGetProperty("mail", out var mail) && !string.IsNullOrWhiteSpace(mail.GetString())
            ? mail.GetString()
            : root.GetProperty("userPrincipalName").GetString();
        var name = root.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : email;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : email;
        return new MailboxAccountProfile(email ?? throw new InvalidOperationException("Microsoft profile did not include an email address."), name, id ?? email!);
    }

    public async Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken)
    {
        var result = new List<MailboxMessageSummary>();
        foreach (var folder in query.Folders)
        {
            var endpoint = Options.MessagesEndpoint.Replace("{folderId}", Uri.EscapeDataString(folder.ProviderFolderId), StringComparison.Ordinal);
            var uri = QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
            {
                ["$select"] = "id,subject,bodyPreview,hasAttachments",
                ["$filter"] = $"receivedDateTime ge {query.FromUtc:O} and receivedDateTime le {query.ToUtc:O}"
            });
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("value", out var messages) || messages.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var message in messages.EnumerateArray())
            {
                var id = message.GetProperty("id").GetString()!;
                result.Add(new MailboxMessageSummary(
                    id,
                    message.TryGetProperty("subject", out var subject) ? subject.GetString() : null,
                    null,
                    message.TryGetProperty("bodyPreview", out var bodyPreview) ? bodyPreview.GetString() : null,
                    []));
            }
        }

        return result;
    }

    private async Task<MailboxOAuthTokenResult> SendTokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await _httpClientFactory.CreateClient(ClientName)
            .PostAsync(Options.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        await MailboxOAuthHttpResponse.EnsureOAuthSuccessAsync(response, "Microsoft 365", cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var accessToken = root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("OAuth token response did not include an access token.");
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement) ? refreshElement.GetString() : null;
        var expiresUtc = root.TryGetProperty("expires_in", out var expiresElement)
            ? DateTime.UtcNow.AddSeconds(expiresElement.GetInt32())
            : (DateTime?)null;
        var scopes = root.TryGetProperty("scope", out var scopeElement) && !string.IsNullOrWhiteSpace(scopeElement.GetString())
            ? scopeElement.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : DefaultScopes;
        return new MailboxOAuthTokenResult(accessToken, refreshToken, expiresUtc, scopes);
    }

    private void EnsureConfigured()
    {
        var options = Options;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException("Microsoft 365 mailbox OAuth client settings are not configured.");
        }
    }

    private MailboxIntegrationOptions.OAuthProviderOptions Options => _options.CurrentValue.Microsoft365;
}

internal static class MailboxOAuthHttpResponse
{
    public static async Task EnsureOAuthSuccessAsync(
        HttpResponseMessage response,
        string providerDisplayName,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var providerError = TryReadOAuthError(body);
        var detail = string.IsNullOrWhiteSpace(providerError)
            ? $"{providerDisplayName} OAuth token endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase})."
            : $"{providerDisplayName} OAuth token endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}): {providerError}";

        throw new InvalidOperationException(detail);
    }

    private static string? TryReadOAuthError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : null;
            var description = root.TryGetProperty("error_description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null;

            return string.Join(": ", new[] { error, description }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        catch (JsonException)
        {
            return body.Length > 500 ? string.Concat(body.AsSpan(0, 500), "...") : body;
        }
    }
}
