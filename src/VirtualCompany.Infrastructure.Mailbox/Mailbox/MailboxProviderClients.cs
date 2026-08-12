using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
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

    public OAuthProviderOptions ZohoEu { get; init; } = new()
    {
        AuthorizationEndpoint = "https://accounts.zoho.eu/oauth/v2/auth",
        TokenEndpoint = "https://accounts.zoho.eu/oauth/v2/token"
    };

    public List<StandardProfileOptions> StandardProfiles { get; init; } = [];

    public sealed class OAuthProviderOptions
    {
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public string AuthorizationEndpoint { get; init; } = string.Empty;
        public string TokenEndpoint { get; init; } = string.Empty;
        public string ProfileEndpoint { get; init; } = string.Empty;
        public string MessagesEndpoint { get; init; } = string.Empty;
    }

    public sealed class StandardProfileOptions
    {
        public string ProfileKey { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public string ImapHost { get; init; } = string.Empty;
        public int ImapPort { get; init; } = 993;
        public MailboxTlsMode ImapTlsMode { get; init; } = MailboxTlsMode.ImplicitTls;
        public string SmtpHost { get; init; } = string.Empty;
        public int SmtpPort { get; init; } = 465;
        public MailboxTlsMode SmtpTlsMode { get; init; } = MailboxTlsMode.ImplicitTls;
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
    private const int GmailMessageListPageSize = 100;
    private const int GmailMessageListMaxPagesPerFolder = 10;
    private const int GmailAttachmentSearchMaxPages = 3;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MailboxIntegrationOptions> _options;
    private readonly ILogger<GmailMailboxProviderClient> _logger;

    public GmailMailboxProviderClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<MailboxIntegrationOptions> options,
        ILogger<GmailMailboxProviderClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public MailboxProvider Provider => MailboxProvider.Gmail;
    public MailboxReplyThreadingMode ReplyThreadingMode => MailboxReplyThreadingMode.Native;

    // Gmail readonly supports message and attachment retrieval without send/modify/delete permissions.
    public IReadOnlyCollection<string> DefaultScopes { get; } =
    [
        "openid",
        "email",
        "profile",
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.compose",
        "https://www.googleapis.com/auth/gmail.send",
    ];

    public IReadOnlyCollection<string> ReadRequiredScopes { get; } =
    [
        "https://www.googleapis.com/auth/gmail.readonly"
    ];

    public IReadOnlyCollection<string> ReplyRequiredScopes { get; } =
    [
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.send"
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
            ["scope"] = string.Join(' ', request.RequestedScopes ?? DefaultScopes),
            ["state"] = request.State,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true"
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

    public async Task<MailboxCredentialRevocationResult> RevokeCredentialAsync(
        MailboxCredentialRevocationRequest request,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = request.Token
        });
        using var response = await _httpClientFactory.CreateClient(ClientName).PostAsync(
            "https://oauth2.googleapis.com/revoke",
            content,
            cancellationToken);
        return new MailboxCredentialRevocationResult(true, response.IsSuccessStatusCode);
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

    public async Task<MailboxAccountProfile> GetExternalAccountProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName)
            .SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var email = root.TryGetProperty("email", out var emailElement)
            ? emailElement.GetString()
            : null;
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : email;
        var id = root.TryGetProperty("sub", out var idElement) ? idElement.GetString() : email;
        return new MailboxAccountProfile(
            email ?? throw new InvalidOperationException("Google account profile did not include an email address."),
            name, id ?? email!);
    }
    public async Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken)

    {
        var result = new List<MailboxMessageSummary>();
        var seenMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var after = new DateTimeOffset(query.FromUtc).ToUnixTimeSeconds();
        var before = new DateTimeOffset(query.ToUtc).ToUnixTimeSeconds();
        foreach (var folder in query.Folders)
        {
            var folderResultCountBefore = result.Count;
            string? pageToken = null;
            for (var page = 0; page < GmailMessageListMaxPagesPerFolder; page++)
            {
                var queryString = new Dictionary<string, string?>
                {
                    ["labelIds"] = folder.ProviderFolderId,
                    ["q"] = $"after:{after} before:{before}",
                    ["maxResults"] = GmailMessageListPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };

                if (!string.IsNullOrWhiteSpace(pageToken))
                {
                    queryString["pageToken"] = pageToken;
                }

                var listUri = QueryHelpers.AddQueryString(Options.MessagesEndpoint, queryString);
                using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var pageMessageCount = 0;
                if (json.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                {
                    pageMessageCount = messages.GetArrayLength();
                    foreach (var message in messages.EnumerateArray())
                    {
                        if (!message.TryGetProperty("id", out var idElement))
                        {
                            continue;
                        }

                        var messageId = idElement.GetString();
                        if (string.IsNullOrWhiteSpace(messageId) || !seenMessageIds.Add(messageId))
                        {
                            continue;
                        }

                        result.Add(await FetchMessageSummaryAsync(accessToken, messageId, cancellationToken));
                    }
                }

                pageToken = json.RootElement.TryGetProperty("nextPageToken", out var nextPageToken) &&
                    nextPageToken.ValueKind == JsonValueKind.String
                        ? nextPageToken.GetString()
                        : null;
                _logger.LogInformation(
                    "Gmail mailbox list page fetched. Folder: {Folder}. DisplayName: {DisplayName}. FromUtc: {FromUtc}. ToUtc: {ToUtc}. Page: {Page}. PageMessages: {PageMessages}. HasNextPage: {HasNextPage}.",
                    folder.ProviderFolderId,
                    folder.DisplayName,
                    query.FromUtc,
                    query.ToUtc,
                    page + 1,
                    pageMessageCount,
                    !string.IsNullOrWhiteSpace(pageToken));
                if (string.IsNullOrWhiteSpace(pageToken))
                {
                    break;
                }
            }

            _logger.LogInformation(
                "Gmail mailbox folder list completed. Folder: {Folder}. DisplayName: {DisplayName}. FromUtc: {FromUtc}. ToUtc: {ToUtc}. MessagesAdded: {MessagesAdded}.",
                folder.ProviderFolderId,
                folder.DisplayName,
                query.FromUtc,
                query.ToUtc,
                result.Count - folderResultCountBefore);
        }

        await AppendAttachmentSearchResultsAsync(accessToken, query, after, before, result, seenMessageIds, cancellationToken);

        return result;
    }

    private async Task AppendAttachmentSearchResultsAsync(
        string accessToken,
        MailboxMessageQuery query,
        long after,
        long before,
        List<MailboxMessageSummary> result,
        HashSet<string> seenMessageIds,
        CancellationToken cancellationToken)
    {
        foreach (var attachmentQuery in new[] { "filename:pdf", "filename:docx", "filename:png", "filename:jpg", "filename:jpeg", "filename:webp" })
        {
            var resultCountBefore = result.Count;
            string? pageToken = null;
            for (var page = 0; page < GmailAttachmentSearchMaxPages; page++)
            {
                var queryString = new Dictionary<string, string?>
                {
                    ["q"] = $"in:inbox after:{after} before:{before} {attachmentQuery}",
                    ["maxResults"] = GmailMessageListPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };

                if (!string.IsNullOrWhiteSpace(pageToken))
                {
                    queryString["pageToken"] = pageToken;
                }

                var listUri = QueryHelpers.AddQueryString(Options.MessagesEndpoint, queryString);
                using var request = new HttpRequestMessage(HttpMethod.Get, listUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var pageMessageCount = 0;
                var addedFromPage = 0;
                if (json.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                {
                    pageMessageCount = messages.GetArrayLength();
                    foreach (var message in messages.EnumerateArray())
                    {
                        if (!message.TryGetProperty("id", out var idElement))
                        {
                            continue;
                        }

                        var messageId = idElement.GetString();
                        if (string.IsNullOrWhiteSpace(messageId) || !seenMessageIds.Add(messageId))
                        {
                            continue;
                        }

                        result.Add(await FetchMessageSummaryAsync(accessToken, messageId, cancellationToken));
                        addedFromPage++;
                    }
                }

                pageToken = json.RootElement.TryGetProperty("nextPageToken", out var nextPageToken) &&
                    nextPageToken.ValueKind == JsonValueKind.String
                        ? nextPageToken.GetString()
                        : null;
                _logger.LogInformation(
                    "Gmail mailbox attachment search page fetched. Query: {AttachmentQuery}. FromUtc: {FromUtc}. ToUtc: {ToUtc}. Page: {Page}. PageMessages: {PageMessages}. AddedMessages: {AddedMessages}. HasNextPage: {HasNextPage}.",
                    attachmentQuery,
                    query.FromUtc,
                    query.ToUtc,
                    page + 1,
                    pageMessageCount,
                    addedFromPage,
                    !string.IsNullOrWhiteSpace(pageToken));
                if (string.IsNullOrWhiteSpace(pageToken))
                {
                    break;
                }
            }

            _logger.LogInformation(
                "Gmail mailbox attachment search completed. Query: {AttachmentQuery}. FromUtc: {FromUtc}. ToUtc: {ToUtc}. MessagesAdded: {MessagesAdded}.",
                attachmentQuery,
                query.FromUtc,
                query.ToUtc,
                result.Count - resultCountBefore);
        }
    }

    public async Task<MailboxInboundMessage> GetMessageAsync(string accessToken, MailboxMessageFetchRequest request, CancellationToken cancellationToken) =>
        await FetchInboundMessageAsync(accessToken, request.MessageId, cancellationToken);

    public async Task<MailboxAttachmentContent?> GetAttachmentContentAsync(
        string accessToken,
        MailboxAttachmentFetchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AttachmentId))
        {
            return null;
        }

        var uri = $"{Options.MessagesEndpoint}/{Uri.EscapeDataString(request.MessageId)}/attachments/{Uri.EscapeDataString(request.AttachmentId)}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data))
        {
            return null;
        }

        return new MailboxAttachmentContent(
            request.AttachmentId,
            request.FileName,
            request.MimeType,
            DecodeBase64UrlBytes(data.GetString()));
    }

    public async Task<MailboxInboundThread> GetThreadAsync(string accessToken, MailboxThreadFetchRequest request, CancellationToken cancellationToken)
    {
        var uri = $"{Options.MessagesEndpoint.Replace("/messages", "/threads", StringComparison.Ordinal)}/{Uri.EscapeDataString(request.ThreadId)}?format=full";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var messages = json.RootElement.TryGetProperty("messages", out var messagesElement) && messagesElement.ValueKind == JsonValueKind.Array
            ? messagesElement.EnumerateArray().Select(ParseGmailInboundMessage).OrderBy(x => x.ReceivedUtc ?? DateTime.MinValue).ToArray()
            : [];
        return new MailboxInboundThread(request.ThreadId, messages);
    }

    public async Task<MailboxReplyExecutionResult> CreateDraftReplyAsync(string accessToken, MailboxReplyExecutionRequest request, CancellationToken cancellationToken)
    {
        var raw = BuildGmailRawReply(request);
        var payload = JsonSerializer.Serialize(new
        {
            message = new
            {
                raw,
                threadId = string.IsNullOrWhiteSpace(request.ProviderThreadId) ? null : request.ProviderThreadId
            }
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Options.MessagesEndpoint.Replace("/messages", "/drafts", StringComparison.Ordinal)}");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.TryAddWithoutValidation("X-Idempotency-Key", request.IdempotencyKey);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(httpRequest, cancellationToken);
        await MailboxProviderHttpResponse.EnsureProviderSuccessAsync(response, "gmail_create_draft_reply", cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var draftId = json.RootElement.GetProperty("id").GetString()!;
        var message = json.RootElement.TryGetProperty("message", out var messageElement) ? messageElement : default;
        var messageId = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("id", out var id) ? id.GetString() ?? draftId : draftId;
        var threadId = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("threadId", out var thread) ? thread.GetString() : request.ProviderThreadId;
        return new MailboxReplyExecutionResult(messageId, draftId, threadId, "draft_created");
    }

    public async Task<MailboxReplyExecutionResult> SendReplyAsync(string accessToken, MailboxReplyExecutionRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { raw = BuildGmailRawReply(request), threadId = request.ProviderThreadId });
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Options.MessagesEndpoint}/send");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.TryAddWithoutValidation("X-Idempotency-Key", request.IdempotencyKey);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(httpRequest, cancellationToken);
        await MailboxProviderHttpResponse.EnsureProviderSuccessAsync(response, "gmail_send_reply", cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return new MailboxReplyExecutionResult(json.RootElement.GetProperty("id").GetString()!, null, json.RootElement.TryGetProperty("threadId", out var thread) ? thread.GetString() : request.ProviderThreadId, "sent");
    }

    private async Task<MailboxMessageSummary> FetchMessageSummaryAsync(string accessToken, string messageId, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString($"{Options.MessagesEndpoint}/{Uri.EscapeDataString(messageId)}", "format", "full");
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

    private async Task<MailboxInboundMessage> FetchInboundMessageAsync(string accessToken, string messageId, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString($"{Options.MessagesEndpoint}/{Uri.EscapeDataString(messageId)}", "format", "full");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseGmailInboundMessage(json.RootElement);
    }

    private static MailboxInboundMessage ParseGmailInboundMessage(JsonElement root)
    {
        var from = MailboxEmailAddressParser.Parse(TryReadGmailHeader(root, "From"));
        var recipients = MailboxEmailAddressParser.ParseMany(TryReadGmailHeader(root, "To")).ToArray();
        return new MailboxInboundMessage(root.GetProperty("id").GetString()!, root.TryGetProperty("threadId", out var threadId) ? threadId.GetString() : null, TryReadGmailHeader(root, "Message-ID"), TryReadGmailHeader(root, "Subject"), DecodeGmailBody(root, "text/plain"), DecodeGmailBody(root, "text/html"), from, recipients, TryParseGmailReceivedUtc(root, TryReadGmailHeader(root, "Date")), ReadGmailHeaders(root));
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

    private static IReadOnlyDictionary<string, string> ReadGmailHeaders(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("headers", out var headers) ||
            headers.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var header in headers.EnumerateArray())
        {
            if (header.TryGetProperty("name", out var name) &&
                header.TryGetProperty("value", out var value) &&
                !string.IsNullOrWhiteSpace(name.GetString()))
            {
                result[name.GetString()!] = value.GetString() ?? string.Empty;
            }
        }

        return result;
    }

    private static string? DecodeGmailBody(JsonElement root, string mimeType)
    {
        if (!root.TryGetProperty("payload", out var payload))
        {
            return null;
        }

        return DecodeGmailBodyPart(payload, mimeType);
    }

    private static string? DecodeGmailBodyPart(JsonElement part, string mimeType)
    {
        if (part.TryGetProperty("mimeType", out var partMimeType) &&
            string.Equals(partMimeType.GetString(), mimeType, StringComparison.OrdinalIgnoreCase) &&
            part.TryGetProperty("body", out var body) &&
            body.TryGetProperty("data", out var data))
        {
            return DecodeBase64Url(data.GetString());
        }

        if (!part.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var child in parts.EnumerateArray())
        {
            var value = DecodeGmailBodyPart(child, mimeType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? DecodeBase64Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private static byte[] DecodeBase64UrlBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
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
                    size,
                    IsTextExtractable: IsSupportedTextAttachment(filename.GetString(), mimeType));
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

    private static bool IsSupportedTextAttachment(string? fileName, string? mimeType)
    {
        var name = fileName ?? string.Empty;
        var type = mimeType ?? string.Empty;
        return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("image/webp", StringComparison.OrdinalIgnoreCase);
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

    private static string BuildGmailRawReply(MailboxReplyExecutionRequest request)
    {
        var subject = request.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? request.Subject : $"Re: {request.Subject}";
        var headers = new List<string>
        {
            $"To: {FormatAddress(request.ToEmail, request.ToDisplayName)}",
            $"Subject: {subject}",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=utf-8"
        };

        if (!string.IsNullOrWhiteSpace(request.InternetMessageId))
        {
            headers.Add($"In-Reply-To: {request.InternetMessageId}");
            headers.Add($"References: {request.InternetMessageId}");
        }

        var mime = string.Join("\r\n", headers) + "\r\n\r\n" + request.BodyText;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string FormatAddress(string email, string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? email : $"\"{displayName.Replace("\"", string.Empty, StringComparison.Ordinal)}\" <{email}>";
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
    public MailboxReplyThreadingMode ReplyThreadingMode => MailboxReplyThreadingMode.Native;

    // Mail.Read reads message and attachment metadata; User.Read binds the signed-in mailbox; offline_access enables refresh tokens.
    public IReadOnlyCollection<string> DefaultScopes { get; } = ["offline_access", "User.Read", "Mail.Read", "Mail.ReadWrite", "Mail.Send"];
    public IReadOnlyCollection<string> ReplyRequiredScopes { get; } = ["Mail.Read", "Mail.Send"];
    public IReadOnlyCollection<string> ReadRequiredScopes { get; } = ["Mail.Read"];

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
            ["scope"] = string.Join(' ', request.RequestedScopes ?? DefaultScopes),
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
            ["scope"] = string.Join(' ', request.RequestedScopes ?? DefaultScopes)
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
            ["scope"] = string.Join(' ', request.RequestedScopes ?? DefaultScopes)
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

    public async Task<MailboxInboundMessage> GetMessageAsync(string accessToken, MailboxMessageFetchRequest request, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString($"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(request.MessageId)}", new Dictionary<string, string?>
        {
            ["$select"] = "id,conversationId,internetMessageId,subject,body,bodyPreview,from,toRecipients,receivedDateTime,internetMessageHeaders"
        });
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseMicrosoftInboundMessage(json.RootElement);
    }

    public async Task<MailboxInboundThread> GetThreadAsync(string accessToken, MailboxThreadFetchRequest request, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString("https://graph.microsoft.com/v1.0/me/messages", new Dictionary<string, string?>
        {
            ["$select"] = "id,conversationId,internetMessageId,subject,body,bodyPreview,from,toRecipients,receivedDateTime,internetMessageHeaders",
            ["$filter"] = $"conversationId eq '{request.ThreadId.Replace("'", "''", StringComparison.Ordinal)}'",
            ["$orderby"] = "receivedDateTime asc"
        });
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var messages = json.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(ParseMicrosoftInboundMessage).OrderBy(x => x.ReceivedUtc ?? DateTime.MinValue).ToArray()
            : [];
        return new MailboxInboundThread(request.ThreadId, messages);
    }

    public async Task<MailboxReplyExecutionResult> CreateDraftReplyAsync(string accessToken, MailboxReplyExecutionRequest request, CancellationToken cancellationToken)
    {
        using var createReply = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(request.OriginalMessageId)}/createReply");
        createReply.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        createReply.Headers.TryAddWithoutValidation("client-request-id", request.IdempotencyKey);
        createReply.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var createResponse = await _httpClientFactory.CreateClient(ClientName).SendAsync(createReply, cancellationToken);
        await MailboxProviderHttpResponse.EnsureProviderSuccessAsync(createResponse, "microsoft365_create_reply", cancellationToken);
        using var stream = await createResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var draftId = json.RootElement.GetProperty("id").GetString()!;
        var conversationId = json.RootElement.TryGetProperty("conversationId", out var conversation) ? conversation.GetString() : request.ProviderThreadId;

        var body = JsonSerializer.Serialize(new
        {
            body = new { contentType = "Text", content = request.BodyText },
            toRecipients = new[]
            {
                new { emailAddress = new { address = request.ToEmail, name = request.ToDisplayName } }
            }
        });

        using var update = new HttpRequestMessage(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(draftId)}");
        update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        update.Headers.TryAddWithoutValidation("client-request-id", request.IdempotencyKey);
        update.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var updateResponse = await _httpClientFactory.CreateClient(ClientName).SendAsync(update, cancellationToken);
        await MailboxProviderHttpResponse.EnsureProviderSuccessAsync(updateResponse, "microsoft365_update_reply_draft", cancellationToken);
        return new MailboxReplyExecutionResult(draftId, draftId, conversationId, "draft_created");
    }

    public async Task<MailboxReplyExecutionResult> SendReplyAsync(string accessToken, MailboxReplyExecutionRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            comment = request.BodyText,
            toRecipients = new[]
            {
                new { emailAddress = new { address = request.ToEmail, name = request.ToDisplayName } }
            }
        });

        using var send = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(request.OriginalMessageId)}/reply");
        send.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        send.Headers.TryAddWithoutValidation("client-request-id", request.IdempotencyKey);
        send.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClientFactory.CreateClient(ClientName).SendAsync(send, cancellationToken);
        await MailboxProviderHttpResponse.EnsureProviderSuccessAsync(response, "microsoft365_send_reply", cancellationToken);
        return new MailboxReplyExecutionResult(
            $"{request.OriginalMessageId}:reply:{request.IdempotencyKey}",
            null,
            request.ProviderThreadId,
            "sent");
    }

    private static MailboxInboundMessage ParseMicrosoftInboundMessage(JsonElement root)
    {
        var body = root.TryGetProperty("body", out var bodyElement) && bodyElement.TryGetProperty("content", out var content) ? content.GetString() : null;
        var contentType = root.TryGetProperty("body", out bodyElement) && bodyElement.TryGetProperty("contentType", out var type) ? type.GetString() : null;
        var sender = MicrosoftMailboxJsonReader.ReadAddress(root, "from");
        var recipients = MicrosoftMailboxJsonReader.ReadRecipients(root, "toRecipients").ToArray();
        var received = root.TryGetProperty("receivedDateTime", out var receivedElement) && DateTimeOffset.TryParse(receivedElement.GetString(), out var parsed) ? parsed.UtcDateTime : (DateTime?)null;
        return new MailboxInboundMessage(root.GetProperty("id").GetString()!, root.TryGetProperty("conversationId", out var conversationId) ? conversationId.GetString() : null, root.TryGetProperty("internetMessageId", out var internetMessageId) ? internetMessageId.GetString() : null, root.TryGetProperty("subject", out var subject) ? subject.GetString() : null, string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase) ? root.TryGetProperty("bodyPreview", out var preview) ? preview.GetString() : null : body, string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase) ? body : null, sender, recipients, received, MicrosoftMailboxJsonReader.ReadHeaders(root));
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

internal static class MailboxEmailAddressParser
{
    public static MailboxAddress Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new MailboxAddress(null, null);
        }

        var trimmed = value.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(trimmed, "^(?<name>.*?)\\s*<(?<email>[^>]+)>$");
        if (!match.Success)
        {
            return new MailboxAddress(trimmed.ToLowerInvariant(), null);
        }

        var display = match.Groups["name"].Value.Trim().Trim('"');
        var email = match.Groups["email"].Value.Trim().ToLowerInvariant();
        return new MailboxAddress(email, string.IsNullOrWhiteSpace(display) ? null : display);
    }

    public static IEnumerable<MailboxAddress> ParseMany(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Parse(part);
        }
    }
}

