using System.Net;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Mailbox;

public sealed record MailboxEndpointSettings(string Host, int Port, MailboxTlsMode TlsMode);

public sealed record MailboxTransportSettings(
    MailboxEndpointSettings Imap,
    MailboxEndpointSettings Smtp,
    int ConnectionTimeoutSeconds = 30,
    int CommandTimeoutSeconds = 60,
    long MaxMessageBytes = 25 * 1024 * 1024,
    long MaxAttachmentBytes = 10 * 1024 * 1024,
    int MaxAttachments = 25);

public sealed record MailboxCredentialLease(
    MailboxAuthenticationType AuthenticationType,
    string Username,
    string Secret,
    DateTime? ExpiresUtc = null);

public sealed record MailboxTransportContext(
    Guid CompanyId,
    Guid ConnectionId,
    string EmailAddress,
    MailboxTransportSettings Settings,
    MailboxCredentialLease Credential);

public sealed record MailboxTransportFolder(
    string FolderId,
    string DisplayName,
    bool CanRead,
    bool CanAppend,
    bool IsInbox,
    bool IsDrafts,
    bool IsSent);

public sealed record MailboxTransportHealthResult(
    bool ImapSucceeded,
    bool SmtpSucceeded,
    string AuthenticatedEmailAddress,
    MailboxCapability Capabilities,
    IReadOnlyList<MailboxTransportFolder> Folders,
    string? SafeFailureCode = null,
    string? SafeFailureMessage = null);

