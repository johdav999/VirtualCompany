using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class ProspectingRunBackgroundService(IServiceScopeFactory scopes, ILogger<ProspectingRunBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatch(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Prospecting run worker failed."); }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task ProcessBatch(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var work = await db.ProspectingRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == LeadGenerationStatuses.Running)
            .OrderBy(x => x.UpdatedUtc).Take(5)
            .Select(x => new { x.CompanyId, x.Id }).ToListAsync(ct);

        var scheduled = await db.ProspectingRuns.IgnoreQueryFilters()
            .Where(x => x.Status == LeadGenerationStatuses.Planned && x.Schedule != null && x.ApprovalId == null)
            .OrderBy(x => x.CreatedUtc).Take(5).ToListAsync(ct);
        foreach (var run in scheduled) run.Start();
        if (scheduled.Count > 0) await db.SaveChangesAsync(ct);
        work.AddRange(scheduled.Select(x => new { x.CompanyId, x.Id }));
        foreach (var item in work)
        {
            using var itemScope = scopes.CreateScope();
            var service = (LeadGenerationService)itemScope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Sales.ILeadGenerationService>();
            await service.ProcessRunAsync(item.CompanyId, item.Id, ct);
        }

        var policies = await db.ProspectSourcePolicies.IgnoreQueryFilters().AsNoTracking().Where(x => x.IsActive).Select(x => new { x.CompanyId, x.RetentionDays }).ToListAsync(ct);
        foreach (var policy in policies)
        {
            var cutoff = DateTime.UtcNow.AddDays(-policy.RetentionDays);
            var stale = await db.ProspectAccounts.IgnoreQueryFilters().Where(x => x.CompanyId == policy.CompanyId && x.LastObservedUtc < cutoff && x.Status != LeadGenerationStatuses.Stale).Take(100).ToListAsync(ct);
            foreach (var account in stale) account.MarkStale();
        }

        var completedRecurring = await db.ProspectingRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == LeadGenerationStatuses.Completed && x.Schedule != null && x.EstimatedCost == 0 && x.CompletedUtc != null)
            .Take(25).ToListAsync(ct);
        foreach (var run in completedRecurring)
        {
            var delay = run.Schedule!.Equals("weekly", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
            if (run.CompletedUtc!.Value + delay > DateTime.UtcNow) continue;
            var exists = await db.ProspectingRuns.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == run.CompanyId && x.Name == run.Name && x.CreatedUtc > run.CompletedUtc, ct);
            if (!exists) db.ProspectingRuns.Add(new ProspectingRun(Guid.NewGuid(), run.CompanyId, run.IdealCustomerProfileId, run.OwnerUserId, run.Name, run.AccountLimit, run.ContactLimit, run.Sources, run.Geography, run.FreshnessDays, 0, run.Schedule));
        }
        await db.SaveChangesAsync(ct);
    }
}