internal static class MailboxProviderHttpResponse
{
    public static async Task EnsureProviderSuccessAsync(HttpResponseMessage response, string code, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        var detail = string.IsNullOrWhiteSpace(body)
            ? $"Email provider returned {(int)response.StatusCode} ({response.ReasonPhrase})."
            : $"Email provider returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        throw new MailboxProviderExecutionException(code, detail, retryable);
    }
}

internal static class MicrosoftMailboxJsonReader
{
    public static IEnumerable<MailboxAddress> ReadRecipients(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var recipients) || recipients.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var recipient in recipients.EnumerateArray())
        {
            if (recipient.TryGetProperty("emailAddress", out var address))
            {
                yield return new MailboxAddress(
                    address.TryGetProperty("address", out var email) ? email.GetString()?.ToLowerInvariant() : null,
                    address.TryGetProperty("name", out var name) ? name.GetString() : null);
            }
        }
    }

    public static MailboxAddress ReadAddress(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var sender) ||
            !sender.TryGetProperty("emailAddress", out var address))
        {
            return new MailboxAddress(null, null);
        }

        return new MailboxAddress(
            address.TryGetProperty("address", out var email) ? email.GetString()?.ToLowerInvariant() : null,
            address.TryGetProperty("name", out var name) ? name.GetString() : null);
    }

    public static IReadOnlyDictionary<string, string> ReadHeaders(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("internetMessageHeaders", out var headers) || headers.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var header in headers.EnumerateArray())
        {
            if (header.TryGetProperty("name", out var name) &&
                header.TryGetProperty("value", out var value) &&
                !string.IsNullOrWhiteSpace(name.GetString()))
            {
                result[name.GetString()!] = value.GetString() ?? string.Empty;
            }
        }

        return result;
    }
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
