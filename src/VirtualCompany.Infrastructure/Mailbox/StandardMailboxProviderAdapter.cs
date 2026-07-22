using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Security;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class StandardMailboxProviderClient : IMailboxProviderClient
{
    private readonly IMailboxTransportRegistry _transports;
    private readonly IMailboxConnectionProfileRegistry? _profiles;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOptionsMonitor<MailboxIntegrationOptions>? _options;
    private readonly VirtualCompanyDbContext? _dbContext;

    public StandardMailboxProviderClient(IMailboxTransportRegistry transports)
    {
        _transports = transports;
    }

    public StandardMailboxProviderClient(
        IMailboxTransportRegistry transports,
        IMailboxConnectionProfileRegistry profiles,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<MailboxIntegrationOptions> options,
        VirtualCompanyDbContext dbContext)
    {
        _transports = transports;
        _profiles = profiles;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _dbContext = dbContext;
    }

    public MailboxProvider Provider => MailboxProvider.StandardEmail;
    public IReadOnlyCollection<string> DefaultScopes => [];

    public Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request)
    {
        var (profile, registration) = ResolveOAuth(request.ProfileKey);
        var scopes = profile.OAuth!.ReadScopes.Concat(profile.OAuth.SendScopes).Distinct(StringComparer.Ordinal).ToArray();
        return new Uri(QueryHelpers.AddQueryString(profile.OAuth.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = registration.ClientId,
            ["redirect_uri"] = request.CallbackUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', scopes),
            ["state"] = request.State,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        }));
    }

    public Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken) =>
        ExchangeTokenAsync(request.ProfileKey, new Dictionary<string, string>
        {
            ["code"] = request.Code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = request.CallbackUri.ToString()
        }, cancellationToken);

    public Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken) =>
        ExchangeTokenAsync(request.ProfileKey, new Dictionary<string, string>
        {
            ["refresh_token"] = request.RefreshToken,
            ["grant_type"] = "refresh_token"
        }, cancellationToken);

    public Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        var context = StandardMailboxSessionCodec.Decode(accessToken);
        return Task.FromResult(new MailboxAccountProfile(context.EmailAddress, null, context.EmailAddress));
    }

    private async Task<MailboxOAuthTokenResult> ExchangeTokenAsync(
        string? profileKey,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var (profile, registration) = ResolveOAuth(profileKey);
        form["client_id"] = registration.ClientId;
        form["client_secret"] = registration.ClientSecret;
        using var request = new HttpRequestMessage(HttpMethod.Post, profile.OAuth!.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _httpClientFactory!.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The hosted email provider did not accept the OAuth authorization. Reconnect and try again.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("The hosted email provider did not return an access token.");
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 3600;
        var grantedScopes = root.TryGetProperty("scope", out var scope)
            ? (scope.GetString() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : profile.OAuth.ReadScopes.Concat(profile.OAuth.SendScopes).Distinct(StringComparer.Ordinal).ToArray();
        return new MailboxOAuthTokenResult(accessToken, refreshToken, DateTime.UtcNow.AddSeconds(expiresIn), grantedScopes);
    }

    private (MailboxConnectionProfile Profile, MailboxIntegrationOptions.OAuthProviderOptions Registration) ResolveOAuth(string? profileKey)
    {
        if (_profiles is null || _options is null || _httpClientFactory is null || string.IsNullOrWhiteSpace(profileKey))
        {
            throw new InvalidOperationException("Hosted mailbox OAuth is not configured.");
        }

        var profile = _profiles.Resolve(profileKey);
        if (profile.OAuth is null || !string.Equals(profile.ProfileKey, StandardMailboxConnectionProfileRegistry.ZohoEuProfileKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OAuth is not available for this hosted email profile.");
        }

        var registration = _options.CurrentValue.ZohoEu;
        if (string.IsNullOrWhiteSpace(registration.ClientId) || string.IsNullOrWhiteSpace(registration.ClientSecret))
        {
            throw new InvalidOperationException("Hosted mailbox OAuth client settings are not configured by an administrator.");
        }

        return (profile, registration);
    }

    public async Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(
        string accessToken,
        MailboxMessageQuery query,
        CancellationToken cancellationToken)
    {
        var context = StandardMailboxSessionCodec.Decode(accessToken);
        var transport = _transports.Resolve(MailKitMailboxTransport.Key);
        var folders = query.Folders.Count == 0
            ? (await transport.ListFoldersAsync(context, cancellationToken)).Where(folder => folder.IsInbox).Select(folder => folder.FolderId)
            : query.Folders.Select(folder => folder.ProviderFolderId);
        var messages = new List<MailboxMessageSummary>();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cursor = _dbContext is null
                ? null
                : await _dbContext.MailboxFolderSyncCursors.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.CompanyId == context.CompanyId &&
                        item.MailboxConnectionId == context.ConnectionId &&
                        item.FolderId == folder, cancellationToken);
            var page = await transport.ReadIncrementalAsync(
                context,
                new MailboxIncrementalQuery(
                    folder,
                    cursor?.Status == MailboxCursorStatus.ReconciliationRequired ? null : cursor?.UidValidity,
                    cursor?.Status == MailboxCursorStatus.ReconciliationRequired ? 0 : cursor?.LastProcessedUid ?? 0,
                    500,
                    query.FromUtc,
                    query.ToUtc),
                cancellationToken);
            if (cursor is not null &&
                cursor.Status == MailboxCursorStatus.Active &&
                cursor.UidValidity.HasValue &&
                cursor.UidValidity.Value != page.UidValidity)
            {
                var trackedCursor = await _dbContext!.MailboxFolderSyncCursors
                    .SingleAsync(item => item.CompanyId == context.CompanyId && item.Id == cursor.Id, cancellationToken);
                trackedCursor.Advance(page.UidValidity, 0, page.HighestModSequence, DateTime.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (cursor is not null &&
                cursor.Status == MailboxCursorStatus.ReconciliationRequired &&
                page.Messages.Count == 0)
            {
                var trackedCursor = await _dbContext!.MailboxFolderSyncCursors
                    .SingleAsync(item => item.CompanyId == context.CompanyId && item.Id == cursor.Id, cancellationToken);
                trackedCursor.ResetAfterReconciliation(page.UidValidity, DateTime.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            messages.AddRange(page.Messages
                .Select(message => message with
                {
                    ProviderMessageId = StandardMailboxMessageReference.WithUidValidity(message.ProviderMessageId, page.UidValidity),
                    BodyReference = string.IsNullOrWhiteSpace(message.BodyReference)
                        ? message.BodyReference
                        : StandardMailboxMessageReference.WithUidValidity(message.BodyReference, page.UidValidity)
                })
                .Where(message =>
                !message.ReceivedUtc.HasValue ||
                (message.ReceivedUtc.Value >= query.FromUtc && message.ReceivedUtc.Value <= query.ToUtc)));
        }

        return messages.OrderBy(message => message.ReceivedUtc).ToArray();
    }

    public Task<MailboxInboundMessage> GetMessageAsync(
        string accessToken,
        MailboxMessageFetchRequest request,
        CancellationToken cancellationToken) =>
        _transports.Resolve(MailKitMailboxTransport.Key).GetMessageAsync(
            StandardMailboxSessionCodec.Decode(accessToken),
            request,
            cancellationToken);

    public Task<MailboxAttachmentContent?> GetAttachmentContentAsync(
        string accessToken,
        MailboxAttachmentFetchRequest request,
        CancellationToken cancellationToken) =>
        _transports.Resolve(MailKitMailboxTransport.Key).GetAttachmentAsync(
            StandardMailboxSessionCodec.Decode(accessToken),
            request,
            cancellationToken);

    public async Task<MailboxInboundThread> GetThreadAsync(
        string accessToken,
        MailboxThreadFetchRequest request,
        CancellationToken cancellationToken)
    {
        var message = await GetMessageAsync(accessToken, new MailboxMessageFetchRequest(request.ThreadId), cancellationToken);
        return new MailboxInboundThread(request.ThreadId, [message]);
    }

    public Task<MailboxReplyExecutionResult> CreateDraftReplyAsync(
        string accessToken,
        MailboxReplyExecutionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteReplyAsync(accessToken, request, createDraft: true, cancellationToken);

    public Task<MailboxReplyExecutionResult> SendReplyAsync(
        string accessToken,
        MailboxReplyExecutionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteReplyAsync(accessToken, request, createDraft: false, cancellationToken);

    private async Task<MailboxReplyExecutionResult> ExecuteReplyAsync(
        string accessToken,
        MailboxReplyExecutionRequest request,
        bool createDraft,
        CancellationToken cancellationToken)
    {
        var context = StandardMailboxSessionCodec.Decode(accessToken);
        var transport = _transports.Resolve(MailKitMailboxTransport.Key);
        var messageId = BuildMessageId(request.IdempotencyKey, context.EmailAddress);
        var message = new MailboxOutboundMessage(
            messageId,
            context.EmailAddress,
            [request.ToEmail],
            [],
            [],
            request.Subject,
            request.BodyText,
            null,
            request.InternetMessageId,
            string.IsNullOrWhiteSpace(request.InternetMessageId) ? [] : [request.InternetMessageId],
            []);
        var result = createDraft
            ? await transport.CreateDraftAsync(context, message, cancellationToken)
            : await transport.SendAsync(context, message, cancellationToken);
        if (!createDraft && result.Outcome == MailboxSubmissionOutcome.Ambiguous)
        {
            try
            {
                var reconciledReference = await transport.FindSentMessageAsync(context, messageId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(reconciledReference))
                {
                    result = new MailboxSubmissionResult(
                        MailboxSubmissionOutcome.Accepted,
                        messageId,
                        reconciledReference,
                        null,
                        null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Preserve the ambiguous result. The outbox must not resend automatically.
            }
        }
        if (result.Outcome != MailboxSubmissionOutcome.Accepted)
        {
            throw new MailboxProviderExecutionException(
                result.SafeFailureCode ?? "mail_operation_failed",
                result.SafeFailureMessage ?? "The mail operation did not complete.",
                result.Outcome == MailboxSubmissionOutcome.RetryableFailure);
        }

        return new MailboxReplyExecutionResult(messageId, createDraft ? result.ProviderReference : null, request.ProviderThreadId, createDraft ? "draft" : "sent");
    }

    private static string BuildMessageId(string idempotencyKey, string emailAddress)
    {
        var domain = emailAddress.Split('@').LastOrDefault() ?? "virtualcompany.local";
        var stable = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey))).ToLowerInvariant();
        return $"<{stable}@{domain}>";
    }
}

public static class StandardMailboxMessageReference
{
    private const char Separator = '~';

    public static string WithUidValidity(string providerMessageId, long uidValidity) =>
        $"{WithoutUidValidity(providerMessageId)}{Separator}{uidValidity}";

    public static string WithoutUidValidity(string providerMessageId)
    {
        var separator = providerMessageId.LastIndexOf(Separator);
        return separator > 0 && long.TryParse(providerMessageId[(separator + 1)..], out _)
            ? providerMessageId[..separator]
            : providerMessageId;
    }

    public static bool TryRead(string providerMessageId, out long uidValidity, out long uid)
    {
        uidValidity = 0;
        uid = 0;
        var separator = providerMessageId.LastIndexOf(Separator);
        var dot = providerMessageId.LastIndexOf('.', separator > 0 ? separator : providerMessageId.Length - 1);
        return separator > dot && dot > 0 &&
            long.TryParse(providerMessageId[(separator + 1)..], out uidValidity) &&
            long.TryParse(providerMessageId[(dot + 1)..separator], out uid);
    }
}

public static class StandardMailboxSessionCodec
{
    public static string Create(MailboxConnection connection, IFieldEncryptionService fieldEncryption)
    {
        if (connection.Provider != MailboxProvider.StandardEmail ||
            connection.AuthenticationType is null ||
            string.IsNullOrWhiteSpace(connection.AuthenticatedUsername) ||
            string.IsNullOrWhiteSpace(connection.ImapHost) ||
            !connection.ImapPort.HasValue ||
            !connection.ImapTlsMode.HasValue ||
            string.IsNullOrWhiteSpace(connection.SmtpHost) ||
            !connection.SmtpPort.HasValue ||
            !connection.SmtpTlsMode.HasValue)
        {
            throw new InvalidOperationException("The hosted mailbox configuration is incomplete. Reconnect this mailbox.");
        }

        var authentication = connection.AuthenticationType.Value;
        var secret = authentication switch
        {
            MailboxAuthenticationType.ApplicationPassword when !string.IsNullOrWhiteSpace(connection.EncryptedCredentialEnvelope) =>
                fieldEncryption.Decrypt(connection.CompanyId, StandardMailboxCredentialPurposes.ApplicationPassword(connection.Id), connection.EncryptedCredentialEnvelope),
            MailboxAuthenticationType.OAuth2 when !string.IsNullOrWhiteSpace(connection.EncryptedAccessToken) =>
                fieldEncryption.Decrypt(connection.CompanyId, StandardMailboxCredentialPurposes.AccessToken(connection.Id), connection.EncryptedAccessToken),
            _ => throw new InvalidOperationException("Reconnect this mailbox to restore its authentication.")
        };
        var context = new MailboxTransportContext(
            connection.CompanyId,
            connection.Id,
            connection.EmailAddress,
            new MailboxTransportSettings(
                new MailboxEndpointSettings(connection.ImapHost, connection.ImapPort.Value, connection.ImapTlsMode.Value),
                new MailboxEndpointSettings(connection.SmtpHost, connection.SmtpPort.Value, connection.SmtpTlsMode.Value)),
            new MailboxCredentialLease(authentication, connection.AuthenticatedUsername, secret, connection.AccessTokenExpiresUtc));
        var json = JsonSerializer.Serialize(context);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static MailboxTransportContext Decode(string session)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(session));
            return JsonSerializer.Deserialize<MailboxTransportContext>(json)
                ?? throw new InvalidOperationException("The hosted mailbox session is invalid.");
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new InvalidOperationException("The hosted mailbox session is invalid.", exception);
        }
    }
}
