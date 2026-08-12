using System.Security.Cryptography;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Mailbox;

public sealed record StartMailboxOAuthConnectionCommand(
    Guid CompanyId,
    Guid UserId,
    MailboxProvider Provider,
    Uri CallbackUri,
    Uri? ReturnUri = null,
    IReadOnlyCollection<MailboxFolderSelection>? ConfiguredFolders = null,
    MailboxPurpose Purpose = MailboxPurpose.Finance,
    string? ProfileKey = null,
    string? EmailAddress = null,
    string? Username = null,
    MailboxEndpointSettings? Imap = null,
    MailboxEndpointSettings? Smtp = null);

public sealed record CompleteMailboxOAuthConnectionCommand(
    string State,
    string Code,
    Uri CallbackUri,
    MailboxProvider? ExpectedProvider = null);

public sealed record TriggerManualMailboxScanCommand(
    Guid CompanyId,
    Guid UserId,
    Guid? MailboxConnectionId = null,
    MailboxPurpose Purpose = MailboxPurpose.Finance);

public sealed record GetMailboxConnectionStatusQuery(
    Guid CompanyId,
    Guid UserId,
    MailboxPurpose Purpose = MailboxPurpose.Finance);

public sealed record GetMailboxScannedMessagesQuery(
    Guid CompanyId,
    Guid UserId,
    int Limit = 50,
    MailboxPurpose Purpose = MailboxPurpose.Finance);

public sealed record DisconnectMailboxConnectionCommand(
    Guid CompanyId,
    Guid UserId,
    MailboxPurpose Purpose);

public sealed record MailboxOAuthStartResult(
    MailboxProvider Provider,
    Uri AuthorizationUrl);

public sealed record MailboxOAuthCompletionResult(
    Guid MailboxConnectionId,
    Guid CompanyId,
    Guid UserId,
    MailboxProvider Provider,
    string EmailAddress,
    string Status,
    Uri? ReturnUri = null,
    MailboxPurpose Purpose = MailboxPurpose.Finance);

public sealed record ManualMailboxScanResult(
    Guid IngestionRunId,
    Guid MailboxConnectionId,
    DateTime ScanFromUtc,
    DateTime ScanToUtc,
    int ScannedMessageCount,
    int DetectedCandidateCount,
    int NonCandidateMessageCount,
    int CandidateAttachmentSnapshotCount,
    int DeduplicatedAttachmentCount,
    string? FailureDetails,
    string Status = "completed");

public sealed record MailboxConnectionStatusResult(
    bool IsConnected,
    Guid? MailboxConnectionId,
    string? Provider,
    string? ConnectionStatus,
    string? EmailAddress,
    string? DisplayName,
    DateTime? ConnectedAtUtc,
    DateTime? LastSuccessfulScanAtUtc,
    string? LastErrorSummary,
    IReadOnlyCollection<MailboxFolderSelectionSummary> ConfiguredFolders,
    EmailIngestionRunSummary? LastRun,
    string Purpose = "finance");

public sealed record MailboxFolderSelectionSummary(
    string ProviderFolderId,
    string? DisplayName);

public sealed record EmailIngestionRunSummary(
    Guid Id,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string Provider,
    DateTime? ScanFromUtc,
    DateTime? ScanToUtc,
    int ScannedMessageCount,
    int DetectedCandidateCount,
    int NonCandidateMessageCount,
    int CandidateAttachmentSnapshotCount,
    int DeduplicatedAttachmentCount,
    string? FailureDetails);

public sealed record MailboxScannedMessageSummary(
    Guid Id,
    Guid EmailIngestionRunId,
    string ExternalMessageId,
    string? FromAddress,
    string? FromDisplayName,
    string? Subject,
    DateTime? ReceivedUtc,
    string? FolderId,
    string? FolderDisplayName,
    string SourceType,
    string CandidateDecision,
    IReadOnlyCollection<string> MatchedRules,
    string ReasonSummary,
    string? BodyPreview,
    IReadOnlyCollection<MailboxScannedAttachmentSummary> Attachments,
    Guid? DetectedBillId,
    Guid? DetectedSubscriptionProposalId,
    string? DetectedSubscriptionProposalStatus,
    DateTime CreatedUtc);

