using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class DocumentRepositorySynchronizationOptions
{
    public const string SectionName = "DocumentRepositorySynchronization";
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;
    public int SynchronizationIntervalMinutes { get; set; } = 15;
    public int LeaseSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 8;
    public int InitialRetrySeconds { get; set; } = 30;
}

internal sealed class CompanyDocumentRepositorySynchronizationService(
    VirtualCompanyDbContext db,
    IDocumentRepositoryGraphAdapter graph,
    ICompanyDocumentStorage storage,
    ITrustedCompanyDocumentIngestionService ingestion,
    ICompanyKnowledgeIndexingProcessor indexing,
    ICompanyDocumentIngestionStatusService ingestionStatus,
    ICompanyContextAccessor companyContext,
    ICorrelationContextAccessor correlation,
    IOptions<CompanyDocumentOptions> documentOptions,
    IOptions<DocumentRepositorySynchronizationOptions> syncOptions,
    IOptions<DocumentRepositoryOperationsOptions> operationsOptions,
    IAuditEventWriter audit,
    TimeProvider clock,
    ILogger<CompanyDocumentRepositorySynchronizationService> logger) : ICompanyDocumentRepositorySynchronizationService
{
    private static readonly Meter Meter = new("VirtualCompany.DocumentRepositories");
    private static readonly Counter<long> CompletedJobs = Meter.CreateCounter<long>("document_repository.sync.completed");
    private static readonly Counter<long> ItemFailures = Meter.CreateCounter<long>("document_repository.sync.item_failures");
    private static readonly Counter<long> AuthorizationFailures = Meter.CreateCounter<long>("document_repository.sync.authorization_failures");
    private static readonly Histogram<double> QueueAge = Meter.CreateHistogram<double>("document_repository.sync.queue_age_seconds");
    private readonly DocumentRepositorySynchronizationOptions _options = syncOptions.Value;

    public async Task<DocumentRepositorySynchronizationJobDto> StartAsync(Guid companyId, Guid connectionId,
        StartDocumentRepositorySynchronizationCommand command, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        if (!operationsOptions.Value.Enabled) throw new DocumentRepositoryUnavailableException("feature_disabled", "Document repository operations are disabled for this deployment.");
        var key = ValidateKey(command.IdempotencyKey);
        var connection = await db.CompanyDocumentRepositoryConnections.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, cancellationToken)
            ?? throw new DocumentRepositoryNotFoundException();
        EnsureActive(connection);
        if (connection.SynchronizationPausedUtc.HasValue) throw new DocumentRepositoryUnavailableException("synchronization_paused", "Repository synchronization is paused by an administrator.");
        var existing = await db.CompanyDocumentRepositorySynchronizationJobs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null) return Map(existing);
        var active = await db.CompanyDocumentRepositorySynchronizationJobs.AsNoTracking().OrderBy(x => x.CreatedUtc)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId &&
                (x.Status == DocumentRepositorySynchronizationStates.Queued || x.Status == DocumentRepositorySynchronizationStates.Running || x.Status == DocumentRepositorySynchronizationStates.RetryWaiting), cancellationToken);
        if (active is not null) return Map(active);
        var now = clock.GetUtcNow().UtcDateTime;
        var job = CreateJob(connection, key, command.ForceFullReconciliation, now);
        db.CompanyDocumentRepositorySynchronizationJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(job, AuditEventActions.DocumentRepositorySynchronizationQueued, AuditEventOutcomes.Succeeded, "Queued repository synchronization.", cancellationToken);
        return Map(job);
    }

    public Task<DocumentRepositorySynchronizationJobDto> RetryFailedItemsAsync(Guid companyId, Guid connectionId,
        RetryDocumentRepositoryFailuresCommand command, CancellationToken cancellationToken)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new DocumentRepositoryValidationException("A recovery idempotency key is required.");
        return StartAsync(companyId, connectionId,
            new StartDocumentRepositorySynchronizationCommand(command.IdempotencyKey, ForceFullReconciliation: true), cancellationToken);
    }

    public async Task<DocumentRepositorySynchronizationJobDto?> GetAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var job = await db.CompanyDocumentRepositorySynchronizationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId && x.Id == jobId, cancellationToken);
        return job is null ? null : Map(job);
    }

    public async Task CancelAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var job = await db.CompanyDocumentRepositorySynchronizationJobs.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId && x.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException("The repository synchronization job was not found.");
        job.Cancel(clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        if (!operationsOptions.Value.Enabled) return;
        await ScheduleDueConnectionsAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await db.CompanyDocumentRepositorySynchronizationJobs.IgnoreQueryFilters()
            .Where(x => x.Connection.SynchronizationPausedUtc == null &&
                (x.Status == DocumentRepositorySynchronizationStates.Queued ||
                 (x.Status == DocumentRepositorySynchronizationStates.RetryWaiting && x.NextRetryUtc <= now) ||
                 (x.Status == DocumentRepositorySynchronizationStates.Running && x.LeaseExpiresUtc <= now)))
            .OrderBy(x => x.CreatedUtc).Take(4).Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var id in candidates)
        {
            db.ChangeTracker.Clear();
            var job = await db.CompanyDocumentRepositorySynchronizationJobs.IgnoreQueryFilters().Include(x => x.Connection).SingleAsync(x => x.Id == id, cancellationToken);
            if (job.Connection.SynchronizationPausedUtc.HasValue)
            {
                job.Defer(now);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }
            var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
            if (!job.TryClaim(owner, now, TimeSpan.FromSeconds(Math.Max(30, _options.LeaseSeconds)))) continue;
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { continue; }
            QueueAge.Record(Math.Max(0, (now - job.CreatedUtc).TotalSeconds));
            await ProcessClaimedAsync(job, cancellationToken);
        }
    }

    private async Task ProcessClaimedAsync(CompanyDocumentRepositorySynchronizationJob job, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (job.Connection.LifecycleState != DocumentRepositoryLifecycleStates.Active) { job.Fail("connection_not_active", "The repository connection is not active.", now); await db.SaveChangesAsync(cancellationToken); return; }
        var context = ToContext(job.Connection);
        try
        {
            if (job.Mode == DocumentRepositorySynchronizationModes.Delta)
            {
                try
                {
                    var page = await graph.ReadDeltaPageAsync(context, job.PageCursor, cancellationToken);
                    await ApplyDeltaPageAsync(job, page, context, cancellationToken);
                    if (page.NextCursor is not null) return;
                    job.Connection.RecordSynchronization(DocumentRepositorySynchronizationModes.Delta, page.DeltaCursor ?? job.PendingDeltaCursor, clock.GetUtcNow().UtcDateTime);
                    job.Complete(clock.GetUtcNow().UtcDateTime);
                }
                catch (DocumentRepositoryUnavailableException ex) when (ex.Code is DocumentRepositoryValidationCodes.MissingAccess or "delta_token_expired")
                {
                    // The v1.0 delta permission table does not list Selected application permissions.
                    // A bounded, complete enumeration is therefore the compatibility path and only
                    // commits removals after the enumeration succeeds.
                    job.UseFullReconciliation(clock.GetUtcNow().UtcDateTime);
                    await db.SaveChangesAsync(cancellationToken);
                    await RunFullReconciliationAsync(job, context, cancellationToken);
                }
            }
            else await RunFullReconciliationAsync(job, context, cancellationToken);

            if (job.Status == DocumentRepositorySynchronizationStates.Completed)
            {
                CompletedJobs.Add(1);
                await db.SaveChangesAsync(cancellationToken);
                await WriteAuditAsync(job, AuditEventActions.DocumentRepositorySynchronizationCompleted, AuditEventOutcomes.Succeeded, "Repository synchronization completed.", cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DocumentRepositoryUnavailableException ex)
        {
            if (ex.Code is DocumentRepositoryValidationCodes.MissingAccess or DocumentRepositoryValidationCodes.InvalidCredentials or DocumentRepositoryValidationCodes.NotFound) AuthorizationFailures.Add(1);
            await RecordFailureAsync(job, ex.Code, ex.SafeMessage, IsRetryable(ex.Code), cancellationToken, ex.RetryAfter);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repository synchronization job {JobId} failed for company {CompanyId}.", job.Id, job.CompanyId);
            await RecordFailureAsync(job, "synchronization_failed", "Repository synchronization could not complete.", true, cancellationToken);
        }
    }

    private async Task ApplyDeltaPageAsync(CompanyDocumentRepositorySynchronizationJob job, GraphRepositoryDeltaPage page,
        GraphRepositoryContext context, CancellationToken cancellationToken)
    {
        var changes = page.Changes.GroupBy(x => x.ItemId, StringComparer.Ordinal).Select(x => x.Last()).ToArray();
        var changed = 0; var removed = 0; var failed = 0;
        foreach (var change in changes)
        {
            if (change.IsDeleted) { removed += await InvalidateItemAndDescendantsAsync(job, change.ItemId, "remote_deleted", cancellationToken); continue; }
            var availability = await graph.ValidateItemAsync(context, change.ItemId, cancellationToken);
            if (!availability.IsAvailable) { removed += await InvalidateItemAndDescendantsAsync(job, change.ItemId, "outside_approved_root", cancellationToken); continue; }
            await ObserveTrackedItemAsync(job, change, cancellationToken);
            if (!change.IsFolder)
            {
                try { if (await SynchronizeFileAsync(job, change, context, cancellationToken)) changed++; }
                catch (Exception ex) { failed++; ItemFailures.Add(1); logger.LogWarning(ex, "Repository item {ItemId} failed during synchronization job {JobId}.", change.ItemId, job.Id); }
            }
        }
        job.RecordPage(page.NextCursor, page.DeltaCursor, changes.Length, changed, removed, failed, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RunFullReconciliationAsync(CompanyDocumentRepositorySynchronizationJob job, GraphRepositoryContext context, CancellationToken cancellationToken)
    {
        var files = await graph.EnumerateFilesAsync(context, cancellationToken);
        var changed = 0; var failed = 0;
        foreach (var file in files)
        {
            var change = new GraphRepositoryChange(file.ItemId, null, file.Name, false, false, file.SizeBytes, file.RemoteVersion, file.WebUrl, file.ContentType, file.LastModifiedUtc);
            await ObserveTrackedItemAsync(job, change, cancellationToken);
            try { if (await SynchronizeFileAsync(job, change, context, cancellationToken)) changed++; }
            catch (Exception ex) { failed++; ItemFailures.Add(1); logger.LogWarning(ex, "Repository item {ItemId} failed during reconciliation job {JobId}.", file.ItemId, job.Id); }
        }
        if (failed > 0) throw new DocumentRepositoryUnavailableException("reconciliation_item_failures", "The complete reconciliation contained item failures; unseen documents were not removed.");
        var removed = await InvalidateUnseenAsync(job, cancellationToken);
        job.RecordPage(null, null, files.Count, changed, removed, 0, clock.GetUtcNow().UtcDateTime);
        job.Connection.RecordSynchronization(DocumentRepositorySynchronizationModes.FullReconciliation, null, clock.GetUtcNow().UtcDateTime);
        job.Complete(clock.GetUtcNow().UtcDateTime);
    }

    private async Task<bool> SynchronizeFileAsync(CompanyDocumentRepositorySynchronizationJob job, GraphRepositoryChange file,
        GraphRepositoryContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file.RemoteVersion)) throw new DocumentRepositoryUnavailableException("missing_remote_version", "The provider did not return a stable document version.");
        if (!CompanyDocumentFileRules.TryValidate(file.Name, file.ContentType, out var validation)) throw new DocumentRepositoryUnavailableException(validation!.Code, validation.Message);
        if (file.SizeBytes <= 0 || file.SizeBytes > documentOptions.Value.MaxUploadBytes) throw new DocumentRepositoryUnavailableException("file_too_large", "The document is empty or exceeds the configured synchronization size limit.");
        if (!Uri.TryCreate(file.WebUrl, UriKind.Absolute, out var webUri) || webUri.Scheme != Uri.UriSchemeHttps) throw new DocumentRepositoryUnavailableException("invalid_source_link", "The provider did not return a safe permanent source link.");

        var source = await db.CompanyKnowledgeDocumentRemoteSources.IgnoreQueryFilters().Include(x => x.Document)
            .SingleOrDefaultAsync(x => x.CompanyId == job.CompanyId && x.ConnectionId == job.ConnectionId && x.DriveId == job.Connection.DriveId && x.ItemId == file.ItemId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (source is not null && string.Equals(source.RemoteVersion, file.RemoteVersion, StringComparison.Ordinal) &&
            source.Document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
            source.Document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed)
        {
            source.ObserveVersion(file.RemoteVersion, webUri.ToString(), now); source.Document.UpdateRepositoryMetadata(Path.GetFileNameWithoutExtension(file.Name), webUri.ToString()); await db.SaveChangesAsync(cancellationToken); return false;
        }
        if (source is not null)
        {
            source.ObserveVersion(file.RemoteVersion, webUri.ToString(), now);
            var active = await db.CompanyKnowledgeChunks.IgnoreQueryFilters().Where(x => x.CompanyId == job.CompanyId && x.DocumentId == source.DocumentId && x.IsActive).ToListAsync(cancellationToken);
            foreach (var chunk in active) chunk.Deactivate();
            await db.SaveChangesAsync(cancellationToken); // stale evidence is hidden before downloading its replacement
        }

        await using var content = new MemoryStream((int)Math.Min(file.SizeBytes, int.MaxValue));
        await graph.CopyContentAsync(context, file.ItemId, content, documentOptions.Value.MaxUploadBytes, cancellationToken); content.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken)).ToLowerInvariant(); content.Position = 0;
        var documentId = source?.DocumentId ?? Guid.NewGuid(); var name = Path.GetFileName(file.Name);
        var storageKey = $"companies/{job.CompanyId:N}/documents/{documentId:N}/versions/{hash[..16]}/{name}";
        var stored = await storage.WriteAsync(new DocumentStorageWriteRequest(job.CompanyId, documentId, storageKey, name, file.ContentType, content), cancellationToken);
        if (source is null)
        {
            var document = CompanyKnowledgeDocument.CreateRepositoryDocument(documentId, job.CompanyId, Path.GetFileNameWithoutExtension(name), CompanyKnowledgeDocumentType.Reference,
                stored.StorageKey, stored.StorageUrl, name, file.ContentType, Path.GetExtension(name).ToLowerInvariant(), file.SizeBytes,
                new CompanyKnowledgeDocumentAccessScope(job.CompanyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility), webUri.ToString());
            source = new CompanyKnowledgeDocumentRemoteSource(job.CompanyId, job.ConnectionId, documentId, job.Connection.DriveId, file.ItemId, file.RemoteVersion, webUri.ToString(), hash, now);
            source.ObserveVersion(file.RemoteVersion, webUri.ToString(), now);
            db.CompanyKnowledgeDocuments.Add(document); db.CompanyKnowledgeDocumentRemoteSources.Add(source);
        }
        else source.Document.PrepareRepositoryReplacement(Path.GetFileNameWithoutExtension(name), stored.StorageKey, stored.StorageUrl, name, file.ContentType, file.SizeBytes, webUri.ToString());
        await db.SaveChangesAsync(cancellationToken);

        await ingestion.ProcessAsync(job.CompanyId, documentId, true, cancellationToken);
        var state = await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == job.CompanyId && x.Id == documentId, cancellationToken);
        if (state.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.ScanClean)
        {
            try { await indexing.IndexDocumentAsync(job.CompanyId, documentId, cancellationToken); }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                await ingestionStatus.MarkFailedAsync(job.CompanyId, documentId, new CompanyDocumentIngestionFailure("indexing_failed", "The replacement could not be indexed.", "Restore the indexing dependency and retry synchronization.", CanRetry: true), cancellationToken);
                throw new DocumentRepositoryUnavailableException("indexing_failed", "The replacement could not be indexed.") { Source = ex.Source };
            }
        }
        state = await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == job.CompanyId && x.Id == documentId, cancellationToken);
        if (state.IngestionStatus != CompanyKnowledgeDocumentIngestionStatus.Processed || state.IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Indexed) throw new DocumentRepositoryUnavailableException("ingestion_failed", "The replacement did not complete scanning and indexing.");
        source = await db.CompanyKnowledgeDocumentRemoteSources.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == job.CompanyId && x.DocumentId == documentId, cancellationToken);
        if (!string.Equals(source.ObservedRemoteVersion, file.RemoteVersion, StringComparison.Ordinal)) throw new DocumentRepositoryUnavailableException("superseded_version", "A newer remote document version superseded this synchronization attempt.");
        source.CompleteVersion(file.RemoteVersion, hash, webUri.ToString(), clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(cancellationToken); return true;
    }

    private async Task ObserveTrackedItemAsync(CompanyDocumentRepositorySynchronizationJob job, GraphRepositoryChange change, CancellationToken cancellationToken)
    {
        var item = await db.CompanyDocumentRepositoryTrackedItems.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == job.CompanyId && x.ConnectionId == job.ConnectionId && x.DriveId == job.Connection.DriveId && x.ItemId == change.ItemId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (item is null) db.CompanyDocumentRepositoryTrackedItems.Add(new CompanyDocumentRepositoryTrackedItem(job.CompanyId, job.ConnectionId, job.Connection.DriveId, change.ItemId, change.ParentItemId, change.Name, change.IsFolder, change.RemoteVersion, job.ReconciliationGeneration, now));
        else item.Observe(change.ParentItemId, change.Name, change.IsFolder, change.RemoteVersion, job.ReconciliationGeneration, now);
    }

    private async Task<int> InvalidateItemAndDescendantsAsync(CompanyDocumentRepositorySynchronizationJob job, string itemId, string reason, CancellationToken cancellationToken)
    {
        var items = await db.CompanyDocumentRepositoryTrackedItems.IgnoreQueryFilters().Where(x => x.CompanyId == job.CompanyId && x.ConnectionId == job.ConnectionId).ToListAsync(cancellationToken);
        var ids = new HashSet<string>(StringComparer.Ordinal) { itemId };
        var changed = true; while (changed) { changed = false; foreach (var item in items.Where(x => x.ParentItemId is not null && ids.Contains(x.ParentItemId))) changed |= ids.Add(item.ItemId); }
        var now = clock.GetUtcNow().UtcDateTime; foreach (var item in items.Where(x => ids.Contains(x.ItemId))) item.MarkUnavailable(reason, now);
        return await InvalidateSourcesAsync(job.CompanyId, job.ConnectionId, ids, reason, now, cancellationToken);
    }

    private async Task<int> InvalidateUnseenAsync(CompanyDocumentRepositorySynchronizationJob job, CancellationToken cancellationToken)
    {
        var unseen = await db.CompanyDocumentRepositoryTrackedItems.IgnoreQueryFilters().Where(x => x.CompanyId == job.CompanyId && x.ConnectionId == job.ConnectionId && x.LastSeenGeneration != job.ReconciliationGeneration && x.IsAvailable).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime; foreach (var item in unseen) item.MarkUnavailable("not_seen_in_complete_reconciliation", now);
        return await InvalidateSourcesAsync(job.CompanyId, job.ConnectionId, unseen.Select(x => x.ItemId).ToHashSet(StringComparer.Ordinal), "not_seen_in_complete_reconciliation", now, cancellationToken);
    }

    private async Task<int> InvalidateSourcesAsync(Guid companyId, Guid connectionId, HashSet<string> itemIds, string reason, DateTime now, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0) return 0;
        var sources = await db.CompanyKnowledgeDocumentRemoteSources.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.ConnectionId == connectionId && itemIds.Contains(x.ItemId)).ToListAsync(cancellationToken);
        foreach (var source in sources)
        {
            source.MarkUnavailable(reason, now);
            var chunks = await db.CompanyKnowledgeChunks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.DocumentId == source.DocumentId && x.IsActive).ToListAsync(cancellationToken);
            foreach (var chunk in chunks) chunk.Deactivate();
        }
        await db.SaveChangesAsync(cancellationToken); return sources.Count;
    }

    private async Task ScheduleDueConnectionsAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddMinutes(-Math.Max(1, _options.SynchronizationIntervalMinutes));
        var connections = await db.CompanyDocumentRepositoryConnections.IgnoreQueryFilters().Where(x => x.LifecycleState == DocumentRepositoryLifecycleStates.Active && (x.LastSynchronizedUtc == null || x.LastSynchronizedUtc <= cutoff)).Take(20).ToListAsync(cancellationToken);
        connections = connections.Where(x => !x.SynchronizationPausedUtc.HasValue).ToList();
        foreach (var connection in connections)
        {
            var active = await db.CompanyDocumentRepositorySynchronizationJobs.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == connection.CompanyId && x.ConnectionId == connection.Id && (x.Status == DocumentRepositorySynchronizationStates.Queued || x.Status == DocumentRepositorySynchronizationStates.Running || x.Status == DocumentRepositorySynchronizationStates.RetryWaiting), cancellationToken);
            if (!active) db.CompanyDocumentRepositorySynchronizationJobs.Add(CreateJob(connection, $"scheduled:{clock.GetUtcNow():yyyyMMddHHmm}", false, clock.GetUtcNow().UtcDateTime));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private CompanyDocumentRepositorySynchronizationJob CreateJob(CompanyDocumentRepositoryConnection connection, string key, bool force, DateTime now)
    {
        var job = new CompanyDocumentRepositorySynchronizationJob(connection.CompanyId, connection.Id, key, force, correlation.CorrelationId ?? Guid.NewGuid().ToString("N"), now);
        if (!force) job.StartFrom(connection.SynchronizationCursor, now);
        return job;
    }

    private async Task RecordFailureAsync(CompanyDocumentRepositorySynchronizationJob job, string code, string message, bool retryable, CancellationToken cancellationToken, TimeSpan? providerRetryAfter = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (retryable && job.AttemptCount < Math.Max(1, _options.MaxAttempts))
        {
            var seconds = Math.Max(providerRetryAfter?.TotalSeconds ?? 0, Math.Min(900, Math.Max(1, _options.InitialRetrySeconds) * Math.Pow(2, Math.Max(0, job.AttemptCount - 1))));
            job.Retry(code, message, now.AddSeconds(seconds), now);
        }
        else job.Fail(code, message, now);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(job, AuditEventActions.DocumentRepositorySynchronizationFailed, AuditEventOutcomes.Failed, message, cancellationToken);
    }

    private Task WriteAuditAsync(CompanyDocumentRepositorySynchronizationJob job, string action, string outcome, string summary, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEventWriteRequest(job.CompanyId, AuditActorTypes.System, companyContext.UserId, action, AuditTargetTypes.DocumentRepositoryConnection, job.ConnectionId.ToString("D"), outcome, summary, ["microsoft_graph", "document_repository"], new Dictionary<string, string?> { ["jobId"] = job.Id.ToString("D"), ["mode"] = job.Mode, ["attemptCount"] = job.AttemptCount.ToString() }, job.CorrelationId), cancellationToken);

    private void EnsureCompany(Guid companyId) { if (companyContext.CompanyId != companyId) throw new UnauthorizedAccessException("Repository synchronization is scoped to the active company."); }
    private static void EnsureActive(CompanyDocumentRepositoryConnection connection) { if (connection.LifecycleState != DocumentRepositoryLifecycleStates.Active) throw new DocumentRepositoryUnavailableException("connection_not_active", "Validate the repository connection before synchronizing it."); }
    private static bool IsRetryable(string code) => code is DocumentRepositoryValidationCodes.Throttled or DocumentRepositoryValidationCodes.Unavailable or "synchronization_failed" or "reconciliation_item_failures";
    private static string ValidateKey(string value) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200 ? throw new DocumentRepositoryValidationException("IdempotencyKey is required and must be 200 characters or fewer.") : value.Trim();
    private static GraphRepositoryContext ToContext(CompanyDocumentRepositoryConnection x) => new(x.ProviderKind, x.CredentialMode, x.DirectoryTenantId, x.ApplicationClientId, x.CredentialReference, x.DriveId, x.RootItemId);
    private static DocumentRepositorySynchronizationJobDto Map(CompanyDocumentRepositorySynchronizationJob x) => new(x.Id, x.ConnectionId, x.Status, x.Mode, x.AttemptCount, x.ObservedCount, x.ChangedCount, x.RemovedCount, x.FailedCount, x.NextRetryUtc, x.FailureCode, x.FailureMessage, x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc);
}

internal sealed class CompanyDocumentRepositorySynchronizationBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<DocumentRepositorySynchronizationOptions> options,
    ILogger<CompanyDocumentRepositorySynchronizationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) { logger.LogInformation("Document repository synchronization is disabled."); return; }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds)));
        do
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ICompanyDocumentRepositorySynchronizationService>().ProcessPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Document repository synchronization polling failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
