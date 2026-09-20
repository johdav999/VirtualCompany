using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class DocumentRepositoryImportOptions
{
    public const string SectionName = "DocumentRepositoryImports";
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 10;
}

internal sealed class CompanyDocumentRepositoryImportService(
    VirtualCompanyDbContext db, IDocumentRepositoryGraphAdapter graph, ICompanyDocumentStorage storage,
    ITrustedCompanyDocumentIngestionService ingestion, ICompanyKnowledgeIndexingProcessor indexing,
    ICompanyDocumentIngestionStatusService ingestionStatus, ICompanyContextAccessor companyContext,
    ICorrelationContextAccessor correlation, IOptions<CompanyDocumentOptions> documentOptions,
    IOptions<DocumentRepositoryOperationsOptions> operationsOptions,
    IAuditEventWriter audit, TimeProvider timeProvider, ILogger<CompanyDocumentRepositoryImportService> logger) : ICompanyDocumentRepositoryImportService
{
    public async Task<DocumentRepositoryImportJobDto> StartAsync(Guid companyId, Guid connectionId, StartDocumentRepositoryImportCommand command, CancellationToken ct)
    {
        if (companyContext.CompanyId != companyId) throw new UnauthorizedAccessException("Repository imports are scoped to the active company.");
        if (!operationsOptions.Value.Enabled) throw new DocumentRepositoryUnavailableException("feature_disabled", "Document repository operations are disabled for this deployment.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 200) throw new DocumentRepositoryValidationException("IdempotencyKey is required and must be 200 characters or fewer.");
        var connection = await db.CompanyDocumentRepositoryConnections.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, ct) ?? throw new DocumentRepositoryNotFoundException();
        if (connection.LifecycleState != DocumentRepositoryLifecycleStates.Active) throw new DocumentRepositoryUnavailableException("connection_not_active", "Validate the repository connection before starting an import.");
        if (connection.SynchronizationPausedUtc.HasValue) throw new DocumentRepositoryUnavailableException("synchronization_paused", "Repository synchronization is paused by an administrator.");
        var existing = await db.CompanyDocumentRepositoryImportJobs.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId && x.IdempotencyKey == command.IdempotencyKey.Trim(), ct);
        if (existing is not null) return Map(existing);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var job = new CompanyDocumentRepositoryImportJob(companyId, connectionId, command.IdempotencyKey.Trim(), correlation.CorrelationId ?? Guid.NewGuid().ToString("N"), now);
        db.CompanyDocumentRepositoryImportJobs.Add(job); await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, companyContext.UserId, AuditEventActions.DocumentRepositoryImportQueued, AuditTargetTypes.DocumentRepositoryConnection, connectionId.ToString("D"), AuditEventOutcomes.Succeeded, "Queued a bounded Microsoft 365 repository import.", ["microsoft_graph", "document_repository"], new Dictionary<string, string?> { ["jobId"] = job.Id.ToString("D"), ["idempotencyKey"] = job.IdempotencyKey }, job.CorrelationId), ct);
        return Map(job);
    }

    public async Task<DocumentRepositoryImportJobDto?> GetAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken ct)
    {
        if (companyContext.CompanyId != companyId) throw new UnauthorizedAccessException("Repository imports are scoped to the active company.");
        var job = await db.CompanyDocumentRepositoryImportJobs.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId && x.Id == jobId, ct);
        return job is null ? null : Map(job);
    }

    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        if (!operationsOptions.Value.Enabled) return;
        var job = await db.CompanyDocumentRepositoryImportJobs.IgnoreQueryFilters().Include(x => x.Connection).OrderBy(x => x.CreatedUtc)
            .FirstOrDefaultAsync(x => x.Status == DocumentRepositoryImportStates.Queued && x.Connection.SynchronizationPausedUtc == null, ct);
        if (job is null) return;
        if (job.Connection.SynchronizationPausedUtc.HasValue) return;
        var now = timeProvider.GetUtcNow().UtcDateTime; job.Start(now); await db.SaveChangesAsync(ct);
        if (job.Connection.LifecycleState != DocumentRepositoryLifecycleStates.Active) { job.Fail("connection_not_active", "The repository connection is not active.", now); await db.SaveChangesAsync(ct); return; }
        try
        {
            var context = new GraphRepositoryContext(job.Connection.ProviderKind, job.Connection.DirectoryTenantId, job.Connection.ApplicationClientId, job.Connection.CredentialReference, job.Connection.DriveId, job.Connection.RootItemId);
            var files = await graph.EnumerateFilesAsync(context, ct); job.SetDiscoveredCount(files.Count, now);
            foreach (var file in files) db.CompanyDocumentRepositoryImportItems.Add(new CompanyDocumentRepositoryImportItem(job.CompanyId, job.Id, job.Connection.DriveId, file.ItemId, file.Name, file.RemoteVersion, file.WebUrl, file.ContentType, file.SizeBytes, file.LastModifiedUtc, now));
            await db.SaveChangesAsync(ct);
            foreach (var item in await db.CompanyDocumentRepositoryImportItems.IgnoreQueryFilters().Where(x => x.CompanyId == job.CompanyId && x.JobId == job.Id).OrderBy(x => x.Name).ToListAsync(ct)) await ProcessItemAsync(job, item, context, ct);
            job.Complete(timeProvider.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEventWriteRequest(job.CompanyId, AuditActorTypes.System, null, AuditEventActions.DocumentRepositoryImportCompleted, AuditTargetTypes.DocumentRepositoryConnection, job.ConnectionId.ToString("D"), job.FailedCount == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed, "Completed a bounded Microsoft 365 repository import.", ["microsoft_graph", "document_repository"], new Dictionary<string, string?> { ["jobId"] = job.Id.ToString("D"), ["processedCount"] = job.ProcessedCount.ToString(), ["failedCount"] = job.FailedCount.ToString() }, job.CorrelationId), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repository import job {JobId} failed for company {CompanyId}.", job.Id, job.CompanyId);
            job.Fail("repository_import_failed", "The repository could not be enumerated. Retry when the connection is available.", timeProvider.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessItemAsync(CompanyDocumentRepositoryImportJob job, CompanyDocumentRepositoryImportItem item, GraphRepositoryContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        string? uncommittedStorageKey = null;
        try
        {
            if (!CompanyDocumentFileRules.TryValidate(item.Name, item.ContentType, out var failure)) { item.Fail(failure!.Code, failure.Message, false, now); job.RecordFailed(now); await db.SaveChangesAsync(ct); return; }
            if (item.SizeBytes <= 0 || item.SizeBytes > documentOptions.Value.MaxUploadBytes) { item.Fail("file_too_large", "The document is empty or exceeds the configured import size limit.", false, now); job.RecordFailed(now); await db.SaveChangesAsync(ct); return; }
            if (!Uri.TryCreate(item.SourceWebUrl, UriKind.Absolute, out var webUri) || webUri.Scheme != Uri.UriSchemeHttps) { item.Fail("invalid_source_link", "The provider did not return a safe permanent source link.", true, now); job.RecordFailed(now); await db.SaveChangesAsync(ct); return; }
            var existing = await db.CompanyKnowledgeDocumentRemoteSources.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == job.CompanyId && x.ConnectionId == job.ConnectionId && x.DriveId == item.DriveId && x.ItemId == item.ItemId, ct);
            if (existing is not null) { item.Skip(existing.DocumentId, now); job.RecordProcessed(now); await db.SaveChangesAsync(ct); return; }
            await using var content = new MemoryStream((int)item.SizeBytes);
            await graph.CopyContentAsync(context, item.ItemId, content, documentOptions.Value.MaxUploadBytes, ct); content.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant(); content.Position = 0;
            var documentId = Guid.NewGuid(); var fileName = Path.GetFileName(item.Name); var storageKey = $"companies/{job.CompanyId:N}/documents/{documentId:N}/{fileName}";
            var stored = await storage.WriteAsync(new DocumentStorageWriteRequest(job.CompanyId, documentId, storageKey, fileName, item.ContentType, content), ct);
            uncommittedStorageKey = stored.StorageKey;
            var document = CompanyKnowledgeDocument.CreateRepositoryDocument(documentId, job.CompanyId, Path.GetFileNameWithoutExtension(fileName), CompanyKnowledgeDocumentType.Reference, stored.StorageKey, stored.StorageUrl, fileName, item.ContentType, Path.GetExtension(fileName).ToLowerInvariant(), content.Length, new CompanyKnowledgeDocumentAccessScope(job.CompanyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility), webUri.ToString());
            db.CompanyKnowledgeDocuments.Add(document); db.CompanyKnowledgeDocumentRemoteSources.Add(new CompanyKnowledgeDocumentRemoteSource(job.CompanyId, job.ConnectionId, documentId, item.DriveId, item.ItemId, item.RemoteVersion, webUri.ToString(), hash, now)); await db.SaveChangesAsync(ct); uncommittedStorageKey = null;
            await ingestion.ProcessAsync(job.CompanyId, documentId, true, ct);
            var state = await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == job.CompanyId && x.Id == documentId, ct);
            if (state.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.ScanClean)
            {
                try { await indexing.IndexDocumentAsync(job.CompanyId, documentId, ct); }
                catch (Exception ex)
                {
                    db.ChangeTracker.Clear();
                    var dependency = ex.Message.Contains("embedding", StringComparison.OrdinalIgnoreCase);
                    await ingestionStatus.MarkFailedAsync(job.CompanyId, documentId, new CompanyDocumentIngestionFailure(dependency ? "embedding_unavailable" : "parser_failed", dependency ? "Production semantic indexing is unavailable." : "The document could not be parsed.", dependency ? "Configure the production embedding provider and retry." : "Re-save the document in a supported format and retry.", CanRetry: dependency), ct);
                }
                state = await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == job.CompanyId && x.Id == documentId, ct);
            }
            if (state.IngestionStatus != CompanyKnowledgeDocumentIngestionStatus.Processed || state.IndexingStatus != CompanyKnowledgeDocumentIndexingStatus.Indexed) { item.Fail(state.FailureCode ?? state.IndexingFailureCode ?? "ingestion_failed", state.FailureMessage ?? state.IndexingFailureMessage ?? "Document ingestion failed.", state.CanRetry, now); job.RecordFailed(now); } else { item.Complete(documentId, now); job.RecordProcessed(now); }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { if (uncommittedStorageKey is not null) { try { await storage.DeleteAsync(uncommittedStorageKey, CancellationToken.None); } catch { } } logger.LogWarning(ex, "Repository import item {ItemId} failed in job {JobId}.", item.Id, job.Id); item.Fail("item_import_failed", "The document could not be imported.", true, now); job.RecordFailed(now); await db.SaveChangesAsync(ct); }
    }

    private static DocumentRepositoryImportJobDto Map(CompanyDocumentRepositoryImportJob x) => new(x.Id, x.ConnectionId, x.Status, x.DiscoveredCount, x.ProcessedCount, x.FailedCount, x.FailureCode, x.FailureMessage, x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc, x.Items.Select(i => new DocumentRepositoryImportItemDto(i.Id, i.DocumentId, i.Name, i.Status, i.FailureCode, i.FailureMessage, i.CanRetry)).ToArray());
}

internal sealed class CompanyDocumentRepositoryImportBackgroundService(IServiceScopeFactory scopes, IOptions<DocumentRepositoryImportOptions> options, ILogger<CompanyDocumentRepositoryImportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) { logger.LogInformation("Document repository imports are disabled."); return; }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds)));
        do { try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ICompanyDocumentRepositoryImportService>().ProcessPendingAsync(stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; } catch (Exception ex) { logger.LogError(ex, "Document repository import polling failed."); } } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