public sealed record MailboxScannedAttachmentSummary(
    string? FileName,
    string? MimeType,
    long? SizeBytes,
    string SourceType,
    bool IsDuplicateByHash);

public sealed record MailboxOAuthState(
    Guid CompanyId,
    Guid UserId,
    MailboxProvider Provider,
    IReadOnlyCollection<MailboxFolderSelection> ConfiguredFolders,
    DateTime ExpiresUtc,
    Uri? ReturnUri = null,
    MailboxPurpose Purpose = MailboxPurpose.Finance,
    string? ProfileKey = null,
    string? EmailAddress = null,
    string? Username = null,
    MailboxEndpointSettings? Imap = null,
    MailboxEndpointSettings? Smtp = null,
    string? Nonce = null,
    IReadOnlyCollection<string>? RequestedScopes = null);

public sealed record MailboxOAuthTokenResult(
    string AccessToken,
    string? RefreshToken,
    DateTime? AccessTokenExpiresUtc,
    IReadOnlyCollection<string> GrantedScopes);

public sealed record MailboxAccountProfile(
    string EmailAddress,
    string? DisplayName,
    string ProviderAccountId);

public sealed record MailboxMessageQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyCollection<MailboxFolderSelection> Folders);

public sealed record MailboxAttachmentSummary(
    string ExternalAttachmentId,
    string? FileName,
    string? MimeType,
    long? SizeBytes,
    string? ContentHash = null,
    string? StorageReference = null,
    string? UntrustedExtractedText = null,
    bool? IsTextExtractable = null);

public sealed record MailboxMessageSummary(
    string ProviderMessageId,
    string? Subject,
    string? Snippet,
    string? BodyPreview,
    IReadOnlyCollection<string> AttachmentFileNames,
    string? FromAddress = null,
    string? FromDisplayName = null,
    DateTime? ReceivedUtc = null,
    string? FolderId = null,
    string? FolderDisplayName = null,
    string? BodyReference = null,
    IReadOnlyCollection<MailboxAttachmentSummary>? Attachments = null)
{
    public IReadOnlyCollection<MailboxAttachmentSummary> AttachmentSummaries =>
        Attachments ?? AttachmentFileNames
            .Select(name => new MailboxAttachmentSummary(name, name, null, null))
            .ToArray();
}

public sealed record MailboxAddress(
    string? Email,
    string? DisplayName);

public sealed record MailboxInboundMessage(
    string ProviderMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string? Subject,
    string? PlainTextBody,
    string? HtmlBody,
    MailboxAddress Sender,
    IReadOnlyCollection<MailboxAddress> Recipients,
    DateTime? ReceivedUtc,
    IReadOnlyDictionary<string, string> Headers);

public sealed record MailboxInboundThread(
    string ProviderThreadId,
    IReadOnlyList<MailboxInboundMessage> Messages);

public sealed record MailboxMessageFetchRequest(
    string MessageId);

public sealed record MailboxAttachmentFetchRequest(
    string MessageId,
    string AttachmentId,
    string? FileName = null,
    string? MimeType = null);

public sealed record MailboxAttachmentContent(
    string ExternalAttachmentId,
    string? FileName,
    string? MimeType,
    byte[] Content);

public sealed record MailboxThreadFetchRequest(
    string ThreadId);

public sealed record MailboxReplyExecutionRequest(
    Guid CompanyId,
    Guid MailboxConnectionId,
    string Provider,
    string OriginalMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string ToEmail,
    string? ToDisplayName,
    string Subject,
    string BodyText,
    string IdempotencyKey);

