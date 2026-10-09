using System.Security.Cryptography;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Mailbox;
public sealed record MailboxFolderSelectionSummary(string ProviderFolderId, string? DisplayName)
{
    public string ProviderFolderId { get; set; } = ProviderFolderId;
    public string? DisplayName { get; set; } = DisplayName;

    public MailboxFolderSelectionSummary() : this(string.Empty, default !)
    {
    }
}

public sealed record EmailIngestionRunSummary(Guid Id, DateTime StartedUtc, DateTime? CompletedUtc, string Provider, DateTime? ScanFromUtc, DateTime? ScanToUtc, int ScannedMessageCount, int DetectedCandidateCount, int NonCandidateMessageCount, int CandidateAttachmentSnapshotCount, int DeduplicatedAttachmentCount, string? FailureDetails)
{
    public Guid Id { get; set; } = Id;
    public DateTime StartedUtc { get; set; } = StartedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public string Provider { get; set; } = Provider;
    public DateTime? ScanFromUtc { get; set; } = ScanFromUtc;
    public DateTime? ScanToUtc { get; set; } = ScanToUtc;
    public int ScannedMessageCount { get; set; } = ScannedMessageCount;
    public int DetectedCandidateCount { get; set; } = DetectedCandidateCount;
    public int NonCandidateMessageCount { get; set; } = NonCandidateMessageCount;
    public int CandidateAttachmentSnapshotCount { get; set; } = CandidateAttachmentSnapshotCount;
    public int DeduplicatedAttachmentCount { get; set; } = DeduplicatedAttachmentCount;
    public string? FailureDetails { get; set; } = FailureDetails;

    public EmailIngestionRunSummary() : this(default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record ManualMailboxScanResult(Guid IngestionRunId, Guid MailboxConnectionId, DateTime ScanFromUtc, DateTime ScanToUtc, int ScannedMessageCount, int DetectedCandidateCount, int NonCandidateMessageCount, int CandidateAttachmentSnapshotCount, int DeduplicatedAttachmentCount, string? FailureDetails, string Status = "completed")
{
    public Guid IngestionRunId { get; set; } = IngestionRunId;
    public Guid MailboxConnectionId { get; set; } = MailboxConnectionId;
    public DateTime ScanFromUtc { get; set; } = ScanFromUtc;
    public DateTime ScanToUtc { get; set; } = ScanToUtc;
    public int ScannedMessageCount { get; set; } = ScannedMessageCount;
    public int DetectedCandidateCount { get; set; } = DetectedCandidateCount;
    public int NonCandidateMessageCount { get; set; } = NonCandidateMessageCount;
    public int CandidateAttachmentSnapshotCount { get; set; } = CandidateAttachmentSnapshotCount;
    public int DeduplicatedAttachmentCount { get; set; } = DeduplicatedAttachmentCount;
    public string? FailureDetails { get; set; } = FailureDetails;
    public string Status { get; set; } = Status;

    public ManualMailboxScanResult() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record MailboxScannedAttachmentSummary(string? FileName, string? MimeType, long? SizeBytes, string SourceType, bool IsDuplicateByHash)
{
    public string? FileName { get; set; } = FileName;
    public string? MimeType { get; set; } = MimeType;
    public long? SizeBytes { get; set; } = SizeBytes;
    public string SourceType { get; set; } = SourceType;
    public bool IsDuplicateByHash { get; set; } = IsDuplicateByHash;

    public MailboxScannedAttachmentSummary() : this(default !, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record MailboxScannedMessageSummary(Guid Id, Guid EmailIngestionRunId, string ExternalMessageId, string? FromAddress, string? FromDisplayName, string? Subject, DateTime? ReceivedUtc, string? FolderId, string? FolderDisplayName, string SourceType, string CandidateDecision, IReadOnlyCollection<string> MatchedRules, string ReasonSummary, string? BodyPreview, IReadOnlyCollection<MailboxScannedAttachmentSummary> Attachments, Guid? DetectedBillId, Guid? DetectedSubscriptionProposalId, string? DetectedSubscriptionProposalStatus, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid EmailIngestionRunId { get; set; } = EmailIngestionRunId;
    public string ExternalMessageId { get; set; } = ExternalMessageId;
    public string? FromAddress { get; set; } = FromAddress;
    public string? FromDisplayName { get; set; } = FromDisplayName;
    public string? Subject { get; set; } = Subject;
    public DateTime? ReceivedUtc { get; set; } = ReceivedUtc;
    public string? FolderId { get; set; } = FolderId;
    public string? FolderDisplayName { get; set; } = FolderDisplayName;
    public string SourceType { get; set; } = SourceType;
    public string CandidateDecision { get; set; } = CandidateDecision;
    public IReadOnlyCollection<string> MatchedRules { get; set; } = MatchedRules;
    public string ReasonSummary { get; set; } = ReasonSummary;
    public string? BodyPreview { get; set; } = BodyPreview;
    public IReadOnlyCollection<MailboxScannedAttachmentSummary> Attachments { get; set; } = Attachments;
    public Guid? DetectedBillId { get; set; } = DetectedBillId;
    public Guid? DetectedSubscriptionProposalId { get; set; } = DetectedSubscriptionProposalId;
    public string? DetectedSubscriptionProposalStatus { get; set; } = DetectedSubscriptionProposalStatus;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public MailboxScannedMessageSummary() : this(default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, [], string.Empty, default !, [], default !, default !, default !, default !)
    {
    }
}

public sealed record MailboxConnectionStatusResult(bool IsConnected, Guid? MailboxConnectionId, string? Provider, string? ConnectionStatus, string? EmailAddress, string? DisplayName, DateTime? ConnectedAtUtc, DateTime? LastSuccessfulScanAtUtc, string? LastErrorSummary, IReadOnlyCollection<MailboxFolderSelectionSummary> ConfiguredFolders, EmailIngestionRunSummary? LastRun, string Purpose = "finance")
{
    public bool IsConnected { get; set; } = IsConnected;
    public Guid? MailboxConnectionId { get; set; } = MailboxConnectionId;
    public string? Provider { get; set; } = Provider;
    public string? ConnectionStatus { get; set; } = ConnectionStatus;
    public string? EmailAddress { get; set; } = EmailAddress;
    public string? DisplayName { get; set; } = DisplayName;
    public DateTime? ConnectedAtUtc { get; set; } = ConnectedAtUtc;
    public DateTime? LastSuccessfulScanAtUtc { get; set; } = LastSuccessfulScanAtUtc;
    public string? LastErrorSummary { get; set; } = LastErrorSummary;
    public IReadOnlyCollection<MailboxFolderSelectionSummary> ConfiguredFolders { get; set; } = ConfiguredFolders;
    public EmailIngestionRunSummary? LastRun { get; set; } = LastRun;
    public string Purpose { get; set; } = Purpose;

    public MailboxConnectionStatusResult() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, [], default !, string.Empty)
    {
    }
}
