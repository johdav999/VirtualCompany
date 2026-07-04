using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class InlineManualInboxBillScanJobScheduler : IManualInboxBillScanJobScheduler
{
    private readonly IManualInboxBillScanOrchestrator _orchestrator;

    public InlineManualInboxBillScanJobScheduler(IManualInboxBillScanOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task EnqueueManualScanAsync(ManualInboxBillScanJob job, CancellationToken cancellationToken) =>
        _orchestrator.ExecuteManualScanAsync(job, cancellationToken);
}

public sealed class ScopedManualInboxBillScanJobScheduler : IManualInboxBillScanJobScheduler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScopedManualInboxBillScanJobScheduler> _logger;

    public ScopedManualInboxBillScanJobScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<ScopedManualInboxBillScanJobScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task EnqueueManualScanAsync(ManualInboxBillScanJob job, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IManualInboxBillScanOrchestrator>();
                await orchestrator.ExecuteManualScanAsync(job, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Manual mailbox scan background job failed before completion. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. RunId: {RunId}.",
                    job.CompanyId,
                    job.MailboxConnectionId,
                    job.EmailIngestionRunId);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }
}

public sealed class CompanyManualInboxBillScanOrchestrator : IManualInboxBillScanOrchestrator
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IBillDetectionService _billDetectionService;
    private readonly IDocumentExtractionService? _documentExtractionService;
    private readonly IReadOnlyList<IDocumentTextExtractor> _documentTextExtractors;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyManualInboxBillScanOrchestrator> _logger;

    public CompanyManualInboxBillScanOrchestrator(
        VirtualCompanyDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        IMailboxProviderRegistry providerRegistry,
        IBillDetectionService billDetectionService,
        IDocumentExtractionService documentExtractionService,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider,
        ILogger<CompanyManualInboxBillScanOrchestrator> logger,
        IEnumerable<IDocumentTextExtractor>? documentTextExtractors = null)
        : this(dbContext, providerRegistry, billDetectionService, documentExtractionService, fieldEncryption, timeProvider, logger, documentTextExtractors)
    {
        _scopeFactory = scopeFactory;
    }

    public CompanyManualInboxBillScanOrchestrator(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IBillDetectionService billDetectionService,
        IDocumentExtractionService documentExtractionService,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider,
        ILogger<CompanyManualInboxBillScanOrchestrator> logger,
        IEnumerable<IDocumentTextExtractor>? documentTextExtractors = null)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _billDetectionService = billDetectionService;
        _documentExtractionService = documentExtractionService;
        _documentTextExtractors = documentTextExtractors?.ToArray() ?? [];
        _fieldEncryption = fieldEncryption;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteManualScanAsync(ManualInboxBillScanJob job, CancellationToken cancellationToken)
    {
        var run = await _dbContext.EmailIngestionRuns
            .SingleAsync(x => x.CompanyId == job.CompanyId && x.Id == job.EmailIngestionRunId, cancellationToken);
        var connection = await _dbContext.MailboxConnections
            .SingleAsync(
                x => x.CompanyId == job.CompanyId &&
                    x.UserId == job.UserId &&
                    x.Id == job.MailboxConnectionId,
                cancellationToken);

        var scanToUtc = job.ScanToUtc;
        var minimumScanFromUtc = scanToUtc.Subtract(CompanyMailboxConnectionService.ManualScanWindow);
        var scanFromUtc = job.ScanFromUtc < minimumScanFromUtc ? minimumScanFromUtc : job.ScanFromUtc;
        var scanned = 0;
        var detected = 0;
        var attachmentSnapshots = 0;
        var deduplicatedAttachments = 0;

        try
        {
            if (!CanRunManualScan(connection.Status))
            {
                throw new InvalidOperationException("Mailbox connection is not active.");
            }

            var accessToken = _fieldEncryption.Decrypt(
                job.CompanyId,
                CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken ?? throw new InvalidOperationException("Mailbox access token is missing."));

            var provider = _providerRegistry.Resolve(connection.Provider);
            var messages = await provider.ListMessagesAsync(
                accessToken,
                new MailboxMessageQuery(
                    scanFromUtc,
                    scanToUtc,
                    CompanyMailboxConnectionService.NormalizeFolders(connection.ConfiguredFolders, connection.Provider)),
                cancellationToken);

            scanned = messages.Count;
            _logger.LogInformation(
                "Manual mailbox bill scan fetched {MessageCount} message(s). CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. ScanFromUtc: {ScanFromUtc}. ScanToUtc: {ScanToUtc}. Folders: {Folders}.",
                scanned,
                job.CompanyId,
                connection.Provider,
                connection.Id,
                run.Id,
                scanFromUtc,
                scanToUtc,
                string.Join(", ", CompanyMailboxConnectionService.NormalizeFolders(connection.ConfiguredFolders, connection.Provider)
                    .Select(folder => $"{folder.DisplayName ?? folder.ProviderFolderId} ({folder.ProviderFolderId})")));

            var knownAttachmentSnapshotIdsByHash = await _dbContext.EmailAttachmentSnapshots
                .Where(x => x.CompanyId == job.CompanyId)
                .GroupBy(x => x.ContentHash)
                .Select(x => new { ContentHash = x.Key, SnapshotId = x.Min(y => y.Id) })
                .ToDictionaryAsync(x => x.ContentHash, x => x.SnapshotId, StringComparer.OrdinalIgnoreCase, cancellationToken);

            foreach (var message in messages)
            {
                var detection = _billDetectionService.Detect(message);
                LogScannedMessage(job, connection, run.Id, message, detection);
                if (!detection.IsCandidate)
                {
                    if (ShouldPersistRejectedAttachmentMessage(detection))
                    {
                        var existingRejectedSnapshot = await _dbContext.EmailMessageSnapshots
                            .Include(x => x.Attachments)
                            .SingleOrDefaultAsync(
                                x => x.CompanyId == job.CompanyId &&
                                    x.MailboxConnectionId == connection.Id &&
                                    x.ExternalMessageId == message.ProviderMessageId,
                                cancellationToken);
                        if (existingRejectedSnapshot is null)
                        {
                            var rejectedSnapshot = CreateSnapshot(
                                job,
                                connection.Id,
                                message,
                                detection,
                                knownAttachmentSnapshotIdsByHash,
                                EmailCandidateDecision.NotCandidate,
                                completedUtc: null);
                            var rejectedPersistenceResult = await PersistSnapshotAsync(rejectedSnapshot, knownAttachmentSnapshotIdsByHash, cancellationToken);
                            if (rejectedPersistenceResult.Inserted)
                            {
                                attachmentSnapshots += rejectedSnapshot.Attachments.Count;
                                deduplicatedAttachments += rejectedSnapshot.Attachments.Count(x => x.IsDuplicateByHash);
                            }
                        }
                    }

                    continue;
                }

                detected++;
                var extractionMessage = await HydrateBodyOnlyCandidateAsync(provider, accessToken, message, detection, cancellationToken);
                var existingSnapshot = await _dbContext.EmailMessageSnapshots
                    .Include(x => x.Attachments)
                    .SingleOrDefaultAsync(
                        x => x.CompanyId == job.CompanyId &&
                            x.MailboxConnectionId == connection.Id &&
                            x.ExternalMessageId == message.ProviderMessageId,
                        cancellationToken);
                if (existingSnapshot is not null)
                {
                    await EnsureBillExtractionAsync(job.CompanyId, existingSnapshot, extractionMessage.BodyPreview, cancellationToken);
                    await EnsureAttachmentBillExtractionAsync(provider, accessToken, job.CompanyId, existingSnapshot, cancellationToken);
                    continue;
                }

                var snapshot = CreateSnapshot(
                    job,
                    connection.Id,
                    extractionMessage,
                    detection,
                    knownAttachmentSnapshotIdsByHash,
                    EmailCandidateDecision.Candidate,
                    completedUtc: null);
                var persistenceResult = await PersistSnapshotAsync(snapshot, knownAttachmentSnapshotIdsByHash, cancellationToken);
                if (persistenceResult.Inserted)
                {
                    attachmentSnapshots += snapshot.Attachments.Count;
                    deduplicatedAttachments += snapshot.Attachments.Count(x => x.IsDuplicateByHash);
                }

                await EnsureBillExtractionAsync(job.CompanyId, persistenceResult.Snapshot, extractionMessage.BodyPreview, cancellationToken);
                await EnsureAttachmentBillExtractionAsync(provider, accessToken, job.CompanyId, persistenceResult.Snapshot, cancellationToken);
            }

            var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            run.Complete(
                completedUtc,
                scanned,
                detected,
                scanned - detected,
                attachmentSnapshots,
                deduplicatedAttachments);
            connection.MarkScanSucceeded(completedUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Manual mailbox bill scan completed. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. Scanned: {Scanned}. Candidates: {Candidates}.",
                job.CompanyId,
                connection.Provider,
                connection.Id,
                run.Id,
                scanned,
                detected);
        }
        catch (Exception ex)
        {
            await RecordScanFailureAsync(job, scanned, detected, ex, cancellationToken);

            _logger.LogWarning(
                ex,
                "Manual mailbox bill scan failed. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}.",
                job.CompanyId,
                connection.Provider,
                connection.Id,
                run.Id);
        }
    }

    private async Task RecordScanFailureAsync(
        ManualInboxBillScanJob job,
        int scanned,
        int detected,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var failure = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;

        try
        {
            _dbContext.ChangeTracker.Clear();
            var run = await _dbContext.EmailIngestionRuns
                .SingleOrDefaultAsync(x => x.CompanyId == job.CompanyId && x.Id == job.EmailIngestionRunId, cancellationToken);
            var connection = await _dbContext.MailboxConnections
                .SingleOrDefaultAsync(
                    x => x.CompanyId == job.CompanyId &&
                        x.UserId == job.UserId &&
                        x.Id == job.MailboxConnectionId,
                    cancellationToken);

            if (run is not null)
            {
                run.Fail(completedUtc, scanned, detected, failure);
            }

            if (connection is not null)
            {
                connection.SetStatus(MailboxConnectionStatus.Failed, failure);
            }

            if (run is not null || connection is not null)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException concurrencyException)
        {
            _logger.LogWarning(
                concurrencyException,
                "Manual mailbox scan failure state was already changed by another request. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. RunId: {RunId}.",
                job.CompanyId,
                job.MailboxConnectionId,
                job.EmailIngestionRunId);
        }
        catch (Exception failureRecordingException)
        {
            _logger.LogError(
                failureRecordingException,
                "Manual mailbox scan failure state could not be recorded. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. RunId: {RunId}.",
                job.CompanyId,
                job.MailboxConnectionId,
                job.EmailIngestionRunId);
        }
    }

    private async Task<SnapshotPersistenceResult> PersistSnapshotAsync(
        EmailMessageSnapshot snapshot,
        IDictionary<string, Guid> knownAttachmentSnapshotIdsByHash,
        CancellationToken cancellationToken)
    {
        _dbContext.EmailMessageSnapshots.Add(snapshot);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            TrackPersistedAttachmentSnapshots(snapshot, knownAttachmentSnapshotIdsByHash);
            return new SnapshotPersistenceResult(snapshot, Inserted: true);
        }
        catch (DbUpdateException ex) when (IsDuplicateMessageSnapshotException(ex))
        {
            DetachPendingSnapshot(snapshot);

            var existingSnapshot = await _dbContext.EmailMessageSnapshots
                .Include(x => x.Attachments)
                .SingleOrDefaultAsync(
                    x => x.CompanyId == snapshot.CompanyId &&
                        x.MailboxConnectionId == snapshot.MailboxConnectionId &&
                        x.ExternalMessageId == snapshot.ExternalMessageId,
                    cancellationToken);

            if (existingSnapshot is null)
            {
                throw;
            }

            TrackPersistedAttachmentSnapshots(existingSnapshot, knownAttachmentSnapshotIdsByHash);
            _logger.LogInformation(
                "Manual mailbox bill scan reused an email message snapshot created by another scan. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. MessageId: {MessageId}. SnapshotId: {SnapshotId}.",
                existingSnapshot.CompanyId,
                existingSnapshot.MailboxConnectionId,
                existingSnapshot.ExternalMessageId,
                existingSnapshot.Id);

            return new SnapshotPersistenceResult(existingSnapshot, Inserted: false);
        }
    }

    private void DetachPendingSnapshot(EmailMessageSnapshot snapshot)
    {
        var pendingEntries = _dbContext.ChangeTracker
            .Entries()
            .Where(entry =>
                entry.State == EntityState.Added &&
                (ReferenceEquals(entry.Entity, snapshot) ||
                    entry.Entity is EmailAttachmentSnapshot attachment &&
                    snapshot.Attachments.Any(snapshotAttachment => ReferenceEquals(snapshotAttachment, attachment))))
            .ToArray();

        foreach (var entry in pendingEntries)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateMessageSnapshotException(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        (sqlException.Number == 2601 || sqlException.Number == 2627) &&
        sqlException.Message.Contains(
            "IX_email_message_snapshots_company_id_mailbox_connection_id_external_message_id",
            StringComparison.OrdinalIgnoreCase);

    private static void TrackPersistedAttachmentSnapshots(
        EmailMessageSnapshot snapshot,
        IDictionary<string, Guid> knownAttachmentSnapshotIdsByHash)
    {
        foreach (var attachment in snapshot.Attachments)
        {
            if (!string.IsNullOrWhiteSpace(attachment.ContentHash))
            {
                knownAttachmentSnapshotIdsByHash.TryAdd(attachment.ContentHash, attachment.Id);
            }
        }
    }

    private void LogScannedMessage(
        ManualInboxBillScanJob job,
        MailboxConnection connection,
        Guid runId,
        MailboxMessageSummary message,
        BillDetectionResult detection)
    {
        _logger.LogInformation(
            "Manual mailbox bill scan evaluated message. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. MessageId: {MessageId}. ReceivedUtc: {ReceivedUtc}. From: {FromAddress}. Subject: {Subject}. Folder: {Folder}. Attachments: {Attachments}. Candidate: {IsCandidate}. MatchedRules: {MatchedRules}. SourceTypes: {SourceTypes}. Reason: {Reason}.",
            job.CompanyId,
            connection.Provider,
            connection.Id,
            runId,
            message.ProviderMessageId,
            message.ReceivedUtc,
            message.FromAddress ?? "(unknown)",
            RedactLogText(message.Subject),
            FormatFolder(message),
            FormatAttachments(message),
            detection.IsCandidate,
            FormatEnumList(detection.MatchedRules),
            FormatEnumList(detection.DetectedSourceTypes),
            detection.ReasonSummary);
    }

    private static string FormatFolder(MailboxMessageSummary message)
    {
        if (string.IsNullOrWhiteSpace(message.FolderId) && string.IsNullOrWhiteSpace(message.FolderDisplayName))
        {
            return "(unknown)";
        }

        return string.Equals(message.FolderId, message.FolderDisplayName, StringComparison.OrdinalIgnoreCase)
            ? message.FolderId ?? message.FolderDisplayName ?? "(unknown)"
            : $"{message.FolderDisplayName ?? "(unknown)"} ({message.FolderId ?? "unknown"})";
    }

    private static string FormatAttachments(MailboxMessageSummary message)
    {
        var attachments = message.AttachmentSummaries
            .Select(attachment => string.IsNullOrWhiteSpace(attachment.FileName)
                ? $"{attachment.ExternalAttachmentId} [{attachment.MimeType ?? "unknown"}]"
                : $"{attachment.FileName} [{attachment.MimeType ?? "unknown"}]")
            .ToArray();

        return attachments.Length == 0 ? "(none)" : string.Join(", ", attachments.Select(RedactLogText));
    }

    private static string FormatEnumList<T>(IReadOnlyCollection<T> values) where T : struct, Enum =>
        values.Count == 0 ? "(none)" : string.Join(", ", values.Select(value => value.ToString()));

    private static string RedactLogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        var trimmed = value.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= 160 ? trimmed : string.Concat(trimmed.AsSpan(0, 160), "...");
    }

    private static EmailMessageSnapshot CreateSnapshot(
        ManualInboxBillScanJob job,
        Guid mailboxConnectionId,
        MailboxMessageSummary message,
        BillDetectionResult detection,
        IDictionary<string, Guid> knownAttachmentSnapshotIdsByHash,
        EmailCandidateDecision candidateDecision,
        DateTime? completedUtc)
    {
        var sourceType = SelectPrimarySourceType(detection);
        var newAttachmentIdsByHash = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Body and attachment text came from an external mailbox and must stay untrusted downstream.
        var snapshot = new EmailMessageSnapshot(
            Guid.NewGuid(),
            job.CompanyId,
            mailboxConnectionId,
            job.EmailIngestionRunId,
            message.ProviderMessageId,
            message.FromAddress,
            message.FromDisplayName,
            message.Subject,
            message.ReceivedUtc,
            message.FolderId,
            message.FolderDisplayName,
            message.BodyReference,
            sourceType == BillSourceType.EmailBodyOnly ? message.BodyPreview ?? message.Snippet : null,
            sourceType,
            candidateDecision,
            detection.MatchedRules,
            detection.ReasonSummary,
            completedUtc);

        foreach (var attachment in detection.CandidateAttachments)
        {
            if (attachment.SourceType == BillSourceType.EmailBodyOnly)
            {
                continue;
            }

            var duplicateByHash =
                knownAttachmentSnapshotIdsByHash.TryGetValue(attachment.ContentHash, out var canonicalAttachmentSnapshotId) ||
                newAttachmentIdsByHash.TryGetValue(attachment.ContentHash, out canonicalAttachmentSnapshotId);
            var attachmentSnapshotId = Guid.NewGuid();
            if (!duplicateByHash)
            {
                newAttachmentIdsByHash[attachment.ContentHash] = attachmentSnapshotId;
            }

            snapshot.Attachments.Add(new EmailAttachmentSnapshot(
                attachmentSnapshotId,
                job.CompanyId,
                snapshot.Id,
                attachment.ExternalAttachmentId,
                attachment.FileName,
                attachment.MimeType,
                attachment.SizeBytes,
                attachment.ContentHash,
                attachment.StorageReference,
                attachment.SourceType,
                attachment.UntrustedExtractedText,
                duplicateByHash,
                duplicateByHash ? canonicalAttachmentSnapshotId : null,
                completedUtc));
        }

        return snapshot;
    }

    private sealed record SnapshotPersistenceResult(EmailMessageSnapshot Snapshot, bool Inserted);

    private static bool ShouldPersistRejectedAttachmentMessage(BillDetectionResult detection) =>
        !detection.IsCandidate &&
        detection.CandidateAttachments.Count > 0;

    private async Task<MailboxMessageSummary> HydrateBodyOnlyCandidateAsync(
        IMailboxProviderClient provider,
        string accessToken,
        MailboxMessageSummary message,
        BillDetectionResult detection,
        CancellationToken cancellationToken)
    {
        if (!detection.DetectedSourceTypes.Contains(BillSourceType.EmailBodyOnly) ||
            HasUsefulBodyPreview(message))
        {
            return message;
        }

        try
        {
            var fullMessage = await provider.GetMessageAsync(
                accessToken,
                new MailboxMessageFetchRequest(message.ProviderMessageId),
                cancellationToken);
            var bodyText = SelectBillBodyText(fullMessage);
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                return message;
            }

            return message with
            {
                Subject = string.IsNullOrWhiteSpace(fullMessage.Subject) ? message.Subject : fullMessage.Subject,
                BodyPreview = bodyText,
                FromAddress = fullMessage.Sender.Email ?? message.FromAddress,
                FromDisplayName = fullMessage.Sender.DisplayName ?? message.FromDisplayName,
                ReceivedUtc = fullMessage.ReceivedUtc ?? message.ReceivedUtc
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Manual mailbox bill scan could not fetch full message body for candidate. MessageId: {MessageId}.",
                message.ProviderMessageId);
            return message;
        }
    }

    private static bool HasUsefulBodyPreview(MailboxMessageSummary message) =>
        !string.IsNullOrWhiteSpace(message.BodyPreview) &&
        !string.Equals(message.BodyPreview, message.Snippet, StringComparison.Ordinal);

    private static string? SelectBillBodyText(MailboxInboundMessage message) =>
        !string.IsNullOrWhiteSpace(message.PlainTextBody)
            ? message.PlainTextBody
            : message.HtmlBody;

    private async Task EnsureBillExtractionAsync(
        Guid companyId,
        EmailMessageSnapshot snapshot,
        string? untrustedBodyTextOverride,
        CancellationToken cancellationToken)
    {
        var sourceEmailId = snapshot.Id.ToString("D");
        if (snapshot.SourceType == BillSourceType.EmailBodyOnly &&
            !string.IsNullOrWhiteSpace(untrustedBodyTextOverride ?? snapshot.UntrustedBodyText))
        {
            await ExtractDocumentAsync(
                companyId,
                BillDocumentInputType.EmailBodyText,
                untrustedBodyTextOverride ?? snapshot.UntrustedBodyText!,
                snapshot.Subject ?? "email-body",
                sourceEmailId,
                sourceAttachmentId: null,
                cancellationToken);
            return;
        }

        foreach (var attachment in snapshot.Attachments.Where(x => !x.IsDuplicateByHash))
        {
            if (string.IsNullOrWhiteSpace(attachment.UntrustedExtractedText))
            {
                continue;
            }

            var inputType = attachment.SourceType switch
            {
                BillSourceType.PdfAttachment => BillDocumentInputType.Pdf,
                BillSourceType.DocxAttachment => BillDocumentInputType.Docx,
                BillSourceType.ImageAttachment => BillDocumentInputType.EmailBodyText,
                _ => (BillDocumentInputType?)null
            };

            if (inputType is null)
            {
                continue;
            }

            await ExtractDocumentAsync(
                companyId,
                inputType.Value,
                attachment.UntrustedExtractedText,
                attachment.FileName ?? attachment.ExternalAttachmentId ?? "mailbox-attachment",
                sourceEmailId,
                attachment.Id.ToString("D"),
                cancellationToken);
        }
    }

    private async Task ExtractDocumentAsync(
        Guid companyId,
        BillDocumentInputType inputType,
        string untrustedText,
        string sourceDocumentName,
        string sourceEmailId,
        string? sourceAttachmentId,
        CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(untrustedText));
        var extractionService = ResolveDocumentExtractionService(out var scope);
        using (scope)
        {
            await extractionService.ExtractAsync(
                new ExtractBillDocumentCommand(
                    companyId,
                    inputType,
                    content,
                    sourceDocumentName,
                    sourceEmailId,
                    sourceAttachmentId),
                cancellationToken);
        }
    }

    private async Task EnsureAttachmentBillExtractionAsync(
        IMailboxProviderClient provider,
        string accessToken,
        Guid companyId,
        EmailMessageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in snapshot.Attachments.Where(x => !x.IsDuplicateByHash))
        {
            if (!string.IsNullOrWhiteSpace(attachment.UntrustedExtractedText))
            {
                continue;
            }

            var inputType = GetAttachmentInputType(attachment.SourceType);
            if (inputType is null || string.IsNullOrWhiteSpace(attachment.ExternalAttachmentId))
            {
                continue;
            }

            try
            {
                var content = await provider.GetAttachmentContentAsync(
                    accessToken,
                    new MailboxAttachmentFetchRequest(
                        snapshot.ExternalMessageId,
                        attachment.ExternalAttachmentId!,
                        attachment.FileName,
                        attachment.MimeType),
                    cancellationToken);

                if (content is null || content.Content.Length == 0)
                {
                    continue;
                }

                var extractedText = await ExtractAttachmentTextAsync(
                    inputType.Value,
                    content.Content,
                    attachment.FileName ?? attachment.ExternalAttachmentId ?? "mailbox-attachment",
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    attachment.UpdateUntrustedExtractedText(extractedText);
                    _logger.LogInformation(
                        "Manual mailbox bill scan extracted attachment text. CompanyId: {CompanyId}. MessageId: {MessageId}. AttachmentId: {AttachmentId}. FileName: {FileName}. SourceType: {SourceType}. TextLength: {TextLength}.",
                        companyId,
                        snapshot.ExternalMessageId,
                        attachment.ExternalAttachmentId,
                        RedactLogText(attachment.FileName),
                        attachment.SourceType,
                        extractedText.Length);
                }
                else
                {
                    _logger.LogInformation(
                        "Manual mailbox bill scan found no readable attachment text. CompanyId: {CompanyId}. MessageId: {MessageId}. AttachmentId: {AttachmentId}. FileName: {FileName}. SourceType: {SourceType}.",
                        companyId,
                        snapshot.ExternalMessageId,
                        attachment.ExternalAttachmentId,
                        RedactLogText(attachment.FileName),
                        attachment.SourceType);
                }

                await using var stream = new MemoryStream(content.Content);
                await ExtractDocumentStreamAsync(
                    companyId,
                    inputType.Value,
                    stream,
                    attachment.FileName ?? attachment.ExternalAttachmentId ?? "mailbox-attachment",
                    snapshot.Id.ToString("D"),
                    attachment.Id.ToString("D"),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Manual mailbox bill scan could not extract attachment content. CompanyId: {CompanyId}. MessageId: {MessageId}. AttachmentId: {AttachmentId}. FileName: {FileName}.",
                    companyId,
                    snapshot.ExternalMessageId,
                    attachment.ExternalAttachmentId,
                    RedactLogText(attachment.FileName));
            }
        }
    }

    private async Task<string?> ExtractAttachmentTextAsync(
        BillDocumentInputType inputType,
        byte[] content,
        string sourceDocumentName,
        CancellationToken cancellationToken)
    {
        var extractors = _documentTextExtractors.Where(x => x.Supports(inputType)).ToArray();
        if (extractors.Length == 0)
        {
            return null;
        }

        foreach (var extractor in extractors)
        {
            await using var stream = new MemoryStream(content);
            var document = await extractor.ExtractAsync(stream, sourceDocumentName, inputType, cancellationToken);
            if (!string.IsNullOrWhiteSpace(document.FullText))
            {
                return document.FullText.Trim();
            }
        }

        return null;
    }

    private async Task ExtractDocumentStreamAsync(
        Guid companyId,
        BillDocumentInputType inputType,
        Stream content,
        string sourceDocumentName,
        string sourceEmailId,
        string? sourceAttachmentId,
        CancellationToken cancellationToken)
    {
        var extractionService = ResolveDocumentExtractionService(out var scope);
        using (scope)
        {
            await extractionService.ExtractAsync(
                new ExtractBillDocumentCommand(
                    companyId,
                    inputType,
                    content,
                    sourceDocumentName,
                    sourceEmailId,
                    sourceAttachmentId),
                cancellationToken);
        }
    }

    private static BillDocumentInputType? GetAttachmentInputType(BillSourceType sourceType) =>
        sourceType switch
        {
            BillSourceType.PdfAttachment => BillDocumentInputType.Pdf,
            BillSourceType.DocxAttachment => BillDocumentInputType.Docx,
            BillSourceType.ImageAttachment => BillDocumentInputType.Image,
            _ => null
        };

    private IDocumentExtractionService ResolveDocumentExtractionService(out IServiceScope? scope)
    {
        if (_scopeFactory is not null)
        {
            scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IDocumentExtractionService>();
        }

        scope = null;
        return _documentExtractionService ?? throw new InvalidOperationException("Document extraction service is not configured.");
    }

    private static BillSourceType SelectPrimarySourceType(BillDetectionResult detection) =>
        detection.DetectedSourceTypes
            .OrderBy(GetSourceTypePrecedence)
            .FirstOrDefault(BillSourceType.EmailBodyOnly);

    private static int GetSourceTypePrecedence(BillSourceType sourceType) =>
        sourceType switch
        {
            BillSourceType.PdfAttachment => 0,
            BillSourceType.DocxAttachment => 1,
            BillSourceType.ImageAttachment => 2,
            BillSourceType.EmailBodyOnly => 3,
            _ => 3
        };

    private static bool CanRunManualScan(MailboxConnectionStatus status) =>
        status is MailboxConnectionStatus.Active or MailboxConnectionStatus.Failed;
}
