using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesNarrationOptions
{
    public bool Enabled { get; set; }
    // Configured conservative maximum modality rates, never a commercial price or allowance.
    public decimal InputUsdPerMillion { get; set; }
    public decimal OutputUsdPerMillion { get; set; }
    public decimal MaximumCompanyDailyUsd { get; set; } = 5;
    public string RateVersion { get; set; } = "";
    public bool CanGenerate => Enabled && InputUsdPerMillion > 0 && OutputUsdPerMillion > 0 &&
        MaximumCompanyDailyUsd > 0 && RateVersion.Length is > 0 and <= 200;
    public decimal AttemptReservation => (20000 * InputUsdPerMillion + 1200 * OutputUsdPerMillion) / 1000000;
}

public sealed class SalesNarrationBackgroundService(IServiceScopeFactory scopes, ILogger<SalesNarrationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SalesNarrationWorker>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Narration worker could not complete its bounded batch; durable receipts will be reconciled."); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}

public sealed class SalesNarrationWorker(VirtualCompanyDbContext db, ICompanyContextAccessor context,
    SalesNarrationService service, IApprovedSpeechGateway speech, ICompanyDocumentStorage storage,
    IOptions<SalesNarrationOptions> configured, TimeProvider clock)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var companies = await db.SalesNarrationAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == SalesNarrationAsset.Pending && db.SalesNarrationSegments.IgnoreQueryFilters().Any(s => s.CompanyId == x.CompanyId && s.AssetId == x.Id &&
                db.SalesNarrationRevisions.IgnoreQueryFilters().Any(r => r.CompanyId == s.CompanyId && r.Id == s.RevisionId && r.ApprovedUtc != null && r.RevokedUtc == null && r.RetainUntilUtc > now)) || x.Status == SalesNarrationAsset.Generating ||
                x.Status != SalesNarrationAsset.Deleted && x.UpdatedUtc < now.AddDays(-90))
            .OrderBy(x => x.UpdatedUtc).Select(x => x.CompanyId).Take(20).ToListAsync(ct);
        foreach (var company in companies.Distinct())
        {
            context.SetCompanyId(company);
            db.ChangeTracker.Clear();
            await RecoverAsync(company, now, ct);
            await PurgeAsync(company, now, ct);
            var ids = await db.SalesNarrationAssets.AsNoTracking().Where(x => x.CompanyId == company &&
                x.Status == SalesNarrationAsset.Pending && db.SalesNarrationSegments.Any(s => s.CompanyId == company && s.AssetId == x.Id &&
                    db.SalesNarrationRevisions.Any(r => r.CompanyId == company && r.Id == s.RevisionId && r.ApprovedUtc != null && r.RevokedUtc == null && r.RetainUntilUtc > now))).OrderBy(x => x.UpdatedUtc).Select(x => x.Id).Take(3).ToListAsync(ct);
            foreach (var id in ids) await ProcessAsync(company, id, ct);
        }
    }

    public async Task ProcessAsync(Guid company, Guid assetId, CancellationToken ct)
    {
        context.SetCompanyId(company);
        db.ChangeTracker.Clear();
        var o = configured.Value;
        if (!o.CanGenerate) return;
        var now = clock.GetUtcNow().UtcDateTime;
        SalesNarrationAsset asset;
        SalesNarrationRevision revision;
        SalesNarrationSegment segment;
        SalesNarrationAttempt attempt;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            asset = await db.SalesNarrationAssets.SingleAsync(x => x.CompanyId == company && x.Id == assetId, ct);
            if (asset.Status != SalesNarrationAsset.Pending || asset.AttemptCount >= 3) return;
            var candidates = await (from s in db.SalesNarrationSegments join r in db.SalesNarrationRevisions
                on new { s.CompanyId, Id = s.RevisionId } equals new { r.CompanyId, r.Id }
                where s.CompanyId == company && s.AssetId == assetId && r.ApprovedUtc != null &&
                    r.RevokedUtc == null && r.RetainUntilUtc > now orderby r.CreatedUtc descending select new { s, r }).Take(20).ToListAsync(ct);
            var eligible = new List<(SalesNarrationSegment s, SalesNarrationRevision r)>();
            foreach (var candidate in candidates)
            {
                try
                {
                    await service.EnsureReleasedAsync(candidate.r, ct);
                    if (await db.CompanyMemberships.AnyAsync(x => x.CompanyId == company && x.UserId == candidate.r.ApprovedByUserId &&
                        x.Status == CompanyMembershipStatus.Active, ct)) eligible.Add((candidate.s, candidate.r));
                }
                catch (VirtualCompany.Application.Sales.SalesNarrationException) { }
            }
            if (eligible.Count == 0)
            {
                asset.Status = SalesNarrationAsset.NeedsReview; asset.FailureCode = "source_or_approval_changed";
                asset.Version++; asset.UpdatedUtc = now;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            (segment, revision) = eligible[0];
            var day = now.Date;
            // Reservations are retained for unknown outcomes; retries cannot erase costs.
            var spent = await db.SalesNarrationAttempts.Where(x => x.CompanyId == company && x.StartedUtc >= day)
                .Select(x => x.EstimatedCostUsd).ToListAsync(ct);
            if (spent.Sum(x => x ?? o.AttemptReservation) + o.AttemptReservation > o.MaximumCompanyDailyUsd)
            {
                asset.FailureCode = "daily_cost_limit"; asset.UpdatedUtc = now; asset.Version++;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            asset.Status = SalesNarrationAsset.Generating; asset.LeaseUntilUtc = now.AddMinutes(3);
            asset.AttemptCount++; asset.Version++; asset.UpdatedUtc = now; asset.FailureCode = null;
            attempt = new() { Id = Guid.NewGuid(), CompanyId = company, AssetId = asset.Id,
                ApprovalRevisionId = revision.Id, Number = asset.AttemptCount, StartedUtc = now,
                ReservedTokens = 21200, RateVersion = o.RateVersion, EstimatedCostUsd = o.AttemptReservation };
            db.SalesNarrationAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        // The durable generating receipt is committed before any billable provider operation.
        try
        {
            await service.EnsureReleasedAsync(revision, ct);
            var deck = await db.SalesPresentationDecks.AsNoTracking().SingleAsync(x => x.CompanyId == company && x.Id == revision.DeckId, ct);
            var result = await speech.GenerateAsync(new(company, revision.ApprovedByUserId!.Value, deck.AgentId,
                segment.Script, revision.Language, revision.Voice, revision.ConfigurationVersion,
                $"narration:{company:N}:{asset.Id:N}:{attempt.Number}"), ct);
            attempt.InputTokens = result.InputTokens; attempt.OutputTokens = result.OutputTokens;
            attempt.UsageJson = result.UsageJson.Length <= 8000 ? result.UsageJson : null;
            attempt.ProviderResponseId = result.ProviderResponseId;
            attempt.GeneratedMilliseconds = result.Pcm.Length / 48;
            attempt.EstimatedCostUsd = (result.InputTokens * o.InputUsdPerMillion + result.OutputTokens * o.OutputUsdPerMillion) / 1000000;
            SalesRoomBenchmarkTelemetry.RecordGeneration(result.InputTokens, result.OutputTokens,
                result.Pcm.Length / 48, result.ContentMatches ? "completed" : "rejected");
            if (!result.ContentMatches || result.Model != revision.Model || result.Pcm.Length is < 2 or > 5760000 || result.Pcm.Length % 2 != 0)
            {
                asset.Status = SalesNarrationAsset.Rejected; asset.FailureCode = "script_audio_mismatch";
            }
            else
            {
                // Re-read release after generation; never publish a revoked generation.
                await db.Entry(revision).ReloadAsync(ct);
                await service.EnsureReleasedAsync(revision, ct);
                var wav = SalesNarrationService.Wave(result.Pcm);
                var key = $"companies/{company:N}/sales/narration/{asset.Id:N}/{attempt.Number}.wav";
                // Save the intended object key before writing so crash cleanup can locate orphaned output.
                asset.StorageKey = key;
                await db.SaveChangesAsync(ct);
                using var content = new MemoryStream(wav, false);
                await storage.WriteAsync(new(company, asset.Id, key, "narration.wav", "audio/wav", content), ct);
                asset.AudioHash = SalesNarrationService.Hash(wav); asset.TranscriptHash = SalesNarrationService.Hash(result.Transcript);
                asset.Bytes = wav.Length; asset.DurationMilliseconds = result.Pcm.Length / 48;
                asset.Status = SalesNarrationAsset.Ready; asset.FailureCode = null;
            }
            attempt.Status = asset.Status;
            attempt.CompletedUtc = clock.GetUtcNow().UtcDateTime;
            asset.LeaseUntilUtc = null; asset.UpdatedUtc = attempt.CompletedUtc.Value; asset.Version++;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            SalesRoomBenchmarkTelemetry.RecordGeneration(0, 0, 0, "unknown");
            // No automatic retry after a provider call: unknown billing/output requires explicit review.
            asset.Status = SalesNarrationAsset.NeedsReview; asset.FailureCode = "generation_outcome_requires_review";
            asset.LeaseUntilUtc = null; asset.Version++; asset.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            attempt.Status = SalesNarrationAsset.NeedsReview; attempt.CompletedUtc = asset.UpdatedUtc;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task RecoverAsync(Guid company, DateTime now, CancellationToken ct)
    {
        var stale = await db.SalesNarrationAssets.Where(x => x.CompanyId == company && x.Status == SalesNarrationAsset.Generating &&
            x.LeaseUntilUtc <= now).Take(20).ToListAsync(ct);
        foreach (var asset in stale)
        {
            asset.Status = SalesNarrationAsset.NeedsReview; asset.FailureCode = "worker_outcome_unknown";
            asset.Version++; asset.UpdatedUtc = now; asset.LeaseUntilUtc = null;
            var receipt = await db.SalesNarrationAttempts.SingleAsync(x => x.CompanyId == company && x.AssetId == asset.Id && x.Number == asset.AttemptCount, ct);
            receipt.Status = SalesNarrationAsset.NeedsReview; receipt.CompletedUtc = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task PurgeAsync(Guid company, DateTime now, CancellationToken ct)
    {
        var old = await db.SalesNarrationAssets.Where(x => x.CompanyId == company && x.Status != SalesNarrationAsset.Deleted &&
            x.Status != SalesNarrationAsset.Generating && x.UpdatedUtc < now.AddDays(-90)).Take(20).ToListAsync(ct);
        foreach (var asset in old)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await db.Entry(asset).ReloadAsync(ct);
            if (asset.Status == SalesNarrationAsset.Generating || asset.Status == SalesNarrationAsset.Deleted) continue;
            var activeReference = await (from s in db.SalesNarrationSegments join r in db.SalesNarrationRevisions
                on new { s.CompanyId, Id = s.RevisionId } equals new { r.CompanyId, r.Id }
                where s.CompanyId == company && s.AssetId == asset.Id && r.RevokedUtc == null && r.RetainUntilUtc > now select r.Id).AnyAsync(ct);
            if (activeReference) continue;
            // Persist invalidation before object deletion; repeat deletion safely after a crash.
            asset.Status = SalesNarrationAsset.NeedsReview; asset.FailureCode = "retention_deletion"; asset.Version++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            for (var i = 1; i <= asset.AttemptCount; i++)
                await storage.DeleteAsync($"companies/{company:N}/sales/narration/{asset.Id:N}/{i}.wav", ct);
            asset.Status = SalesNarrationAsset.Deleted; asset.StorageKey = null; asset.Version++; asset.UpdatedUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }
}



