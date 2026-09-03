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
    private readonly ISupplierSubscriptionDocumentClassifier? _subscriptionDocumentClassifier;
    private readonly IEmailClassificationService? _emailClassifier;
    private readonly IReadOnlyList<IDocumentTextExtractor> _documentTextExtractors;
    private readonly IMailboxOAuthAccessTokenLeaseService _tokenLeaseService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyManualInboxBillScanOrchestrator> _logger;

    public CompanyManualInboxBillScanOrchestrator(
        VirtualCompanyDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        IMailboxProviderRegistry providerRegistry,
        IBillDetectionService billDetectionService,
        IDocumentExtractionService documentExtractionService,
        IMailboxOAuthAccessTokenLeaseService tokenLeaseService,
        TimeProvider timeProvider,
        ILogger<CompanyManualInboxBillScanOrchestrator> logger,
        IEnumerable<IDocumentTextExtractor>? documentTextExtractors = null,
        ISupplierSubscriptionDocumentClassifier? subscriptionDocumentClassifier = null,
        IEmailClassificationService? emailClassifier = null)
        : this(dbContext, providerRegistry, billDetectionService, documentExtractionService, tokenLeaseService, timeProvider, logger, documentTextExtractors, subscriptionDocumentClassifier, emailClassifier)
    {
        _scopeFactory = scopeFactory;
    }

    public CompanyManualInboxBillScanOrchestrator(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IBillDetectionService billDetectionService,
        IDocumentExtractionService documentExtractionService,
        IMailboxOAuthAccessTokenLeaseService tokenLeaseService,
        TimeProvider timeProvider,
        ILogger<CompanyManualInboxBillScanOrchestrator> logger,
        IEnumerable<IDocumentTextExtractor>? documentTextExtractors = null,
        ISupplierSubscriptionDocumentClassifier? subscriptionDocumentClassifier = null,
        IEmailClassificationService? emailClassifier = null)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _billDetectionService = billDetectionService;
        _documentExtractionService = documentExtractionService;
        _subscriptionDocumentClassifier = subscriptionDocumentClassifier;
        _emailClassifier = emailClassifier;
        _documentTextExtractors = documentTextExtractors?.ToArray() ?? [];
        _tokenLeaseService = tokenLeaseService;
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
                    x.Id == job.MailboxConnectionId,
                cancellationToken);

        var scanToUtc = job.ScanToUtc;
        var minimumScanFromUtc = scanToUtc.Subtract(MailboxConnectionDefaults.ManualScanWindow);
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

            var provider = _providerRegistry.Resolve(connection.Provider);
            var accessToken = (await _tokenLeaseService.AcquireAsync(
                job.CompanyId, connection.Id, provider.ReadRequiredScopes, cancellationToken)).AccessToken;

            var messages = await provider.ListMessagesAsync(
                accessToken,
                new MailboxMessageQuery(
                    scanFromUtc,
                    scanToUtc,
                    MailboxConnectionDefaults.NormalizeFolders(connection.ConfiguredFolders, connection.Provider)),
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
                string.Join(", ", MailboxConnectionDefaults.NormalizeFolders(connection.ConfiguredFolders, connection.Provider)
                    .Select(folder => $"{folder.DisplayName ?? folder.ProviderFolderId} ({folder.ProviderFolderId})")));

            var knownAttachmentSnapshots = await _dbContext.EmailAttachmentSnapshots
                .Where(x => x.CompanyId == job.CompanyId)
                .Select(x => new { x.ContentHash, SnapshotId = x.Id, x.CreatedUtc })
                .ToListAsync(cancellationToken);
            var knownAttachmentSnapshotIdsByHash = knownAttachmentSnapshots
                .GroupBy(x => x.ContentHash, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(x => x.CreatedUtc).ThenBy(x => x.SnapshotId).First().SnapshotId,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var message in messages)
            {
                var detection = await ClassifyFinanceMessageAsync(job, connection, message, cancellationToken);
                var evaluationMessage = message;
                if (ShouldHydratePossibleBodyOnlyCandidate(message, detection))
                {
                    _logger.LogInformation(
                        "Manual mailbox bill scan found a weak body-only bill signal and will fetch the full message before rejecting it. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. MessageId: {MessageId}. SubjectPresent: {SubjectPresent}. SnippetLength: {SnippetLength}. BodyPreviewLength: {BodyPreviewLength}. SenderDomain: {SenderDomain}. InitialReason: {InitialReason}.",
                        job.CompanyId,
                        connection.Provider,
                        connection.Id,
                        run.Id,
                        message.ProviderMessageId,
                        !string.IsNullOrWhiteSpace(message.Subject),
                        message.Snippet?.Length ?? 0,
                        message.BodyPreview?.Length ?? 0,
                        GetSenderDomain(message.FromAddress),
                        detection.ReasonSummary);

                    evaluationMessage = await HydratePossibleBodyOnlyCandidateAsync(provider, accessToken, message, cancellationToken);
                    if (!ReferenceEquals(evaluationMessage, message))
                    {
                        detection = await ClassifyFinanceMessageAsync(job, connection, evaluationMessage, cancellationToken);
                        _logger.LogInformation(
                            "Manual mailbox bill scan re-evaluated a hydrated body-only message. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. MessageId: {MessageId}. Candidate: {Candidate}. SourceTypes: {SourceTypes}. BodyLength: {BodyLength}. Reason: {Reason}.",
                            job.CompanyId,
                            connection.Provider,
                            connection.Id,
                            run.Id,
                            evaluationMessage.ProviderMessageId,
                            detection.IsCandidate,
                            FormatEnumList(detection.DetectedSourceTypes),
                            evaluationMessage.BodyPreview?.Length ?? 0,
                            detection.ReasonSummary);
                    }
                }

                LogScannedMessage(job, connection, run.Id, evaluationMessage, detection);
                if (!detection.IsCandidate)
                {
                    if (ShouldPersistRejectedAttachmentMessage(detection))
                    {
                        var existingRejectedSnapshot = await _dbContext.EmailMessageSnapshots
                            .Include(x => x.Attachments)
                            .SingleOrDefaultAsync(
                                x => x.CompanyId == job.CompanyId &&
                                    x.MailboxConnectionId == connection.Id &&
                                    x.ExternalMessageId == evaluationMessage.ProviderMessageId,
                                cancellationToken);
                        if (existingRejectedSnapshot is null)
                        {
                            var rejectedSnapshot = CreateSnapshot(
                                job,
                                connection.Id,
                                evaluationMessage,
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

                            await ClassifySubscriptionSourceAsync(job.CompanyId, job.UserId, rejectedPersistenceResult.Snapshot, cancellationToken);
                        }
                    }

                    continue;
                }

                detected++;
                var extractionMessage = await HydrateBodyOnlyCandidateAsync(provider, accessToken, evaluationMessage, detection, cancellationToken);
                var existingSnapshot = await _dbContext.EmailMessageSnapshots
                    .Include(x => x.Attachments)
                    .SingleOrDefaultAsync(
                        x => x.CompanyId == job.CompanyId &&
                            x.MailboxConnectionId == connection.Id &&
                            x.ExternalMessageId == extractionMessage.ProviderMessageId,
                        cancellationToken);
                if (existingSnapshot is not null)
                {
                    await EnsureBillExtractionAsync(job.CompanyId, existingSnapshot, extractionMessage.BodyPreview, cancellationToken);
                    await EnsureAttachmentBillExtractionAsync(provider, accessToken, job.CompanyId, existingSnapshot, cancellationToken);
                    await ClassifySubscriptionSourceAsync(job.CompanyId, job.UserId, existingSnapshot, cancellationToken);
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
                await ClassifySubscriptionSourceAsync(job.CompanyId, job.UserId, persistenceResult.Snapshot, cancellationToken);
            }

            var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (connection.Provider == MailboxProvider.StandardEmail)
            {
                await AdvanceStandardMailboxCursorsAsync(connection, messages, completedUtc, cancellationToken);
            }
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

    private async Task AdvanceStandardMailboxCursorsAsync(
        MailboxConnection connection,
        IReadOnlyList<MailboxMessageSummary> messages,
        DateTime completedUtc,
        CancellationToken cancellationToken)
    {
        var checkpoints = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.FolderId) &&
                StandardMailboxMessageReference.TryRead(message.ProviderMessageId, out _, out _))
            .Select(message =>
            {
                StandardMailboxMessageReference.TryRead(message.ProviderMessageId, out var uidValidity, out var uid);
                return new { FolderId = message.FolderId!, UidValidity = uidValidity, Uid = uid };
            })
            .GroupBy(item => new { item.FolderId, item.UidValidity })
            .Select(group => new { group.Key.FolderId, group.Key.UidValidity, LastUid = group.Max(item => item.Uid) })
            .ToArray();

        foreach (var checkpoint in checkpoints)
        {
            var cursor = await _dbContext.MailboxFolderSyncCursors
                .SingleOrDefaultAsync(item => item.CompanyId == connection.CompanyId &&
                    item.MailboxConnectionId == connection.Id &&
                    item.FolderId == checkpoint.FolderId, cancellationToken);
            if (cursor is null)
            {
                cursor = new MailboxFolderSyncCursor(Guid.NewGuid(), connection.CompanyId, connection.Id, checkpoint.FolderId, completedUtc);
                _dbContext.MailboxFolderSyncCursors.Add(cursor);
            }
            else if (cursor.Status == MailboxCursorStatus.ReconciliationRequired)
            {
                cursor.ResetAfterReconciliation(checkpoint.UidValidity, completedUtc);
            }

            cursor.Advance(checkpoint.UidValidity, checkpoint.LastUid, null, completedUtc);
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
            "Manual mailbox bill scan evaluated message. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. MessageId: {MessageId}. ReceivedUtc: {ReceivedUtc}. SenderDomain: {SenderDomain}. Folder: {Folder}. SubjectPresent: {SubjectPresent}. SnippetLength: {SnippetLength}. BodyPreviewLength: {BodyPreviewLength}. AttachmentCount: {AttachmentCount}. Candidate: {IsCandidate}. MatchedRules: {MatchedRules}. SourceTypes: {SourceTypes}. Reason: {Reason}.",
            job.CompanyId,
            connection.Provider,
            connection.Id,
            runId,
            message.ProviderMessageId,
            message.ReceivedUtc,
            GetSenderDomain(message.FromAddress),
            FormatFolder(message),
            !string.IsNullOrWhiteSpace(message.Subject),
            message.Snippet?.Length ?? 0,
            message.BodyPreview?.Length ?? 0,
            message.AttachmentSummaries.Count,
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

    private static string GetSenderDomain(string? fromAddress)
    {
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            return "(unknown)";
        }

        var atIndex = fromAddress.LastIndexOf('@');
        if (atIndex < 0 || atIndex == fromAddress.Length - 1)
        {
            return "(unknown)";
        }

        return fromAddress[(atIndex + 1)..].Trim().TrimEnd('>').ToLowerInvariant();
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

    private static bool ShouldHydratePossibleBodyOnlyCandidate(
        MailboxMessageSummary message,
        BillDetectionResult detection)
    {
        if (detection.IsCandidate ||
            detection.CandidateAttachments.Count > 0 ||
            HasUsefulBodyPreview(message))
        {
            return false;
        }

        var weakSignalText = string.Join(
            " ",
            message.Subject,
            message.Snippet,
            message.FromAddress,
            message.FolderId,
            message.FolderDisplayName,
            string.Join(" ", message.AttachmentFileNames));

        return ContainsIgnoreCase(weakSignalText, "invoice") ||
            ContainsIgnoreCase(weakSignalText, "faktura") ||
            ContainsIgnoreCase(weakSignalText, "bill") ||
            ContainsIgnoreCase(weakSignalText, "supplier") ||
            ContainsIgnoreCase(weakSignalText, "amount due") ||
            ContainsIgnoreCase(weakSignalText, "payment due") ||
            ContainsIgnoreCase(weakSignalText, "due date");
    }

    private async Task<MailboxMessageSummary> HydratePossibleBodyOnlyCandidateAsync(
        IMailboxProviderClient provider,
        string accessToken,
        MailboxMessageSummary message,
        CancellationToken cancellationToken)
    {
        try
        {
            var fullMessage = await provider.GetMessageAsync(
                accessToken,
                new MailboxMessageFetchRequest(message.ProviderMessageId),
                cancellationToken);
            var bodyText = SelectBillBodyText(fullMessage);
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                _logger.LogInformation(
                    "Manual mailbox bill scan fetched a weak body-only signal but no usable full body text was returned. MessageId: {MessageId}.",
                    message.ProviderMessageId);
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
                "Manual mailbox bill scan could not fetch full message body for weak body-only signal. MessageId: {MessageId}.",
                message.ProviderMessageId);
            return message;
        }
    }

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
                _logger.LogInformation(
                    "Manual mailbox bill scan fetched a body-only candidate but no usable text was returned. MessageId: {MessageId}.",
                    message.ProviderMessageId);
                return message;
            }

            _logger.LogInformation(
                "Manual mailbox bill scan hydrated a body-only candidate. MessageId: {MessageId}. BodyLength: {BodyLength}.",
                message.ProviderMessageId,
                bodyText.Length);
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

    private static bool ContainsIgnoreCase(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string? SelectBillBodyText(MailboxInboundMessage message) =>
        !string.IsNullOrWhiteSpace(message.PlainTextBody)
            ? message.PlainTextBody
            : message.HtmlBody;

    private async Task<BillDetectionResult> ClassifyFinanceMessageAsync(ManualInboxBillScanJob job, MailboxConnection connection, MailboxMessageSummary message, CancellationToken cancellationToken)
    {
        var detection = _billDetectionService.Detect(message);
        if (_emailClassifier is null)
        {
            return detection;
        }

        try
        {
            var classification = await _emailClassifier.ClassifyAsync(
                new EmailClassificationRequest(
                    job.CompanyId,
                    MailboxPurpose.Finance,
                    connection.Provider.ToStorageValue(),
                    connection.Id,
                    message,
                    [],
                    AllowAi: false),
                cancellationToken);
            _logger.LogInformation(
                "Finance mailbox message classified. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. MessageId: {MessageId}. Domain: {Domain}. Intent: {Intent}. Confidence: {Confidence}. Action: {Action}. UsedAi: {UsedAi}.",
                job.CompanyId,
                connection.Id,
                message.ProviderMessageId,
                classification.Domain,
                classification.Intent,
                classification.Confidence,
                classification.RecommendedAction,
                classification.UsedAi);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Finance mailbox message classification failed safely. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. MessageId: {MessageId}.", job.CompanyId, connection.Id, message.ProviderMessageId);
        }

        return detection;
    }
    private async Task ClassifySubscriptionSourceAsync(Guid companyId, Guid? actorUserId, EmailMessageSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_subscriptionDocumentClassifier is null)
        {
            return;
        }

        try
        {
            await _subscriptionDocumentClassifier.ClassifyAsync(
                new ClassifySupplierSubscriptionSourceCommand(companyId, snapshot.Id, actorUserId, "Laura"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Supplier subscription source classification failed after mailbox snapshot persisted. CompanyId: {CompanyId}. MessageSnapshotId: {MessageSnapshotId}.",
                companyId,
                snapshot.Id);
        }
    }
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
            if (DocumentTextQuality.IsUsableForBillExtraction(document))
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



