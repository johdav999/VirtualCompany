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
    IReadOnlyCollection<MailboxFolderSelection>? ConfiguredFolders = null);

public sealed record CompleteMailboxOAuthConnectionCommand(
    string State,
    string Code,
    Uri CallbackUri,
    MailboxProvider? ExpectedProvider = null);

public sealed record TriggerManualMailboxScanCommand(
    Guid CompanyId,
    Guid UserId,
    Guid? MailboxConnectionId = null);

public sealed record GetMailboxConnectionStatusQuery(
    Guid CompanyId,
    Guid UserId);

public sealed record GetMailboxScannedMessagesQuery(
    Guid CompanyId,
    Guid UserId,
    int Limit = 50);

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
    Uri? ReturnUri = null);

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
    EmailIngestionRunSummary? LastRun);

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
    Uri? ReturnUri = null);

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

public sealed record ManualInboxBillScanJob(
    Guid CompanyId,
    Guid UserId,
    Guid MailboxConnectionId,
    Guid EmailIngestionRunId,
    DateTime ScanFromUtc,
    DateTime ScanToUtc);

public interface IMailboxConnectionService
{
    Task<MailboxOAuthStartResult> StartOAuthConnectionAsync(StartMailboxOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<MailboxOAuthCompletionResult> CompleteOAuthConnectionAsync(CompleteMailboxOAuthConnectionCommand command, CancellationToken cancellationToken);
    Task<ManualMailboxScanResult> TriggerManualScanAsync(TriggerManualMailboxScanCommand command, CancellationToken cancellationToken);
    Task<MailboxConnectionStatusResult> GetStatusAsync(GetMailboxConnectionStatusQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailboxScannedMessageSummary>> GetScannedMessagesAsync(GetMailboxScannedMessagesQuery query, CancellationToken cancellationToken);
}

public interface IManualInboxBillScanJobScheduler
{
    Task EnqueueManualScanAsync(ManualInboxBillScanJob job, CancellationToken cancellationToken);
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
    Uri BuildAuthorizationUrl(MailboxAuthorizationRequest request);
    Task<MailboxOAuthTokenResult> ExchangeCodeAsync(MailboxTokenExchangeRequest request, CancellationToken cancellationToken);
    Task<MailboxOAuthTokenResult> RefreshTokenAsync(MailboxRefreshTokenRequest request, CancellationToken cancellationToken);
    Task<MailboxAccountProfile> GetAccountProfileAsync(string accessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailboxMessageSummary>> ListMessagesAsync(string accessToken, MailboxMessageQuery query, CancellationToken cancellationToken);
}

public sealed record MailboxAuthorizationRequest(
    Guid CompanyId,
    Guid UserId,
    Uri CallbackUri,
    string State);

public sealed record MailboxTokenExchangeRequest(
    string Code,
    Uri CallbackUri);

public sealed record MailboxRefreshTokenRequest(
    string RefreshToken);

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
