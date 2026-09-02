using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOperatingEventService(
    VirtualCompanyDbContext db,
    ICompanyMembershipContextResolver memberships) : ICompanyOperatingEventService
{
    public async Task<OperatingEventDto> RecordAsync(Guid companyId, RecordOperatingEventCommand command,
        CancellationToken ct)
    {
        var existing = await db.OperatingEvents.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.DeduplicationKey == command.DeduplicationKey, ct);
        if (existing is not null) return Map(existing);
        var materiality = OperatingEventMaterialityValues.Parse(command.Materiality);
        var row = new OperatingEvent(Guid.NewGuid(), companyId, command.EventType, command.SourceType,
            command.SourceId, command.SourceVersion, command.ObservedUtc, materiality,
            command.DeduplicationKey, command.CorrelationId, command.AffectedGoalId, command.Payload);
        db.OperatingEvents.Add(row);
        if (IsAdministrativeSelfEvent(command.EventType, command.SourceType))
        {
            row.Suppress("Administrative state written by the operating loop does not request another cycle.");
            await db.SaveChangesAsync(ct);
            return Map(row);
        }

        var config = await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        var cooldown = TimeSpan.FromMinutes(config?.MinimumCycleIntervalMinutes ?? 60);
        var similarSince = command.ObservedUtc.ToUniversalTime().Subtract(cooldown);
        var similarExists = await db.OperatingEvents.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.EventType == command.EventType && x.Id != row.Id &&
            x.AffectedGoalId == command.AffectedGoalId && x.ObservedUtc >= similarSince &&
            (x.Status == OperatingEventStatus.Pending || x.Status == OperatingEventStatus.Processed), ct);
        if (similarExists)
        {
            row.Coalesce("A materially equivalent event is already pending or was reviewed inside the configured cooldown.");
            await db.SaveChangesAsync(ct);
            return Map(row);
        }
        await db.SaveChangesAsync(ct);
        if (materiality >= OperatingEventMateriality.Medium)
        {
            var latestCycle = await db.OperatingCycles.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId).MaxAsync(x => (DateTime?)x.RequestedUtc, ct);
            var notBefore = latestCycle.HasValue && latestCycle.Value.Add(cooldown) > DateTime.UtcNow
                ? latestCycle.Value.Add(cooldown) : DateTime.UtcNow;
            await RequestAsync(companyId, "event", row.Id.ToString("N"), $"event-cycle:{row.DeduplicationKey}",
                row.CorrelationId, notBefore, row.Id, ct);
        }
        return Map(row);
    }

    public async Task<OperatingCycleRequestDto> RequestAsync(Guid companyId, string triggerType,
        string? triggerReference, string deduplicationKey, string correlationId, DateTime notBeforeUtc,
        Guid? operatingEventId, CancellationToken ct)
    {
        var existing = await db.OperatingCycleRequests.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.DeduplicationKey == deduplicationKey, ct);
        if (existing is not null) return Map(existing);
        var row = new OperatingCycleRequest(Guid.NewGuid(), companyId, triggerType, triggerReference,
            deduplicationKey, correlationId, notBeforeUtc, operatingEventId);
        db.OperatingCycleRequests.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            existing = await db.OperatingCycleRequests.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DeduplicationKey == deduplicationKey, ct);
            if (existing is null) throw;
            return Map(existing);
        }
        return Map(row);
    }

    public async Task<IReadOnlyList<OperatingEventDto>> ListEventsAsync(Guid companyId, int take, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct); take = Math.Clamp(take, 1, 100);
        return (await db.OperatingEvents.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.ObservedUtc).Take(take).ToListAsync(ct)).Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<OperatingCycleRequestDto>> ListRequestsAsync(Guid companyId, int take, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct); take = Math.Clamp(take, 1, 100);
        return (await db.OperatingCycleRequests.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc).Take(take).ToListAsync(ct)).Select(Map).ToArray();
    }

    private async Task RequireMemberAsync(Guid companyId, CancellationToken ct) =>
        _ = await memberships.ResolveAsync(companyId, ct) ?? throw new UnauthorizedAccessException("Active company membership is required.");
    private static bool IsAdministrativeSelfEvent(string eventType, string sourceType) =>
        sourceType.Trim().ToLowerInvariant() is "operating_cycle" or "operating_plan" or "operating_snapshot" or "operating_validation" ||
        eventType.Trim().ToLowerInvariant() is "operating_cycle_updated" or "operating_plan_updated" or "operating_validation_recorded";
    internal static OperatingEventDto Map(OperatingEvent x) => new(x.Id, x.EventType, x.SourceType,
        x.SourceId, x.SourceVersion, x.ObservedUtc, x.Materiality.ToStorageValue(), x.Status.ToStorageValue(),
        x.SuppressionReason, x.AffectedGoalId, x.CreatedUtc, x.ProcessedUtc);
    internal static OperatingCycleRequestDto Map(OperatingCycleRequest x) => new(x.Id, x.OperatingEventId,
        x.OperatingCycleId, x.TriggerType, x.TriggerReference, x.Status.ToStorageValue(), x.NotBeforeUtc,
        x.AttemptCount, x.MaxAttempts, x.LeaseExpiresUtc, x.FailureCode, x.FailureSummary,
        x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc);
}
