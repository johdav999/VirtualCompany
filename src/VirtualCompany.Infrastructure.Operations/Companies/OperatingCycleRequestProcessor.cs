using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class OperatingCycleRequestProcessor(
    VirtualCompanyDbContext db,
    ICompanyExecutionScopeFactory executionScopes,
    ICompanyOperatingCycleAutomationService cycles,
    ICompanyOperatingReviewAutomationService reviews) : IOperatingCycleRequestProcessor
{
    private readonly string _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    public async Task<OperatingCycleRequestRunResult> RunOnceAsync(int batchSize, CancellationToken ct)
    {
        batchSize = Math.Clamp(batchSize, 1, 20);
        var now = DateTime.UtcNow;
        var ids = await db.OperatingCycleRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == OperatingCycleRequestStatus.Pending || x.Status == OperatingCycleRequestStatus.RetryScheduled ||
                         ((x.Status == OperatingCycleRequestStatus.Claimed || x.Status == OperatingCycleRequestStatus.Processing) && x.LeaseExpiresUtc <= now)) &&
                        x.NotBeforeUtc <= now)
            .OrderBy(x => x.NotBeforeUtc).Select(x => x.Id).Take(batchSize * 2).ToListAsync(ct);
        var claimed = new List<Guid>();
        foreach (var id in ids)
        {
            var row = await db.OperatingCycleRequests.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null || !row.TryClaim(_owner, now, LeaseDuration)) continue;
            try { await db.SaveChangesAsync(ct); claimed.Add(id); if (claimed.Count == batchSize) break; }
            catch (DbUpdateConcurrencyException) { db.Entry(row).State = EntityState.Detached; }
        }

        var completed = 0; var suppressed = 0; var retried = 0; var dead = 0;
        foreach (var id in claimed)
        {
            var status = await ProcessAsync(id, ct);
            if (status == OperatingCycleRequestStatus.Completed) completed++;
            else if (status == OperatingCycleRequestStatus.Suppressed) suppressed++;
            else if (status == OperatingCycleRequestStatus.DeadLettered) dead++;
            else retried++;
        }
        return new(claimed.Count, completed, suppressed, retried, dead);
    }

    private async Task<OperatingCycleRequestStatus> ProcessAsync(Guid id, CancellationToken ct)
    {
        var row = await db.OperatingCycleRequests.IgnoreQueryFilters().Include(x => x.OperatingEvent)
            .SingleAsync(x => x.Id == id, ct);
        var now = DateTime.UtcNow;
        row.Start(_owner, now);
        await db.SaveChangesAsync(ct);
        CompanyOperatingLease? companyLease = null;
        try
        {
            var config = await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == row.CompanyId, ct);
            if (config is null || config.IsPaused || config.EmergencyStopped || !config.CoordinatorAgentId.HasValue)
            {
                row.Suppress("operation_unavailable", "Company operation is paused or has no active coordinator.", now);
                row.OperatingEvent?.Suppress("Company operation is unavailable.");
                await db.SaveChangesAsync(ct); return row.Status;
            }

            companyLease = await db.CompanyOperatingLeases.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == row.CompanyId, ct);
            if (companyLease is null)
            {
                companyLease = new CompanyOperatingLease(Guid.NewGuid(), row.CompanyId);
                db.CompanyOperatingLeases.Add(companyLease);
            }
            if (!companyLease.TryAcquire(_owner, now, LeaseDuration))
            {
                row.Retry("company_lease_busy", "Another company operating cycle currently holds the lease.",
                    now.AddMinutes(1), now);
                await db.SaveChangesAsync(ct); return row.Status;
            }
            await db.SaveChangesAsync(ct);

            using var tenantScope = executionScopes.BeginScope(row.CompanyId);
            if (row.OperatingEvent?.EventType == "task_outcome")
                await reviews.ReviewCommittedWorkAutomaticallyAsync(row.CompanyId, ct);
            var result = await cycles.RunScheduledCycleAsync(row.CompanyId,
                new RequestOperatingCycleCommand(row.TriggerType, row.TriggerReference,
                    row.DeduplicationKey, row.CorrelationId), ct);
            row.Complete(result.Id, DateTime.UtcNow);
            row.OperatingEvent?.MarkProcessed();
            companyLease.Release(_owner, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        catch (CompanyOperatingValidationException ex)
        {
            row.Suppress("cycle_policy_suppressed", string.Join(" ", ex.Errors.SelectMany(x => x.Value)), DateTime.UtcNow);
            row.OperatingEvent?.Suppress("The event did not pass current cycle policy.");
            companyLease?.Release(_owner, DateTime.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            row.Retry("cycle_request_failed", Safe(ex.Message), DateTime.UtcNow.AddMinutes(Math.Min(60, 1 << Math.Min(row.AttemptCount, 5))), DateTime.UtcNow);
            companyLease?.Release(_owner, DateTime.UtcNow);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return row.Status;
    }

    private static string Safe(string? message) => string.IsNullOrWhiteSpace(message) ? "Cycle request failed safely." : message.Trim()[..Math.Min(2000, message.Trim().Length)];
}
