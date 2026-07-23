using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Mailbox;

public static class MailboxConnectionDefaults
{
    public static readonly TimeSpan ManualScanWindow = TimeSpan.FromDays(30);

    public static string TokenPurpose(MailboxProvider provider, string tokenKind) =>
        $"mailbox:{provider.ToStorageValue()}:{tokenKind}";

    public static IReadOnlyCollection<MailboxFolderSelection> NormalizeFolders(
        IReadOnlyCollection<MailboxFolderSelection>? folders,
        MailboxProvider provider)
    {
        var normalized = folders?
            .Select(folder => folder.Normalize())
            .Where(folder => !string.IsNullOrWhiteSpace(folder.ProviderFolderId))
            .ToArray();
        return normalized is { Length: > 0 }
            ? normalized
            : [new MailboxFolderSelection(provider == MailboxProvider.Gmail ? "INBOX" : "inbox", "Inbox")];
    }
}

public static class StandardMailboxCredentialPurposes
{
    public static string ApplicationPassword(Guid connectionId) => Build(connectionId, "application_password");

    public static string AccessToken(Guid connectionId) => Build(connectionId, "access_token");

    public static string RefreshToken(Guid connectionId) => Build(connectionId, "refresh_token");

    private static string Build(Guid connectionId, string kind)
    {
        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException("ConnectionId is required.", nameof(connectionId));
        }

        return $"mailbox:standard_email:{connectionId:D}:{kind}";
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
