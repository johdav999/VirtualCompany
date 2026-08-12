using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingOperatingSnapshotContributor(VirtualCompanyDbContext db)
    : ICompanyOperatingSnapshotContributor
{
    public const int MaximumItemsPerCollection = 25;
    public string SectionName => "marketing";

    public async Task<CompanyOperatingSnapshotContribution> CaptureAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        var objectives = await db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Status == "active").ThenBy(x => x.PeriodEndUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new
            {
                sourceId = $"marketing-objective:{x.Id:N}",
                x.Id,
                x.Name,
                x.ObjectiveType,
                x.TargetValue,
                x.Unit,
                x.BaselineValue,
                x.PeriodStartUtc,
                x.PeriodEndUtc,
                x.Status,
                x.Version,
                x.UpdatedUtc
            }).ToListAsync(cancellationToken);

        var plans = await db.MarketingPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Status == "active").ThenByDescending(x => x.UpdatedUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new
            {
                sourceId = $"marketing-plan:{x.Id:N}",
                x.Id,
                x.Name,
                x.StartsUtc,
                x.EndsUtc,
                x.PlannedBudget,
                x.BudgetCurrency,
                x.Status,
                x.Version,
                x.UpdatedUtc
            }).ToListAsync(cancellationToken);

        var campaigns = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new
            {
                sourceId = $"sales-campaign:{x.Id:N}",
                x.Id,
                x.Name,
                x.Status,
                x.AudienceType,
                x.ApprovalStatus,
                x.ApprovalRequired,
                x.OutboundEnabled,
                x.ScheduledLaunchUtc,
                x.UpdatedUtc
            }).ToListAsync(cancellationToken);

        var content = await db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (x.Status == "draft" || x.Status == "submitted"))
            .OrderBy(x => x.DueUtc).ThenByDescending(x => x.UpdatedUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new
            {
                sourceId = $"marketing-content:{x.Id:N}",
                x.Id,
                x.Title,
                x.Audience,
                x.Channel,
                x.DueUtc,
                x.Status,
                x.Version,
                x.UpdatedUtc
            }).ToListAsync(cancellationToken);

        var experiments = await db.MarketingExperiments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "completed")
            .OrderBy(x => x.EndsUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new
            {
                sourceId = $"marketing-experiment:{x.Id:N}",
                x.Id,
                x.Name,
                x.PrimaryMetric,
                x.GuardrailMetric,
                x.MinimumSampleSize,
                x.StartsUtc,
                x.EndsUtc,
                x.Status,
                x.UpdatedUtc
            }).ToListAsync(cancellationToken);

        var observations = await db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsSuperseded)
            .OrderByDescending(x => x.PeriodEndUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new
            {
                sourceId = $"marketing-observation:{x.Id:N}",
                x.Id,
                x.MetricCode,
                x.Value,
                x.Unit,
                x.Provider,
                x.PeriodStartUtc,
                x.PeriodEndUtc,
                x.SourceReference,
                x.RetrievedUtc
            }).ToListAsync(cancellationToken);

        var strategies = await db.MarketingStrategies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "archived")
            .OrderByDescending(x => x.UpdatedUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-strategy:{x.Id:N}", x.Id, x.Title, x.Status, x.ValidFromUtc, x.ValidToUtc, x.Version, x.ApprovalRequestId, x.UpdatedUtc }).ToListAsync(cancellationToken);
        var segments = await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (x.Status == "approved" || x.Status == "active" || x.Status == "in_review"))
            .OrderByDescending(x => x.UpdatedUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-segment-version:{x.Id:N}", x.Id, x.MarketingCustomerSegmentId, x.VersionNumber, x.Status, x.TargetState, x.SizeLow, x.SizeHigh, x.SizeMethod, x.Confidence, x.AttractivenessScore, x.EvidenceObservedUtc, x.UpdatedUtc }).ToListAsync(cancellationToken);
        var operatingRuns = await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedUtc)
            .Take(MaximumItemsPerCollection + 1).Select(x => new { sourceId = $"marketing-operating-run:{x.Id:N}", x.Id, x.AgentId, x.CompanyGoalId, x.OperatingInitiativeId, x.TriggerType, x.EffectiveAuthority, x.Status, x.RecoveryCode, x.CreatedUtc, x.CompletedUtc }).ToListAsync(cancellationToken);
        var creativeAssets = await db.MarketingCreativeAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.UpdatedUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-creative-asset:{x.Id:N}", x.Id, x.MarketingContentBriefId, x.SalesCampaignId, x.Name, x.MediaType, x.Dimensions, x.Language, x.BrandProfileVersion, x.SafetyResult, x.Status, x.Version, x.UpdatedUtc }).ToListAsync(cancellationToken);
        var channelConnections = await db.MarketingChannelConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.Provider).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-channel-connection:{x.Id:N}", x.Id, x.Provider, x.DisplayName, x.Status, x.HealthStatus, x.FailureSummary, x.LastCheckedUtc }).ToListAsync(cancellationToken);
        var channelActions = await db.MarketingChannelActions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.UpdatedUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-channel-action:{x.Id:N}", x.Id, x.MarketingChannelConnectionId, x.SalesCampaignId, x.MarketingContentBriefId, x.ActionType, x.ScheduledUtc, x.Status, x.ApprovalRequestId, x.AttemptCount, x.FailureCode, x.UpdatedUtc }).ToListAsync(cancellationToken);
        var journeys = await db.MarketingLifecycleJourneys.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "completed").OrderByDescending(x => x.UpdatedUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-journey:{x.Id:N}", x.Id, x.Name, x.MarketingCustomerSegmentVersionId, x.FrequencyCap, x.ValidFromUtc, x.ValidToUtc, x.Status, x.ApprovalRequestId, x.Version, x.UpdatedUtc }).ToListAsync(cancellationToken);
        var journeyEnrollments = await db.MarketingJourneyEnrollments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "completed" && x.Status != "exited")
            .OrderBy(x => x.NextStepUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-journey-enrollment:{x.Id:N}", x.Id, x.MarketingLifecycleJourneyId,
                x.ContactId, x.JourneyVersion, x.Status, x.NextStepIndex, x.NextStepUtc, x.ActionsInWindow,
                x.LastChannelActionId, x.FailureCode, x.UpdatedUtc }).ToListAsync(cancellationToken);
        var attribution = await db.MarketingAttributionResults.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-attribution:{x.Id:N}", x.Id, x.SubjectType, x.SubjectId, x.Model, x.Classification, x.AttributedValue, x.Unit, x.Confidence, x.PeriodStartUtc, x.PeriodEndUtc, x.CreatedUtc }).ToListAsync(cancellationToken);
        var eventTriggers = await db.MarketingEventTriggers.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "resolved").OrderByDescending(x => x.CreatedUtc).Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-event:{x.Id:N}", x.Id, x.EventType, x.SourceType, eventSourceId = x.SourceId, x.SourceVersion, x.Severity, x.Status, x.OperatingRunId, x.FailureSummary, x.CorrelationId, x.CreatedUtc, x.UpdatedUtc }).ToListAsync(cancellationToken);
        var workEvidence = await db.MarketingWorkEvidence.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-work-evidence:{x.Id:N}", x.Id, x.MarketingOperatingRunId,
                x.OperatingInitiativeId, x.WorkTaskId, x.RecordType, x.Version, x.EvidenceVersion,
                x.Confidence, x.BlockersJson, x.RequestedNextAction, x.CorrelationId, x.CreatedUtc }).ToListAsync(cancellationToken);
        var companySignals = await db.MarketingCompanySignals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "dismissed").OrderByDescending(x => x.CreatedUtc)
            .Take(MaximumItemsPerCollection + 1)
            .Select(x => new { sourceId = $"marketing-company-signal:{x.Id:N}", x.Id, x.MarketingOperatingRunId,
                x.SignalType, x.Severity, x.Summary, x.Status, x.CycleEvaluationRequested,
                x.CorrelationId, x.CreatedUtc, x.UpdatedUtc }).ToListAsync(cancellationToken);

        var gaps = new List<string>();
        if (objectives.Count == 0) gaps.Add("No Marketing objectives are available.");
        if (plans.Count == 0) gaps.Add("No Marketing plans are available.");
        if (observations.Count == 0) gaps.Add("No source-linked Marketing performance observations are available.");
        if (segments.Count == 0) gaps.Add("No approved customer segment versions are available.");
        if (channelConnections.Count == 0) gaps.Add("No Marketing channel connections are configured.");
        if (attribution.Count == 0) gaps.Add("No classified Marketing attribution results are available.");

        var truncated = new[]
        {
            objectives.Count, plans.Count, campaigns.Count, content.Count, experiments.Count, observations.Count,
            strategies.Count, segments.Count, operatingRuns.Count, creativeAssets.Count, channelConnections.Count,
            channelActions.Count, journeys.Count, journeyEnrollments.Count, attribution.Count, eventTriggers.Count
            , workEvidence.Count, companySignals.Count
        }.Any(count => count > MaximumItemsPerCollection);

        var payload = JsonSerializer.SerializeToNode(new
        {
            observedAtUtc = DateTime.UtcNow,
            objectives = objectives.Take(MaximumItemsPerCollection),
            plans = plans.Take(MaximumItemsPerCollection),
            campaigns = campaigns.Take(MaximumItemsPerCollection),
            content = content.Take(MaximumItemsPerCollection),
            experiments = experiments.Take(MaximumItemsPerCollection),
            observations = observations.Take(MaximumItemsPerCollection),
            strategies = strategies.Take(MaximumItemsPerCollection),
            segments = segments.Take(MaximumItemsPerCollection),
            operatingRuns = operatingRuns.Take(MaximumItemsPerCollection),
            creativeAssets = creativeAssets.Take(MaximumItemsPerCollection),
            channelConnections = channelConnections.Take(MaximumItemsPerCollection),
            channelActions = channelActions.Take(MaximumItemsPerCollection),
            journeys = journeys.Take(MaximumItemsPerCollection),
            journeyEnrollments = journeyEnrollments.Take(MaximumItemsPerCollection),
            attribution = attribution.Take(MaximumItemsPerCollection),
            eventTriggers = eventTriggers.Take(MaximumItemsPerCollection),
            workEvidence = workEvidence.Take(MaximumItemsPerCollection),
            companySignals = companySignals.Take(MaximumItemsPerCollection),
            dataGaps = gaps
        });

        var sourceCount = objectives.Count + plans.Count + campaigns.Count + content.Count +
                          experiments.Count + observations.Count + strategies.Count + segments.Count + operatingRuns.Count +
                          creativeAssets.Count + channelConnections.Count + channelActions.Count + journeys.Count + journeyEnrollments.Count +
                          attribution.Count + eventTriggers.Count;
        sourceCount += workEvidence.Count + companySignals.Count;
        return new CompanyOperatingSnapshotContribution(
            SectionName,
            payload,
            sourceCount,
            gaps,
            truncated,
            DateTime.UtcNow);
    }
}
