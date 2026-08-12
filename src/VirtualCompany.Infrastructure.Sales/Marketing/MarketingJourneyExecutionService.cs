using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingJourneyWorkerOptions
{
    public const string SectionName = "Marketing:JourneyWorker";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 25;
}

public sealed class MarketingJourneyExecutionService(
    VirtualCompanyDbContext db,
    IApprovalRequestService approvals,
    IMarketingJourneyRuleEvaluator journeyRules,
    IEnumerable<IMarketingChannelAdapter> adapters) : IMarketingJourneyExecutionService
{
    public async Task<int> ProcessDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct)
    {
        var due = await db.MarketingJourneyEnrollments.IgnoreQueryFilters()
            .Where(x => x.Status == "active" && x.NextStepUtc.HasValue && x.NextStepUtc <= nowUtc)
            .OrderBy(x => x.NextStepUtc).Take(Math.Clamp(batchSize, 1, 100)).ToListAsync(ct);
        var processed = 0;
        foreach (var enrollment in due)
        {
            var workerId = $"journey-worker:{Environment.ProcessId}";
            try { enrollment.Claim(workerId, TimeSpan.FromMinutes(5), nowUtc); await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.Entry(enrollment).State = EntityState.Detached; continue; }
            catch (InvalidOperationException) { continue; }
            var journey = await db.MarketingLifecycleJourneys.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == enrollment.CompanyId && x.Id == enrollment.MarketingLifecycleJourneyId, ct);
            var contact = await db.Contacts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == enrollment.CompanyId && x.Id == enrollment.ContactId && !x.IsDeleted, ct);
            if (journey?.Status == "paused")
            { enrollment.WaitUntil(nowUtc.AddMinutes(15)); await db.SaveChangesAsync(ct); processed++; continue; }
            if (journey is null || contact is null || journey.Status != "active" || journey.Version != enrollment.JourneyVersion ||
                nowUtc < journey.ValidFromUtc || nowUtc >= journey.ValidToUtc)
            { enrollment.Exit("journey_or_contact_unavailable"); await db.SaveChangesAsync(ct); processed++; continue; }
            if (journey.MarketingCustomerSegmentVersionId.HasValue && !await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == enrollment.CompanyId && x.Id == journey.MarketingCustomerSegmentVersionId.Value &&
                (x.Status == MarketingStrategicStatuses.Approved || x.Status == MarketingStrategicStatuses.Active), ct))
            { enrollment.Block("target_segment_version_stale"); await db.SaveChangesAsync(ct); processed++; continue; }
            if (await IsSuppressedAsync(enrollment.CompanyId, contact, nowUtc, ct))
            { enrollment.Exit("communication_suppressed"); await db.SaveChangesAsync(ct); processed++; continue; }
            if (!await HasCurrentConsentAsync(enrollment.CompanyId, contact.Id, ct))
            { enrollment.Exit("communication_consent_withdrawn"); await db.SaveChangesAsync(ct); processed++; continue; }
            var inboundEventTypes = await db.MarketingJourneyInboundEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == enrollment.CompanyId && x.MarketingLifecycleJourneyId == journey.Id &&
                    x.JourneyVersion == journey.Version && x.ContactId == contact.Id)
                .Select(x => x.EventType).Distinct().ToListAsync(ct);
            var ruleDecision = journeyRules.Evaluate(journey.AudienceEligibilityJson, journey.EntryExitCriteriaJson,
                new MarketingJourneyContactFacts(contact.Id, contact.Status, !string.IsNullOrWhiteSpace(contact.Email),
                    contact.CustomerCompanyId.HasValue, contact.PreferredLanguage, contact.CreatedUtc,
                    inboundEventTypes.ToHashSet(StringComparer.OrdinalIgnoreCase), journey.MarketingCustomerSegmentVersionId.HasValue
                        ? new HashSet<Guid> { journey.MarketingCustomerSegmentVersionId.Value } : new HashSet<Guid>()), "step");
            enrollment.RecordEvaluation(workerId, ruleDecision.EvidenceJson);
            if (ruleDecision.GoalReached) { enrollment.Exit("journey_goal_reached"); await db.SaveChangesAsync(ct); processed++; continue; }
            if (ruleDecision.ShouldExit || !ruleDecision.Allowed) { enrollment.Exit(ruleDecision.ReasonCodes.FirstOrDefault() ?? "journey_ineligible"); await db.SaveChangesAsync(ct); processed++; continue; }
            if (!journey.ApprovalRequestId.HasValue)
            { enrollment.Block("journey_approval_missing"); await db.SaveChangesAsync(ct); processed++; continue; }
            var journeyApproval = await approvals.GetAsync(enrollment.CompanyId, journey.ApprovalRequestId.Value, ct);
            if (!journeyApproval.Status.Equals("approved", StringComparison.OrdinalIgnoreCase) || journey.Version != enrollment.JourneyVersion)
            { enrollment.Block("journey_approval_stale"); await db.SaveChangesAsync(ct); processed++; continue; }
            if (enrollment.LastChannelActionId.HasValue)
            {
                var last = await db.MarketingChannelActions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CompanyId == enrollment.CompanyId && x.Id == enrollment.LastChannelActionId, ct);
                if (last is not null && last.Status != "delivered")
                {
                    if (last.Status is "failed" or "cancelled" or "ambiguous") enrollment.Block($"channel_action_{last.Status}");
                    else enrollment.WaitUntil(nowUtc.AddMinutes(5));
                    await db.SaveChangesAsync(ct); processed++; continue;
                }
            }
            if (nowUtc - enrollment.WindowStartedUtc >= TimeSpan.FromHours(24)) enrollment.ResetWindow(nowUtc);
            if (enrollment.ActionsInWindow >= journey.FrequencyCap)
            { enrollment.WaitUntil(enrollment.WindowStartedUtc.AddHours(24)); await db.SaveChangesAsync(ct); processed++; continue; }
            JourneyStep[] steps;
            try { steps = JsonSerializer.Deserialize<JourneyStep[]>(journey.StepsJson, JsonOptions) ?? []; }
            catch (JsonException) { enrollment.Block("invalid_journey_steps"); await db.SaveChangesAsync(ct); processed++; continue; }
            if (enrollment.NextStepIndex >= steps.Length)
            { enrollment.Complete(); await db.SaveChangesAsync(ct); processed++; continue; }
            var step = steps[enrollment.NextStepIndex];
            if (step.ConnectionId == Guid.Empty || string.IsNullOrWhiteSpace(step.ActionType) || string.IsNullOrWhiteSpace(step.PayloadJson) || string.IsNullOrWhiteSpace(step.DestinationReference))
            { enrollment.Block("invalid_journey_step"); await db.SaveChangesAsync(ct); processed++; continue; }
            var connection = await db.MarketingChannelConnections.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == enrollment.CompanyId && x.Id == step.ConnectionId && x.Status == "connected", ct);
            if (connection is null || connection.HealthStatus == "reauthorization_required")
            { enrollment.Block("channel_connection_unavailable"); await db.SaveChangesAsync(ct); processed++; continue; }
            var payload = step.PayloadJson.Replace("{{contact.fullName}}", contact.FullName, StringComparison.Ordinal)
                .Replace("{{contact.email}}", contact.Email, StringComparison.Ordinal);
            var validator = adapters.SingleOrDefault(x => x.Provider.Equals(connection.Provider, StringComparison.OrdinalIgnoreCase));
            var validation = validator?.Validate(step.ActionType, payload, connection.CapabilitiesJson);
            if (validation is null || !validation.Allowed)
            { enrollment.Block(validation?.ReasonCode ?? "channel_adapter_unavailable"); await db.SaveChangesAsync(ct); processed++; continue; }
            var key = $"journey:{enrollment.Id:N}:v{enrollment.JourneyVersion}:step:{enrollment.NextStepIndex}";
            var action = await db.MarketingChannelActions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == enrollment.CompanyId && x.IdempotencyKey == key, ct);
            if (action is null)
            {
                action = new MarketingChannelAction(Guid.NewGuid(), enrollment.CompanyId, connection.Id, null, null,
                    step.DestinationReference, step.ActionType, payload, nowUtc, key);
                db.MarketingChannelActions.Add(action); await db.SaveChangesAsync(ct);
            }
            if (action.Status == "proposed")
            {
                var approval = await approvals.CreateAsync(enrollment.CompanyId, new CreateApprovalRequestCommand(
                    "marketing_channel_action", action.Id, "user", journey.OwnerUserId, "marketing_lifecycle_delivery", null, "company_manager"), ct);
                action.Submit(approval.Id);
            }
            var next = enrollment.NextStepIndex + 1 < steps.Length ? nowUtc.AddHours(Math.Clamp(step.DelayHours, 0, 720)) : (DateTime?)null;
            db.MarketingJourneyStepAttempts.Add(new MarketingJourneyStepAttempt(Guid.NewGuid(), enrollment.CompanyId,
                enrollment.Id, enrollment.JourneyVersion, enrollment.NextStepIndex, enrollment.AttemptCount,
                "action_prepared", ruleDecision.EvidenceJson, action.Id, $"journey:{enrollment.Id:N}:step:{enrollment.NextStepIndex}"));
            enrollment.Advance(action.Id, next);
            await db.SaveChangesAsync(ct); processed++;
        }
        return processed;
    }

    private async Task<bool> IsSuppressedAsync(Guid companyId, Contact contact, DateTime nowUtc, CancellationToken ct) =>
        await db.SalesSuppressions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.IsActive &&
            (!x.ExpiresUtc.HasValue || x.ExpiresUtc > nowUtc) && ((x.ScopeType == "email" && x.ScopeValue == contact.Email) ||
            (x.ScopeType == "person" && x.ScopeValue == contact.FullName.ToLower())), ct);

    private async Task<bool> HasCurrentConsentAsync(Guid companyId, Guid contactId, CancellationToken ct) =>
        await db.SalesCampaignAudienceMembers.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.ContactId == contactId &&
            (x.ConsentStatus == "granted" || x.ConsentStatus == "consented" ||
             x.ConsentStatus == "approved" || x.ConsentStatus == "opted_in"), ct);

    private sealed record JourneyStep(Guid ConnectionId, string DestinationReference, string ActionType, string PayloadJson, int DelayHours);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed class MarketingJourneyBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<MarketingJourneyWorkerOptions> options,
    ILogger<MarketingJourneyBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            if (current.Enabled)
            {
                try
                { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<IMarketingJourneyExecutionService>().ProcessDueAsync(DateTime.UtcNow, current.BatchSize, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception) { logger.LogError(exception, "Marketing lifecycle journey worker failed."); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(current.PollSeconds, 5, 300)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}

public sealed class MarketingJourneyInboundEventService(VirtualCompanyDbContext db) : IMarketingJourneyInboundEventService
{
    public async Task<MarketingJourneyInboundEventDto> ProcessAsync(Guid companyId,
        ProcessMarketingJourneyInboundEventRequest request, CancellationToken ct)
    {
        var key = $"journey:{request.JourneyId:N}:v{request.JourneyVersion}:contact:{request.ContactId:N}:{request.EventType.ToLowerInvariant()}:{request.EventReference}:v{request.OccurrenceVersion}";
        var existing = await db.MarketingJourneyInboundEvents.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.IdempotencyKey == key, ct);
        if (existing is not null) return Map(existing);
        var journey = await db.MarketingLifecycleJourneys.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == request.JourneyId && x.Version == request.JourneyVersion, ct)
            ?? throw new InvalidOperationException("The exact journey version is unavailable.");
        if (!await db.Contacts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == request.ContactId && !x.IsDeleted, ct))
            throw new InvalidOperationException("The contact is unavailable.");
        var item = new MarketingJourneyInboundEvent(Guid.NewGuid(), companyId, journey.Id, journey.Version,
            request.ContactId, request.EventType, request.EventReference, request.OccurrenceVersion,
            request.OccurredUtc, request.EvidenceJson, key);
        var enrollment = await db.MarketingJourneyEnrollments.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.MarketingLifecycleJourneyId == journey.Id &&
            x.JourneyVersion == journey.Version && x.ContactId == request.ContactId && x.Status == "active", ct);
        if (enrollment is not null && enrollment.NextStepUtc > DateTime.UtcNow) enrollment.WaitUntil(DateTime.UtcNow);
        item.SetOutcome(enrollment is null ? "recorded" : "transition_due"); db.MarketingJourneyInboundEvents.Add(item);
        await db.SaveChangesAsync(ct); return Map(item);
    }
    private static MarketingJourneyInboundEventDto Map(MarketingJourneyInboundEvent x) => new(x.Id,
        x.MarketingLifecycleJourneyId, x.JourneyVersion, x.ContactId, x.EventType, x.EventReference,
        x.OccurrenceVersion, x.Outcome, x.OccurredUtc, x.ProcessedUtc);
}
