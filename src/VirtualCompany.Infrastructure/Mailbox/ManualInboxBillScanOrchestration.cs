using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IBillDetectionService _billDetectionService;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyManualInboxBillScanOrchestrator> _logger;

    public CompanyManualInboxBillScanOrchestrator(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IBillDetectionService billDetectionService,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider,
        ILogger<CompanyManualInboxBillScanOrchestrator> logger)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _billDetectionService = billDetectionService;
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
            if (connection.Status != MailboxConnectionStatus.Active)
            {
                throw new InvalidOperationException("Mailbox connection is not active.");
            }

            var accessToken = _fieldEncryption.Decrypt(
                job.CompanyId,
                CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken ?? throw new InvalidOperationException("Mailbox access token is missing."));

            var messages = await _providerRegistry.Resolve(connection.Provider).ListMessagesAsync(
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
                    continue;
                }

                detected++;
                var existingSnapshot = await _dbContext.EmailMessageSnapshots
                    .AnyAsync(
                        x => x.CompanyId == job.CompanyId &&
                            x.MailboxConnectionId == connection.Id &&
                            x.ExternalMessageId == message.ProviderMessageId,
                        cancellationToken);
                if (existingSnapshot)
                {
                    continue;
                }

                var snapshot = CreateSnapshot(job, connection.Id, message, detection, knownAttachmentSnapshotIdsByHash, completedUtc: null);
                attachmentSnapshots += snapshot.Attachments.Count;
                deduplicatedAttachments += snapshot.Attachments.Count(x => x.IsDuplicateByHash);
                _dbContext.EmailMessageSnapshots.Add(snapshot);
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
            var completedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var failure = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            run.Fail(completedUtc, scanned, detected, failure);
            connection.SetStatus(MailboxConnectionStatus.Failed, failure);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                ex,
                "Manual mailbox bill scan failed. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}.",
                job.CompanyId,
                connection.Provider,
                connection.Id,
                run.Id);
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
        DateTime? completedUtc)
    {
        var sourceType = SelectPrimarySourceType(detection);

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
            EmailCandidateDecision.Candidate,
            detection.MatchedRules,
            detection.ReasonSummary,
            completedUtc);

        foreach (var attachment in detection.CandidateAttachments)
        {
            if (attachment.SourceType == BillSourceType.EmailBodyOnly)
            {
                continue;
            }

            var duplicateByHash = knownAttachmentSnapshotIdsByHash.TryGetValue(attachment.ContentHash, out var canonicalAttachmentSnapshotId);
            var attachmentSnapshotId = Guid.NewGuid();
            if (!duplicateByHash)
            {
                knownAttachmentSnapshotIdsByHash[attachment.ContentHash] = attachmentSnapshotId;
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

    private static BillSourceType SelectPrimarySourceType(BillDetectionResult detection) =>
        detection.DetectedSourceTypes
            .OrderBy(GetSourceTypePrecedence)
            .FirstOrDefault(BillSourceType.EmailBodyOnly);

    private static int GetSourceTypePrecedence(BillSourceType sourceType) =>
        sourceType switch
        {
            BillSourceType.PdfAttachment => 0,
            BillSourceType.DocxAttachment => 1,
            BillSourceType.EmailBodyOnly => 2,
            _ => 3
        };
}
