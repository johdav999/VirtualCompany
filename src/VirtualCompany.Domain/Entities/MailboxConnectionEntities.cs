using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class MailboxConnection : ICompanyOwnedEntity
{
    private MailboxConnection()
    {
    }

    public MailboxConnection(
        Guid id,
        Guid companyId,
        Guid userId,
        MailboxProvider provider,
        string emailAddress,
        string? displayName = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        MailboxProviderValues.EnsureSupported(provider, nameof(provider));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        Provider = provider;
        Status = MailboxConnectionStatus.Pending;
        EmailAddress = NormalizeEmail(emailAddress);
        DisplayName = NormalizeOptional(displayName, nameof(displayName), 200);
        GrantedScopes = [];
        ConfiguredFolders = [];
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public MailboxProvider Provider { get; private set; }
    public MailboxConnectionStatus Status { get; private set; }
    public string EmailAddress { get; private set; } = null!;
    public string? DisplayName { get; private set; }
    public string? MailboxExternalId { get; private set; }
    public string? EncryptedAccessToken { get; private set; }
    public string? EncryptedRefreshToken { get; private set; }
    public string? EncryptedCredentialEnvelope { get; private set; }
    public DateTime? AccessTokenExpiresUtc { get; private set; }
    public List<string> GrantedScopes { get; private set; } = [];
    public List<MailboxFolderSelection> ConfiguredFolders { get; private set; } = [];
    public JsonObject ProviderMetadata { get; private set; } = [];
    public DateTime? LastSuccessfulScanUtc { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<EmailMessageSnapshot> MessageSnapshots { get; } = new List<EmailMessageSnapshot>();
    public User User { get; private set; } = null!;
    public ICollection<EmailIngestionRun> IngestionRuns { get; } = new List<EmailIngestionRun>();

    public void UpdateMailboxProfile(string emailAddress, string? displayName, string? mailboxExternalId = null)
    {
        EmailAddress = NormalizeEmail(emailAddress);
        DisplayName = NormalizeOptional(displayName, nameof(displayName), 200);
        MailboxExternalId = NormalizeOptional(mailboxExternalId, nameof(mailboxExternalId), 256);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void StoreEncryptedCredentials(
        string? encryptedAccessToken,
        string? encryptedRefreshToken,
        DateTime? accessTokenExpiresUtc,
        IReadOnlyCollection<string>? grantedScopes,
        string? encryptedCredentialEnvelope = null)
    {
        EncryptedAccessToken = NormalizeOptional(encryptedAccessToken, nameof(encryptedAccessToken), 4096);
        EncryptedRefreshToken = NormalizeOptional(encryptedRefreshToken, nameof(encryptedRefreshToken), 4096);
        EncryptedCredentialEnvelope = NormalizeOptional(encryptedCredentialEnvelope, nameof(encryptedCredentialEnvelope), 8192);
        AccessTokenExpiresUtc = accessTokenExpiresUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(accessTokenExpiresUtc.Value, nameof(accessTokenExpiresUtc))
            : null;
        GrantedScopes = NormalizeStringList(grantedScopes, nameof(grantedScopes), 256);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void ConfigureFolders(IReadOnlyCollection<MailboxFolderSelection>? configuredFolders)
    {
        ConfiguredFolders = configuredFolders?
            .Select(folder => folder.Normalize())
            .Where(folder => !string.IsNullOrWhiteSpace(folder.ProviderFolderId))
            .GroupBy(folder => folder.ProviderFolderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList() ?? [];
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(MailboxConnectionStatus status, string? errorSummary = null)
    {
        MailboxConnectionStatusValues.EnsureSupported(status, nameof(status));
        Status = status;
        LastErrorSummary = NormalizeOptional(errorSummary, nameof(errorSummary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkScanSucceeded(DateTime completedUtc)
    {
        LastSuccessfulScanUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        LastErrorSummary = null;
        UpdatedUtc = LastSuccessfulScanUtc.Value;
    }

    private static List<string> NormalizeStringList(IReadOnlyCollection<string>? values, string name, int maxLength) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeRequired(value, name, maxLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static string NormalizeEmail(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 256).ToLowerInvariant();
        if (!normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Mailbox email address must be valid.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class EmailIngestionRun : ICompanyOwnedEntity
{
    private EmailIngestionRun()
    {
    }

    public EmailIngestionRun(
        Guid id,
        Guid companyId,
        Guid mailboxConnectionId,
        Guid triggeredByUserId,
        MailboxProvider provider,
        DateTime startedUtc,
        DateTime? scanFromUtc = null,
        DateTime? scanToUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (mailboxConnectionId == Guid.Empty)
        {
            throw new ArgumentException("MailboxConnectionId is required.", nameof(mailboxConnectionId));
        }

        if (triggeredByUserId == Guid.Empty)
        {
            throw new ArgumentException("TriggeredByUserId is required.", nameof(triggeredByUserId));
        }

        MailboxProviderValues.EnsureSupported(provider, nameof(provider));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MailboxConnectionId = mailboxConnectionId;
        TriggeredByUserId = triggeredByUserId;
        Provider = provider;
        StartedUtc = EntityTimestampNormalizer.NormalizeUtc(startedUtc, nameof(startedUtc));
        ScanFromUtc = scanFromUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(scanFromUtc.Value, nameof(scanFromUtc)) : null;
        ScanToUtc = scanToUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(scanToUtc.Value, nameof(scanToUtc)) : null;
        CreatedUtc = StartedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MailboxConnectionId { get; private set; }
    public Guid TriggeredByUserId { get; private set; }
    public MailboxProvider Provider { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime? ScanFromUtc { get; private set; }
    public DateTime? ScanToUtc { get; private set; }
    public int ScannedMessageCount { get; private set; }
    public int DetectedCandidateCount { get; private set; }
    public int NonCandidateMessageCount { get; private set; }
    public int CandidateAttachmentSnapshotCount { get; private set; }
    public int DeduplicatedAttachmentCount { get; private set; }
    public string? FailureDetails { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public MailboxConnection MailboxConnection { get; private set; } = null!;
    public User TriggeredByUser { get; private set; } = null!;

    public void Complete(DateTime completedUtc, int scannedMessageCount, int detectedCandidateCount)
    {
        Complete(completedUtc, scannedMessageCount, detectedCandidateCount, scannedMessageCount - detectedCandidateCount, 0, 0);
    }

    public void Complete(
        DateTime completedUtc,
        int scannedMessageCount,
        int detectedCandidateCount,
        int nonCandidateMessageCount,
        int candidateAttachmentSnapshotCount,
        int deduplicatedAttachmentCount)
    {
        if (scannedMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scannedMessageCount), "Scanned message count cannot be negative.");
        }

        if (detectedCandidateCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detectedCandidateCount), "Detected candidate count cannot be negative.");
        }

        if (nonCandidateMessageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nonCandidateMessageCount), "Non-candidate message count cannot be negative.");
        }

        if (candidateAttachmentSnapshotCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateAttachmentSnapshotCount), "Candidate attachment snapshot count cannot be negative.");
        }

        if (deduplicatedAttachmentCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deduplicatedAttachmentCount), "Deduplicated attachment count cannot be negative.");
        }

        CompletedUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        ScannedMessageCount = scannedMessageCount;
        DetectedCandidateCount = detectedCandidateCount;
        NonCandidateMessageCount = nonCandidateMessageCount;
        CandidateAttachmentSnapshotCount = candidateAttachmentSnapshotCount;
        DeduplicatedAttachmentCount = deduplicatedAttachmentCount;
        FailureDetails = null;
    }

    public void Fail(DateTime completedUtc, int scannedMessageCount, int detectedCandidateCount, string failureDetails)
    {
        Complete(completedUtc, scannedMessageCount, detectedCandidateCount);
        FailureDetails = string.IsNullOrWhiteSpace(failureDetails)
            ? "Manual mailbox scan failed."
            : failureDetails.Trim();
    }
}

public sealed record MailboxFolderSelection(
    string ProviderFolderId,
    string? DisplayName = null,
    MailboxFolderSelectionMode Mode = MailboxFolderSelectionMode.Include)
{
    public MailboxFolderSelection Normalize()
    {
        MailboxFolderSelectionModeValues.EnsureSupported(Mode, nameof(Mode));

        return this with
        {
            ProviderFolderId = string.IsNullOrWhiteSpace(ProviderFolderId) ? string.Empty : ProviderFolderId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim()
        };
    }
}

public sealed class EmailMessageSnapshot : ICompanyOwnedEntity
{
    private EmailMessageSnapshot()
    {
    }

    public EmailMessageSnapshot(
        Guid id,
        Guid companyId,
        Guid mailboxConnectionId,
        Guid emailIngestionRunId,
        string externalMessageId,
        string? fromAddress,
        string? fromDisplayName,
        string? subject,
        DateTime? receivedUtc,
        string? folderId,
        string? folderDisplayName,
        string? bodyReference,
        string? untrustedBodyText,
        BillSourceType sourceType,
        EmailCandidateDecision candidateDecision,
        IReadOnlyCollection<BillDetectionRuleMatch> matchedRules,
        string reasonSummary,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (mailboxConnectionId == Guid.Empty)
        {
            throw new ArgumentException("MailboxConnectionId is required.", nameof(mailboxConnectionId));
        }

        if (emailIngestionRunId == Guid.Empty)
        {
            throw new ArgumentException("EmailIngestionRunId is required.", nameof(emailIngestionRunId));
        }

        BillSourceTypeValues.EnsureSupported(sourceType, nameof(sourceType));
        EmailCandidateDecisionValues.EnsureSupported(candidateDecision, nameof(candidateDecision));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MailboxConnectionId = mailboxConnectionId;
        EmailIngestionRunId = emailIngestionRunId;
        ExternalMessageId = NormalizeRequired(externalMessageId, nameof(externalMessageId), 512);
        FromAddress = NormalizeOptional(fromAddress, nameof(fromAddress), 320);
        FromDisplayName = NormalizeOptional(fromDisplayName, nameof(fromDisplayName), 256);
        Subject = NormalizeOptional(subject, nameof(subject), 500);
        SenderDomain = ExtractSenderDomain(FromAddress);
        ReceivedUtc = receivedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(receivedUtc.Value, nameof(receivedUtc)) : null;
        FolderId = NormalizeOptional(folderId, nameof(folderId), 256);
        FolderDisplayName = NormalizeOptional(folderDisplayName, nameof(folderDisplayName), 256);
        BodyReference = NormalizeOptional(bodyReference, nameof(bodyReference), 512);
        UntrustedBodyText = NormalizeOptional(untrustedBodyText, nameof(untrustedBodyText), 16000);
        SourceType = sourceType;
        CandidateDecision = candidateDecision;
        MatchedRules = matchedRules?.Distinct().ToList() ?? [];
        ReasonSummary = NormalizeRequired(reasonSummary, nameof(reasonSummary), 1000);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MailboxConnectionId { get; private set; }
    public Guid EmailIngestionRunId { get; private set; }
    public string ExternalMessageId { get; private set; } = null!;
    public string? FromAddress { get; private set; }
    public string? FromDisplayName { get; private set; }
    public string? Subject { get; private set; }
    public string? SenderDomain { get; private set; }
    public DateTime? ReceivedUtc { get; private set; }
    public string? FolderId { get; private set; }
    public string? FolderDisplayName { get; private set; }
    public string? BodyReference { get; private set; }
    public string? UntrustedBodyText { get; private set; }
    public BillSourceType SourceType { get; private set; }
    public EmailCandidateDecision CandidateDecision { get; private set; }
    public List<BillDetectionRuleMatch> MatchedRules { get; private set; } = [];
    public string ReasonSummary { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public MailboxConnection MailboxConnection { get; private set; } = null!;
    public EmailIngestionRun EmailIngestionRun { get; private set; } = null!;
    public ICollection<EmailAttachmentSnapshot> Attachments { get; } = new List<EmailAttachmentSnapshot>();

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            return trimmed[..maxLength];
        }

        return trimmed;
    }

    private static string? ExtractSenderDomain(string? fromAddress)
    {
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            return null;
        }

        var at = fromAddress.IndexOf('@', StringComparison.Ordinal);
        if (at < 0 || at == fromAddress.Length - 1)
        {
            return null;
        }

        return NormalizeOptional(fromAddress[(at + 1)..].ToLowerInvariant(), nameof(fromAddress), 256);
    }
}

public sealed class EmailAttachmentSnapshot : ICompanyOwnedEntity
{
    private EmailAttachmentSnapshot()
    {
    }

    public EmailAttachmentSnapshot(
        Guid id,
        Guid companyId,
        Guid emailMessageSnapshotId,
        string externalAttachmentId,
        string? fileName,
        string? mimeType,
        long? sizeBytes,
        string contentHash,
        string? storageReference,
        BillSourceType sourceType,
        string? untrustedExtractedText,
        bool isDuplicateByHash = false,
        Guid? canonicalAttachmentSnapshotId = null,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (emailMessageSnapshotId == Guid.Empty)
        {
            throw new ArgumentException("EmailMessageSnapshotId is required.", nameof(emailMessageSnapshotId));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Attachment size cannot be negative.");
        }

        BillSourceTypeValues.EnsureSupported(sourceType, nameof(sourceType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        EmailMessageSnapshotId = emailMessageSnapshotId;
        ExternalAttachmentId = NormalizeRequired(externalAttachmentId, nameof(externalAttachmentId), 512);
        FileName = NormalizeOptional(fileName, nameof(fileName), 512);
        MimeType = NormalizeOptional(mimeType, nameof(mimeType), 256);
        SizeBytes = sizeBytes;
        ContentHash = NormalizeRequired(contentHash, nameof(contentHash), 128);
        StorageReference = NormalizeOptional(storageReference, nameof(storageReference), 512);
        SourceType = sourceType;
        UntrustedExtractedText = NormalizeOptional(untrustedExtractedText, nameof(untrustedExtractedText), 16000);
        IsDuplicateByHash = isDuplicateByHash;
        CanonicalAttachmentSnapshotId = canonicalAttachmentSnapshotId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmailMessageSnapshotId { get; private set; }
    public string ExternalAttachmentId { get; private set; } = null!;
    public string? FileName { get; private set; }
    public string? MimeType { get; private set; }
    public long? SizeBytes { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public string? StorageReference { get; private set; }
    public BillSourceType SourceType { get; private set; }
    public string? UntrustedExtractedText { get; private set; }
    public bool IsDuplicateByHash { get; private set; }
    public Guid? CanonicalAttachmentSnapshotId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public EmailMessageSnapshot EmailMessageSnapshot { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