public sealed record MailboxIncrementalQuery(
    string FolderId,
    long? ExpectedUidValidity,
    long LastProcessedUid,
    int Limit = 100,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record MailboxIncrementalPage(
    string FolderId,
    long UidValidity,
    long LastObservedUid,
    long? HighestModSequence,
    IReadOnlyList<MailboxMessageSummary> Messages,
    bool HasMore);

public sealed record MailboxOutboundMessage(
    string MessageId,
    string FromAddress,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string PlainTextBody,
    string? HtmlBody,
    string? InReplyTo,
    IReadOnlyList<string> References,
    IReadOnlyList<MailboxOutboundAttachment> Attachments);

public sealed record MailboxOutboundAttachment(string FileName, string MimeType, byte[] Content);

public enum MailboxSubmissionOutcome
{
    Accepted = 1,
    RetryableFailure = 2,
    PermanentFailure = 3,
    AuthenticationRequired = 4,
    Ambiguous = 5
}

public sealed record MailboxSubmissionResult(
    MailboxSubmissionOutcome Outcome,
    string MessageId,
    string? ProviderReference,
    string? SafeFailureCode,
    string? SafeFailureMessage);

public interface IMailboxTransport
{
    string TransportKey { get; }
    Task<MailboxTransportHealthResult> TestAsync(MailboxTransportContext context, CancellationToken cancellationToken);
    async Task<MailboxTransportHealthResult> TestIncomingAsync(MailboxTransportContext context, CancellationToken cancellationToken)
    {
        var result = await TestAsync(context, cancellationToken);
        return result with { SmtpSucceeded = false };
    }
    async Task<MailboxTransportHealthResult> TestSendingAsync(MailboxTransportContext context, CancellationToken cancellationToken)
    {
        var result = await TestAsync(context, cancellationToken);
        return result with { ImapSucceeded = false, Folders = [] };
    }
    Task<IReadOnlyList<MailboxTransportFolder>> ListFoldersAsync(MailboxTransportContext context, CancellationToken cancellationToken);
    Task<MailboxIncrementalPage> ReadIncrementalAsync(MailboxTransportContext context, MailboxIncrementalQuery query, CancellationToken cancellationToken);
    Task<MailboxInboundMessage> GetMessageAsync(MailboxTransportContext context, MailboxMessageFetchRequest request, CancellationToken cancellationToken);
    Task<MailboxAttachmentContent?> GetAttachmentAsync(MailboxTransportContext context, MailboxAttachmentFetchRequest request, CancellationToken cancellationToken);
    Task<MailboxSubmissionResult> CreateDraftAsync(MailboxTransportContext context, MailboxOutboundMessage message, CancellationToken cancellationToken);
    Task<MailboxSubmissionResult> SendAsync(MailboxTransportContext context, MailboxOutboundMessage message, CancellationToken cancellationToken);
    Task<string?> FindSentMessageAsync(
        MailboxTransportContext context,
        string messageId,
        CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}

public enum StandardMailboxTestTarget
{
    Both = 0,
    Incoming = 1,
    Sending = 2
}

public interface IMailboxAuthenticationStrategy
{
    MailboxAuthenticationType AuthenticationType { get; }
    Task<MailboxCredentialLease> ResolveAsync(MailboxAuthenticationContext context, CancellationToken cancellationToken);
}

public sealed record MailboxAuthenticationContext(
    Guid CompanyId,
    Guid ConnectionId,
    string Username,
    string? EncryptedAccessToken,
    string? EncryptedRefreshToken,
    string? EncryptedCredentialEnvelope,
    DateTime? AccessTokenExpiresUtc,
    string ProfileKey);

public sealed record MailboxOAuthProfile(
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string ClientRegistrationKey,
    IReadOnlyList<string> ReadScopes,
    IReadOnlyList<string> SendScopes,
    string SaslMechanism = "XOAUTH2");

public sealed record MailboxConnectionProfile(
    string ProfileKey,
    string DisplayName,
    string Region,
    MailboxEndpointSettings Imap,
    MailboxEndpointSettings Smtp,
    IReadOnlySet<MailboxAuthenticationType> AuthenticationTypes,
    MailboxCapability CapabilityDefaults,
    MailboxOAuthProfile? OAuth = null,
    bool AllowsEndpointOverride = false);

public interface IMailboxConnectionProfileRegistry
{
    IReadOnlyList<MailboxConnectionProfile> List();
    MailboxConnectionProfile Resolve(string profileKey);
}

public interface IMailboxEndpointPolicy
{
    Task<MailboxEndpointPolicyDecision> EvaluateAsync(MailboxEndpointSettings endpoint, CancellationToken cancellationToken);
}

public sealed record MailboxEndpointPolicyDecision(
    bool Allowed,
    string ReasonCode,
    string Explanation,
    IReadOnlyList<IPAddress> ResolvedAddresses)
{
    public static MailboxEndpointPolicyDecision Permit(IReadOnlyList<IPAddress> resolvedAddresses) =>
        new(true, "allowed", "The secure mail endpoint is allowed.", resolvedAddresses);

    public static MailboxEndpointPolicyDecision Deny(string reasonCode, string explanation) =>
        new(false, reasonCode, explanation, []);
}

public interface IMailboxTransportRegistry
{
    IMailboxTransport Resolve(string transportKey);
}

public sealed record StandardMailboxConnectionInput(
    string ProfileKey,
    string EmailAddress,
    string Username,
    MailboxAuthenticationType AuthenticationType,
    string? Credential,
    MailboxEndpointSettings? ImapOverride = null,
    MailboxEndpointSettings? SmtpOverride = null);

public sealed record TestStandardMailboxConnectionCommand(
    Guid CompanyId,
    Guid UserId,
    MailboxPurpose Purpose,
    StandardMailboxConnectionInput Connection,
    StandardMailboxTestTarget Target = StandardMailboxTestTarget.Both);

public sealed record SaveStandardMailboxConnectionCommand(
    Guid CompanyId,
    Guid UserId,
    MailboxPurpose Purpose,
    StandardMailboxConnectionInput Connection,
    IReadOnlyCollection<string>? SelectedFolderIds = null);

public sealed record StandardMailboxConnectionResult(
    Guid? ConnectionId,
    bool IncomingSucceeded,
    bool SendingSucceeded,
    string EmailAddress,
    MailboxCapability Capabilities,
    IReadOnlyList<MailboxTransportFolder> Folders,
    string? FailureCode,
    string? FailureMessage,
    DateTime CheckedUtc);

public interface IStandardMailboxConnectionService
{
    IReadOnlyList<MailboxConnectionProfile> ListProfiles();
    Task<StandardMailboxConnectionResult> TestAsync(TestStandardMailboxConnectionCommand command, CancellationToken cancellationToken);
    Task<StandardMailboxConnectionResult> SaveAsync(SaveStandardMailboxConnectionCommand command, CancellationToken cancellationToken);
}