public sealed record MailboxReplyExecutionResult(
    string ProviderMessageId,
    string? ProviderDraftId,
    string? ProviderThreadId,
    string Status);

public sealed class MailboxProviderExecutionException : Exception
{
    public MailboxProviderExecutionException(string code, string message, bool isRetryable, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        IsRetryable = isRetryable;
    }

    public string Code { get; }
    public bool IsRetryable { get; }
}

public sealed record ManualInboxBillScanJob(
    Guid CompanyId,
    Guid UserId,
    Guid MailboxConnectionId,
    Guid EmailIngestionRunId,
    DateTime ScanFromUtc,
    DateTime ScanToUtc,
    Guid? AgentTaskId = null,
    Guid? AgentId = null,
    string TriggerSource = "manual");

public sealed record ConnectedMailboxInboxScanJob(
    Guid CompanyId,
    Guid UserId,
    Guid MailboxConnectionId,
    MailboxProvider Provider,
    MailboxPurpose Purpose = MailboxPurpose.Finance);

public interface IMailboxConnectionService
{
    Task<MailboxOAuthStartResult> StartOAuthConnectionAsync(StartMailboxOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<MailboxOAuthCompletionResult> CompleteOAuthConnectionAsync(CompleteMailboxOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<ManualMailboxScanResult> TriggerManualScanAsync(TriggerManualMailboxScanCommand command, CancellationToken cancellationToken);
    Task<MailboxConnectionStatusResult> GetStatusAsync(GetMailboxConnectionStatusQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailboxScannedMessageSummary>> GetScannedMessagesAsync(GetMailboxScannedMessagesQuery query, CancellationToken cancellationToken);
    Task<MailboxConnectionStatusResult> DisconnectAsync(DisconnectMailboxConnectionCommand command, CancellationToken cancellationToken);
}

public interface IManualInboxBillScanJobScheduler
{
    Task EnqueueManualScanAsync(ManualInboxBillScanJob job, CancellationToken cancellationToken);
}

public interface IConnectedMailboxInboxScanJobScheduler
{
    Task EnqueueConnectedMailboxScanAsync(ConnectedMailboxInboxScanJob job, CancellationToken cancellationToken);
}

public interface IConnectedMailboxInboxScanOrchestrator
{
    Task ExecuteConnectedMailboxScanAsync(ConnectedMailboxInboxScanJob job, CancellationToken cancellationToken);
}

public interface IManualInboxBillScanOrchestrator
{
    Task ExecuteManualScanAsync(ManualInboxBillScanJob job, CancellationToken cancellationToken);
}

public sealed record BillCandidateAttachment(
    string ExternalAttachmentId,
    string? FileName,
    string? MimeType,
    long? SizeBytes,
    string ContentHash,
    string? StorageReference,
    BillSourceType SourceType,
    string? UntrustedExtractedText);

public sealed record BillDetectionResult(
    bool IsCandidate,
    IReadOnlyCollection<BillDetectionRuleMatch> MatchedRules,
    IReadOnlyCollection<BillSourceType> DetectedSourceTypes,
    IReadOnlyCollection<BillCandidateAttachment> CandidateAttachments,
    string ReasonSummary);

public interface IBillDetectionService
{
    BillDetectionResult Detect(MailboxMessageSummary message);
}

public static class MailboxAttachmentHash
{
    public static string ComputeDeterministicHash(MailboxAttachmentSummary attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.ContentHash))
        {
            return attachment.ContentHash.Trim();
        }

        var seed = string.Join(
            "|",
            attachment.ExternalAttachmentId,
            attachment.FileName,
            attachment.MimeType,
            attachment.SizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            attachment.StorageReference,
            attachment.UntrustedExtractedText);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));
    }

    public static string ComputeBodyHash(MailboxMessageSummary message)
    {
        var seed = string.Join("|", message.ProviderMessageId, message.Subject, message.BodyPreview, message.Snippet);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));
    }
}

