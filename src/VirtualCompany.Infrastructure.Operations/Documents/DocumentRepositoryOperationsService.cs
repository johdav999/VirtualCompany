using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class DocumentRepositoryOperationsOptions
{
    public const string SectionName = "DocumentRepositoryOperations";
    public bool Enabled { get; set; } = true;
    public bool RetentionEnabled { get; set; } = true;
    public int CleanupIntervalMinutes { get; set; } = 360;
    public int AbandonedStagedArtifactDays { get; set; } = 30;
    public int TerminalArtifactDays { get; set; } = 90;
    public int SupersededChunkDays { get; set; } = 30;
    public int CleanupBatchSize { get; set; } = 100;
}

internal interface IDocumentRepositoryRetentionService
{
    Task RunOnceAsync(CancellationToken cancellationToken);
}

internal sealed class DocumentRepositoryRetentionService(
    VirtualCompanyDbContext db,
    ICompanyDocumentStorage storage,
    IAuditEventWriter audit,
    IOptions<DocumentRepositoryOperationsOptions> configured,
    TimeProvider clock,
    ILogger<DocumentRepositoryRetentionService> logger) : IDocumentRepositoryRetentionService
{
    private static readonly Meter Meter = new("VirtualCompany.DocumentRepositories");
    private static readonly Counter<long> PurgedArtifacts = Meter.CreateCounter<long>("document_repository.retention.artifacts_purged");
    private static readonly Counter<long> PurgedChunks = Meter.CreateCounter<long>("document_repository.retention.chunks_purged");

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        if (!options.RetentionEnabled) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var stagedCutoff = now.AddDays(-Math.Max(1, options.AbandonedStagedArtifactDays));
        var terminalCutoff = now.AddDays(-Math.Max(1, options.TerminalArtifactDays));
        var chunkCutoff = now.AddDays(-Math.Max(1, options.SupersededChunkDays));
        var batch = Math.Clamp(options.CleanupBatchSize, 1, 1000);

        var publications = await db.CompanyDocumentPublicationRequests.IgnoreQueryFilters()
            .Where(x => x.LocalArtifactsPurgedUtc == null &&
                ((x.Status == DocumentPublicationStatuses.Staged && x.CreatedUtc < stagedCutoff) ||
                 ((x.Status == DocumentPublicationStatuses.Delivered || x.Status == DocumentPublicationStatuses.Failed) && x.UpdatedUtc < terminalCutoff)))
            .OrderBy(x => x.UpdatedUtc).Take(batch).ToListAsync(cancellationToken);

        var purgedByCompany = new Dictionary<Guid, int>();
        foreach (var publication in publications)
        {
            try
            {
                await storage.DeleteAsync(publication.StorageKey, cancellationToken);
                if (!string.IsNullOrWhiteSpace(publication.OriginalStorageKey))
                    await storage.DeleteAsync(publication.OriginalStorageKey, cancellationToken);
                publication.MarkLocalArtifactsPurged(now);
                purgedByCompany[publication.CompanyId] = purgedByCompany.GetValueOrDefault(publication.CompanyId) + 1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Local document publication artifact cleanup failed for request {PublicationRequestId}.", publication.Id);
            }
        }

        var chunks = await db.CompanyKnowledgeChunks.IgnoreQueryFilters()
            .Where(x => !x.IsActive && x.CreatedUtc < chunkCutoff && x.ChunkSetVersion < x.Document.CurrentChunkSetVersion)
            .OrderBy(x => x.CreatedUtc).Take(batch).ToListAsync(cancellationToken);
        db.CompanyKnowledgeChunks.RemoveRange(chunks);
        await db.SaveChangesAsync(cancellationToken);

        PurgedArtifacts.Add(publications.Count(x => x.LocalArtifactsPurgedUtc.HasValue));
        PurgedChunks.Add(chunks.Count);
        foreach (var entry in purgedByCompany)
            await audit.WriteAsync(new AuditEventWriteRequest(entry.Key, AuditActorTypes.System, null,
                AuditEventActions.DocumentRepositoryRetentionCompleted, AuditTargetTypes.Company,
                entry.Key.ToString("D"), AuditEventOutcomes.Succeeded,
                "Completed bounded local document repository retention without changing remote content.",
                ["document_repository", "retention"],
                new Dictionary<string, string?> { ["artifactCount"] = entry.Value.ToString(), ["chunkCount"] = chunks.Count(x => x.CompanyId == entry.Key).ToString() }), cancellationToken);
    }
}

internal sealed class DocumentRepositoryRetentionBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<DocumentRepositoryOperationsOptions> configured,
    ILogger<DocumentRepositoryRetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configured.Value.RetentionEnabled) { logger.LogInformation("Document repository retention is disabled."); return; }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, configured.Value.CleanupIntervalMinutes)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IDocumentRepositoryRetentionService>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Document repository retention run failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
