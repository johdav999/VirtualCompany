using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyManualReviewService(
    VirtualCompanyDbContext db,
    ICompanyMembershipContextResolver memberships,
    ICompanyOperatingEventService operatingEvents,
    IAuditEventWriter audit,
    IExecutiveCockpitDashboardCacheInvalidator cache,
    ICorrelationContextAccessor correlation,
    TimeProvider timeProvider) : ICompanyManualReviewService
{
    private static readonly OperatingCycleRequestStatus[] ActiveStatuses =
    [
        OperatingCycleRequestStatus.Pending,
        OperatingCycleRequestStatus.Claimed,
        OperatingCycleRequestStatus.Processing,
        OperatingCycleRequestStatus.RetryScheduled
    ];

    public async Task<TodayWorkspaceManualReviewDto> GetStatusAsync(
        Guid companyId,
        bool canRequest,
        CancellationToken cancellationToken)
    {
        var latest = await db.OperatingCycleRequests.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TriggerType == "manual_review")
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var availability = await AvailabilityAsync(companyId, canRequest, cancellationToken);
        return Map(latest, availability);
    }

    public async Task<TodayWorkspaceManualReviewDto> RequestAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var member = await memberships.ResolveAsync(companyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (member.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw new UnauthorizedAccessException("Company manager access is required.");

        var availability = await AvailabilityAsync(companyId, true, cancellationToken);
        var correlationId = correlation.CorrelationId ?? Guid.NewGuid().ToString("N");
        if (!availability.Allowed)
        {
            await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
                "company.operating_cycle.manual_review_denied", "company", companyId.ToString("N"),
                AuditEventOutcomes.Denied, availability.Explanation,
                Metadata: new Dictionary<string, string?>
                {
                    ["scope"] = "company",
                    ["policyOutcome"] = availability.ReasonCode
                }, CorrelationId: correlationId), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Map(null, availability);
        }

        var active = await db.OperatingCycleRequests.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TriggerType == "manual_review" && ActiveStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null) return Map(active, availability);

        var previous = await db.OperatingCycleRequests.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TriggerType == "manual_review")
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var deduplicationKey = $"manual-review:{companyId:N}:{previous?.ToString("N") ?? "initial"}";
        var requested = await operatingEvents.RequestAsync(companyId, "manual_review", member.UserId.ToString("N"),
            deduplicationKey, correlationId, timeProvider.GetUtcNow().UtcDateTime, null, cancellationToken);

        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            "company.operating_cycle.manual_review_requested", "operating_cycle_request", requested.Id.ToString("N"),
            AuditEventOutcomes.Requested, "A durable company review was queued for background processing.",
            Metadata: new Dictionary<string, string?>
            {
                ["scope"] = "company",
                ["policyOutcome"] = "allowed",
                ["requestId"] = requested.Id.ToString("N"),
                ["operatingCycleId"] = requested.OperatingCycleId?.ToString("N")
            }, CorrelationId: correlationId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await cache.InvalidateAsync(companyId, cancellationToken);
        return Map(requested, availability);
    }

    private async Task<ReviewAvailability> AvailabilityAsync(Guid companyId, bool authorized, CancellationToken cancellationToken)
    {
        if (!authorized) return new(false, "not_authorized", "Company manager access is required to request a review.");
        var config = await db.CompanyOperatingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (config is null) return new(false, "not_configured", "Configure Company Operation and assign a coordinator agent before requesting a review.");
        if (config.EmergencyStopped) return new(false, "emergency_stopped", "Company Operation is emergency stopped. Clear the stop before requesting a review.");
        if (config.IsPaused) return new(false, "paused", string.IsNullOrWhiteSpace(config.PauseReason)
            ? "Company Operation is paused. Resume it before requesting a review."
            : $"Company Operation is paused: {config.PauseReason}");
        if (!config.CoordinatorAgentId.HasValue || !await db.Agents.AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.Id == config.CoordinatorAgentId && x.Status == AgentStatus.Active,
                cancellationToken))
            return new(false, "coordinator_unavailable", "Assign an active coordinator agent before requesting a review.");
        var today = timeProvider.GetUtcNow().UtcDateTime.Date;
        if (await db.OperatingCycles.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.RequestedUtc >= today,
                cancellationToken) >= config.MaximumCyclesPerDay)
            return new(false, "cycle_budget_reached", "The daily company-review budget has been reached. Try again after the budget resets.");
        return new(true, null, null);
    }

    private static TodayWorkspaceManualReviewDto Map(object? source, ReviewAvailability availability)
    {
        Guid? requestId = null;
        Guid? cycleId = null;
        string state = "idle";
        string message = availability.Allowed ? "Request a fresh company review when you need one." : availability.Explanation!;
        DateTime? updatedUtc = null;

        if (source is VirtualCompany.Domain.Entities.OperatingCycleRequest row)
        {
            requestId = row.Id; cycleId = row.OperatingCycleId; updatedUtc = row.UpdatedUtc;
            (state, message) = Present(row.Status);
        }
        else if (source is OperatingCycleRequestDto dto)
        {
            requestId = dto.Id; cycleId = dto.OperatingCycleId; updatedUtc = dto.UpdatedUtc;
            (state, message) = Present(OperatingCycleRequestStatusValues.Parse(dto.Status));
        }

        return new TodayWorkspaceManualReviewDto(availability.Allowed, availability.ReasonCode,
            availability.Explanation, requestId, cycleId, state, message, updatedUtc);
    }

    private static (string State, string Message) Present(OperatingCycleRequestStatus status) => status switch
    {
        OperatingCycleRequestStatus.Pending or OperatingCycleRequestStatus.Claimed => ("queued", "Company review queued. Agents will run in the background."),
        OperatingCycleRequestStatus.Processing => ("running", "Company review is running in the background."),
        OperatingCycleRequestStatus.Completed => ("completed", "Company review completed. Today now reflects the latest available evidence."),
        OperatingCycleRequestStatus.RetryScheduled => ("blocked", "The review is waiting for an automatic retry."),
        OperatingCycleRequestStatus.Suppressed => ("blocked", "The review was blocked by the current operating policy. Open Company Operation for recovery details."),
        OperatingCycleRequestStatus.DeadLettered => ("failed", "The review failed safely. Open Company Operation for recovery details."),
        _ => ("idle", "Request a fresh company review when you need one.")
    };

    private sealed record ReviewAvailability(bool Allowed, string? ReasonCode, string? Explanation);
}