public interface IMailboxOAuthStateProtector
{
    string Protect(MailboxOAuthState state);
    MailboxOAuthState Unprotect(string protectedState);
}

public interface IMailboxProviderRegistry
{
    IMailboxProviderClient Resolve(MailboxProvider provider);
}

public interface IMailboxProviderClient
{
    MailboxProvider Provider { get; }
    IReadOnlyCollection<string> DefaultScopes { get; }
    IReadOnlyCollection<string> ReplyRequiredScopes => [];
    IReadOnlyCollection<string> ReadRequiredScopes => [];
    MailboxReplyThreadingMode ReplyThreadingMode => MailboxReplyThreadingMode.Unknown;
    Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request);
    Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken);
    Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken);
    Task<MailboxCredentialRevocationResult> RevokeCredentialAsync(
        MailboxCredentialRevocationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MailboxCredentialRevocationResult(false, false));
    Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken);
    Task<MailboxAccountProfile> GetExternalAccountProfileAsync(string accessToken, CancellationToken cancellationToken) =>
        GetAccountProfileAsync(accessToken, cancellationToken);
    Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken);
    Task<MailboxInboundMessage> GetMessageAsync(string accessToken, MailboxMessageFetchRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This mailbox provider does not support fetching individual messages.");
    Task<MailboxAttachmentContent?> GetAttachmentContentAsync(string accessToken, MailboxAttachmentFetchRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<MailboxAttachmentContent?>(null);
    Task<MailboxInboundThread> GetThreadAsync(string accessToken, MailboxThreadFetchRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This mailbox provider does not support fetching threads.");
    Task<MailboxReplyExecutionResult> CreateDraftReplyAsync(string accessToken, MailboxReplyExecutionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This mailbox provider does not support creating reply drafts.");
    Task<MailboxReplyExecutionResult> SendReplyAsync(string accessToken, MailboxReplyExecutionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This mailbox provider does not support sending replies.");
}

public sealed record MailboxAuthorizationRequest(
    Guid CompanyId,
    Guid UserId,
    Uri CallbackUri,
    string State,
    string? ProfileKey = null,
    IReadOnlyCollection<string>? RequestedScopes = null);

public sealed record MailboxTokenExchangeRequest(
    string Code,
    Uri CallbackUri,
    string? ProfileKey = null,
    IReadOnlyCollection<string>? RequestedScopes = null);

public interface IMailboxOAuthReplayGuard
{
    Task RegisterAsync(
        Guid companyId,
        Guid userId,
        MailboxPurpose purpose,
        MailboxProvider provider,
        string nonce,
        DateTime expiresUtc,
        CancellationToken cancellationToken);

    Task<bool> TryConsumeAsync(
        Guid companyId,
        Guid userId,
        MailboxPurpose purpose,
        MailboxProvider provider,
        string nonce,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

public sealed record MailboxCredentialRevocationRequest(string Token, string? ProfileKey = null);

public sealed record MailboxCredentialRevocationResult(bool Supported, bool Succeeded);

public sealed record MailboxRefreshTokenRequest(
    string RefreshToken,
    string? ProfileKey = null,
    IReadOnlyCollection<string>? RequestedScopes = null);

public static class MailboxBillKeywordFilter
{
    public static readonly IReadOnlyList<string> RequiredKeywords =
    [
        "invoice",
        "bill",
        "faktura",
        "payment due",
        "amount due",
        "OCR",
        "IBAN",
        "bankgiro",
        "plusgiro"
    ];

    public static bool IsBillCandidate(MailboxMessageSummary message) =>
        RequiredKeywords.Any(keyword => Contains(message.Subject, keyword) || Contains(message.Snippet, keyword) ||
            Contains(message.BodyPreview, keyword) || message.AttachmentFileNames.Any(name => Contains(name, keyword)));

    private static bool Contains(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
