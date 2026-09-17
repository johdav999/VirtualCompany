using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationLegacyMigrationService(VirtualCompanyDbContext db,TimeProvider timeProvider,
    ILogger<SalesPresentationLegacyMigrationService> logger):ISalesPresentationLegacyMigrationService
{
    private static readonly Meter Meter=new("VirtualCompany.Sales.PresentationLegacyMigration","1.0.0");
    private static readonly Counter<long> PresetBacked=Meter.CreateCounter<long>("sales.presentation.legacy_migration.preset_backed");
    private static readonly Counter<long> CompatibilityOnly=Meter.CreateCounter<long>("sales.presentation.legacy_migration.compatibility_only");
    private static readonly Counter<long> Failures=Meter.CreateCounter<long>("sales.presentation.legacy_migration.failures");
    private static readonly Histogram<double> Latency=Meter.CreateHistogram<double>("sales.presentation.legacy_migration.latency","ms");

    public async Task<SalesPresentationLegacyReconciliationDto> ReconcileAsync(Guid companyId,Guid actorUserId,int batchSize,string? correlationId,CancellationToken ct)
    {
        var started=Stopwatch.GetTimestamp();await Member(companyId,actorUserId,ct);batchSize=Math.Clamp(batchSize,1,200);
        var held=await db.SalesPresentationLegacyCompatibilityRecords.AsNoTracking().Where(x=>x.Status=="completed"||x.Status=="failed").Select(x=>x.DeckId).ToListAsync(ct);
        var decks=await db.SalesPresentationDecks.AsNoTracking().Where(x=>x.CompanyId==companyId&&!held.Contains(x.Id)).OrderBy(x=>x.CreatedUtc).Take(batchSize).ToListAsync(ct);
        var outcomes=new List<SalesPresentationLegacyCompatibilityDto>();var presetBacked=0;var compatibilityOnly=0;var failed=0;
        foreach(var deck in decks)
        {
            var record=await db.SalesPresentationLegacyCompatibilityRecords.SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.DeckId==deck.Id,ct)??new SalesPresentationLegacyCompatibilityRecord(Guid.NewGuid(),companyId,deck.Id,deck.SessionId,deck.ContentHash,Now());
            if(db.Entry(record).State==EntityState.Detached)db.Add(record);
            try
            {
                if(deck.PresentationRunId.HasValue&&deck.PresetAssetId.HasValue){record.Complete("preset_backed","legacy.already_preset_backed","The deck already points to a presentation run and reusable preset asset; compatibility reads remain available.",deck.PresentationRunId,Now());presetBacked++;PresetBacked.Add(1);}
                else{record.Complete("compatibility_only","legacy.contextual_ownership_ambiguous","The session-owned deck is preserved through the compatibility projection. It was not promoted to reusable content because historical customer-specific ownership cannot be proven safe.",null,Now());compatibilityOnly++;CompatibilityOnly.Add(1);}
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(),companyId,AuditActorTypes.User,actorUserId,"sales.presentation_legacy.reconciled","sales_presentation_deck",deck.Id.ToString("D"),AuditEventOutcomes.Succeeded,record.Summary!,["legacy presentation","compatibility"],new Dictionary<string,string?>{{"disposition",record.Disposition},{"presentationRunId",record.PresentationRunId?.ToString("D")},{"contentHash",record.SourceContentHash}},correlationId,Now()));
                await db.SaveChangesAsync(ct);outcomes.Add(Map(record));
            }
            catch(Exception ex)when(ex is not OperationCanceledException)
            {
                failed++;record.Fail("legacy.reconciliation_failed","The legacy presentation could not be classified safely. Retry after reviewing the retained deck and relationships.",Now());await db.SaveChangesAsync(CancellationToken.None);Failures.Add(1);logger.LogWarning(ex,"Legacy presentation reconciliation failed. CompanyId={CompanyId} DeckId={DeckId}",companyId,deck.Id);outcomes.Add(Map(record));
            }
        }
        var processedDeckIds=decks.Select(x=>x.Id).ToArray();
        var hasMore=await db.SalesPresentationDecks.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&!held.Contains(x.Id)&&!processedDeckIds.Contains(x.Id),ct);
        Latency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);return new(decks.Count,presetBacked,compatibilityOnly,failed,hasMore,outcomes);
    }
    public async Task<SalesPresentationLegacyReconciliationDto> GetStatusAsync(Guid companyId,Guid actorUserId,CancellationToken ct){await Member(companyId,actorUserId,ct);var items=await db.SalesPresentationLegacyCompatibilityRecords.AsNoTracking().OrderByDescending(x=>x.UpdatedUtc).Take(200).ToListAsync(ct);var totalDecks=await db.SalesPresentationDecks.AsNoTracking().CountAsync(ct);return new(items.Count,items.Count(x=>x.Disposition=="preset_backed"),items.Count(x=>x.Disposition=="compatibility_only"),items.Count(x=>x.Status=="failed"),items.Count<totalDecks,items.Select(Map).ToArray());}
    public async Task<bool> RetryAsync(Guid companyId,Guid actorUserId,Guid recordId,CancellationToken ct){await Member(companyId,actorUserId,ct);var record=await db.SalesPresentationLegacyCompatibilityRecords.SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==recordId,ct);if(record is null)return false;record.QueueRetry(Now());await db.SaveChangesAsync(ct);return true;}
    private async Task Member(Guid companyId,Guid actor,CancellationToken ct){if(companyId==Guid.Empty||actor==Guid.Empty||!await db.CompanyMemberships.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.UserId==actor&&x.Status==CompanyMembershipStatus.Active,ct))throw new UnauthorizedAccessException("An active company membership is required.");}
    private static SalesPresentationLegacyCompatibilityDto Map(SalesPresentationLegacyCompatibilityRecord x)=>new(x.Id,x.DeckId,x.SessionId,x.PresentationRunId,x.Status,x.Disposition,x.ReasonCode,x.Summary,x.AttemptCount,x.UpdatedUtc);private DateTime Now()=>timeProvider.GetUtcNow().UtcDateTime;
}
