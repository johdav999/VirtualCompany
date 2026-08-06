using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class CampaignPlanningService : ICampaignPlanningService
{
    private static readonly string[] PositiveConsent = ["granted", "allowed", "opted_in", "subscribed", "active"];
    private readonly VirtualCompanyDbContext _db;

    public CampaignPlanningService(VirtualCompanyDbContext db) => _db = db;

    public async Task<CampaignInitiativeResponse?> GetInitiativeAsync(
        Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await InitiativeQuery(companyId, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        return campaign is null ? null : Map(campaign);
    }

    public async Task<CampaignInitiativeResponse?> ConfigureInitiativeAsync(
        Guid companyId, Guid userId, Guid campaignId, ConfigureCampaignInitiativeRequest request, CancellationToken cancellationToken)
    {
        var campaign = await InitiativeQuery(companyId, tracking: true)
            .SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign is null) return null;
        if (campaign.ConcurrencyVersion != request.ExpectedVersion)
            throw new DbUpdateConcurrencyException("This campaign changed after you opened it. Refresh before saving.");
        if (request.OwnerUserId != userId && !await IsCompanyMember(companyId, request.OwnerUserId, cancellationToken))
            throw new InvalidOperationException("Choose an owner who belongs to this company.");
        if (request.OwnerAgentId.HasValue && !await _db.Agents.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == request.OwnerAgentId, cancellationToken))
            throw new InvalidOperationException("Choose an agent that belongs to this company.");
        if (request.Offer.KnowledgeDocumentId.HasValue)
        {
            var documentReady = await _db.CompanyKnowledgeDocuments.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == request.Offer.KnowledgeDocumentId &&
                               x.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed, cancellationToken);
            if (!documentReady)
                throw new InvalidOperationException("The selected product or offer document is not approved and processed for this company.");
        }

        campaign.ConfigureInitiative(request.CampaignType, request.Description, request.OwnerUserId, request.OwnerAgentId,
            request.ObjectiveType, request.ObjectiveTarget, request.ObjectiveUnit, request.ObjectiveTargetUtc,
            request.PlanningStartsUtc, request.ScheduledLaunchUtc, request.EndsUtc, request.TimeZoneId,
            request.PlannedBudget, request.BudgetCurrency, request.ReviewDueUtc);
        _db.SalesCampaignObjectives.RemoveRange(campaign.Objectives.Where(x => x.IsPrimary));
        _db.SalesCampaignOffers.RemoveRange(campaign.Offers);
        campaign.Objectives.Add(new SalesCampaignObjective(Guid.NewGuid(), companyId, campaign.Id, request.ObjectiveType,
            request.ObjectiveTarget, request.ObjectiveUnit, request.ObjectiveTargetUtc, true));
        campaign.Offers.Add(new SalesCampaignOffer(Guid.NewGuid(), companyId, campaign.Id, request.Offer.Name,
            request.Offer.SourceType, request.Offer.SourceReference, request.Offer.KnowledgeDocumentId, request.Offer.NoOfferRequired));
        if (!await _db.SalesCampaignKpiDefinitions.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.SalesCampaignId == campaign.Id, cancellationToken))
        {
            _db.SalesCampaignKpiDefinitions.AddRange(
                new SalesCampaignKpiDefinition(Guid.NewGuid(), companyId, campaign.Id, "reply_rate", "Reply rate",
                    "replies", "sent", "percent", null, null, 90, "Campaign delivery and reply events", 1),
                new SalesCampaignKpiDefinition(Guid.NewGuid(), companyId, campaign.Id, "opportunity_rate", "Opportunity rate",
                    "opportunities", "audience", "percent", null, null, 180, "Linked Sales opportunities", 1),
                new SalesCampaignKpiDefinition(Guid.NewGuid(), companyId, campaign.Id, "primary_objective", "Primary objective",
                    request.ObjectiveType, null, request.ObjectiveUnit, null, request.ObjectiveTarget, 180,
                    "Authoritative campaign objective evidence", 1));
        }
        Audit(companyId, userId, "sales.campaign.initiative_configured", campaign.Id, "Campaign objective, ownership, offer, dates, and budget were updated.");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(campaign);
    }

    public async Task<CampaignReadinessResponse?> GetReadinessAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await InitiativeQuery(companyId, tracking: false).SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        return campaign is null
            ? null
            : new CampaignReadinessResponse(campaign.Id, campaign.LifecycleStatus, campaign.ReadinessGaps().Count == 0,
                campaign.ConcurrencyVersion, campaign.ReadinessGaps());
    }

    public async Task<CampaignInitiativeResponse?> RequestReadinessAsync(
        Guid companyId, Guid userId, Guid campaignId, long expectedVersion, CancellationToken cancellationToken)
    {
        var campaign = await InitiativeQuery(companyId, tracking: true).SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign is null) return null;
        if (campaign.ConcurrencyVersion != expectedVersion)
            throw new DbUpdateConcurrencyException("This campaign changed after you opened it. Refresh before continuing.");
        campaign.MarkReadyForApproval();
        Audit(companyId, userId, "sales.campaign.readiness_requested", campaign.Id,
            campaign.LifecycleStatus == CampaignLifecycleStatuses.WaitingForApproval
                ? "Campaign preparation is complete and human approval is required."
                : "Campaign preparation is complete and the campaign is scheduled.");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(campaign);
    }

    public async Task<IReadOnlyList<CampaignSegmentResponse>> ListSegmentsAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await _db.SalesCampaignAudienceSegments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(Map).ToList();

    public async Task<CampaignSegmentResponse> CreateSegmentAsync(
        Guid companyId, Guid userId, CreateCampaignSegmentRequest request, CancellationToken cancellationToken)
    {
        var segment = new SalesCampaignAudienceSegment(Guid.NewGuid(), companyId, request.Name, request.SegmentKind);
        segment.Configure(request.Industry, request.Country, request.MinEmployees, request.MaxEmployees, request.BuyingRole,
            request.CustomerLifecycle, request.ProductInterest, request.PreferredLanguage,
            request.RequireCommunicationPermission, request.ExcludeOpenCriticalSupportCases);
        _db.SalesCampaignAudienceSegments.Add(segment);
        Audit(companyId, userId, "sales.campaign.segment_created", segment.Id, "A reusable campaign audience segment was created.");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(segment);
    }

    public async Task<CampaignAudiencePreviewResponse> PreviewSegmentAsync(Guid companyId, Guid segmentId, CancellationToken cancellationToken)
    {
        var segment = await _db.SalesCampaignAudienceSegments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == segmentId, cancellationToken)
            ?? throw new KeyNotFoundException("Audience segment was not found.");
        var members = await EvaluateSegment(companyId, segment, cancellationToken);
        return new CampaignAudiencePreviewResponse(segment.Id, segment.Version,
            members.Count(x => x.EligibilityStatus == "eligible"),
            members.Count(x => x.EligibilityStatus == "excluded"),
            members.Count(x => x.EligibilityStatus == "suppressed"),
            members.Count(x => x.EligibilityStatus == "ambiguous"),
            members.Count(x => x.EligibilityStatus == "missing_data"), members);
    }

    public async Task<CampaignAudienceSnapshotResponse?> CaptureAudienceAsync(
        Guid companyId, Guid userId, Guid campaignId, Guid segmentId, CancellationToken cancellationToken)
    {
        var campaign = await InitiativeQuery(companyId, tracking: true).SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign is null) return null;
        var segment = await _db.SalesCampaignAudienceSegments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == segmentId, cancellationToken)
            ?? throw new KeyNotFoundException("Audience segment was not found.");
        var preview = await EvaluateSegment(companyId, segment, cancellationToken);
        var version = await _db.SalesCampaignAudienceSnapshots.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId)
            .Select(x => (int?)x.SnapshotVersion).MaxAsync(cancellationToken) ?? 0;
        var snapshot = new SalesCampaignAudienceSnapshot(Guid.NewGuid(), companyId, campaignId, segment.Id, segment.Version, version + 1);
        foreach (var member in preview)
        {
            snapshot.Members.Add(new SalesCampaignAudienceMember(Guid.NewGuid(), companyId, snapshot.Id, member.ContactId,
                member.CustomerCompanyId, null, member.EligibilityStatus, member.Reason, member.ConsentStatus, member.CommunicationLanguage));
            if (member.EligibilityStatus == "eligible" && campaign.Contacts.All(x => x.ContactId != member.ContactId))
                campaign.Contacts.Add(new SalesCampaignContact(Guid.NewGuid(), companyId, campaignId, member.ContactId));
        }
        _db.SalesCampaignAudienceSnapshots.Add(snapshot);
        Audit(companyId, userId, "sales.campaign.audience_snapshotted", campaignId,
            $"Audience snapshot {snapshot.SnapshotVersion} captured {preview.Count(x => x.EligibilityStatus == "eligible")} eligible contacts.");
        await _db.SaveChangesAsync(cancellationToken);
        return new CampaignAudienceSnapshotResponse(snapshot.Id, campaignId, segment.Id, segment.Version, snapshot.SnapshotVersion,
            snapshot.CapturedUtc, preview.Count(x => x.EligibilityStatus == "eligible"),
            preview.Count(x => x.EligibilityStatus == "excluded"), preview.Count(x => x.EligibilityStatus == "suppressed"));
    }

    public async Task<IReadOnlyList<CampaignActivityResponse>> ListActivitiesAsync(
        Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await _db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == campaignId, cancellationToken);
        if (campaign is null) return [];

        var activities = (await _db.SalesCampaignActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId)
            .OrderBy(x => x.PlannedStartUtc).ThenBy(x => x.DueUtc).ToListAsync(cancellationToken)).Select(Map).ToList();
        var sequenceSteps = await _db.SalesSequenceSteps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SalesSequenceId == campaign.SalesSequenceId)
            .OrderBy(x => x.StepOrder)
            .ToListAsync(cancellationToken);
        if (sequenceSteps.Count == 0) return activities;

        var persistedLinks = await _db.SalesCampaignActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId && x.SalesSequenceStepId.HasValue)
            .Select(x => x.SalesSequenceStepId!.Value)
            .ToListAsync(cancellationToken);
        var executionStates = await _db.SalesSequenceExecutionSteps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId)
            .GroupBy(x => x.SalesSequenceStepId)
            .Select(x => new
            {
                StepId = x.Key,
                Sent = x.Any(v => v.SentUtc.HasValue),
                Failed = x.Any(v => v.Status == SalesStatuses.Failed),
                Pending = x.Any(v => v.Status == SalesStatuses.Pending)
            })
            .ToDictionaryAsync(x => x.StepId, cancellationToken);
        var linked = persistedLinks.ToHashSet();
        var start = campaign.ScheduledLaunchUtc ?? campaign.CreatedUtc;
        var accumulatedDelay = 0;
        foreach (var step in sequenceSteps)
        {
            accumulatedDelay += step.DelayDays;
            if (linked.Contains(step.Id)) continue;
            var due = start.AddDays(accumulatedDelay);
            var status = executionStates.TryGetValue(step.Id, out var state)
                ? state.Failed ? CampaignActivityStatuses.Failed
                    : state.Pending ? CampaignActivityStatuses.Ongoing
                    : state.Sent ? CampaignActivityStatuses.Completed
                    : CampaignActivityStatuses.Planned
                : CampaignActivityStatuses.Planned;
            activities.Add(new CampaignActivityResponse(
                step.Id,
                step.TemplateSubject ?? $"Email step {step.StepOrder}",
                "email",
                step.Channel,
                CampaignExecutionModes.Executable,
                status,
                due,
                due,
                campaign.OwnerUserId,
                campaign.OwnerAgentId,
                null,
                "sales.email.send",
                0,
                status == CampaignActivityStatuses.Completed ? "Legacy sequence email activity completed." : null,
                status == CampaignActivityStatuses.Failed ? "One or more legacy sequence executions failed." : null));
        }

        return activities.OrderBy(x => x.PlannedStartUtc).ThenBy(x => x.DueUtc).ToList();
    }

    public async Task<CampaignActivityResponse?> AddActivityAsync(
        Guid companyId, Guid userId, Guid campaignId, CreateCampaignActivityRequest request, CancellationToken cancellationToken)
    {
        var campaign = await InitiativeQuery(companyId, tracking: true).SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign is null) return null;
        if (campaign.PlanningStartsUtc.HasValue && request.PlannedStartUtc < campaign.PlanningStartsUtc ||
            campaign.EndsUtc.HasValue && request.DueUtc > campaign.EndsUtc)
            throw new InvalidOperationException("Activity dates must be inside the campaign planning and end dates.");
        if (request.DependsOnActivityId.HasValue && !await _db.SalesCampaignActivities.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId && x.Id == request.DependsOnActivityId, cancellationToken))
            throw new InvalidOperationException("The selected dependency is not part of this campaign.");
        if (request.ExecutionMode == CampaignExecutionModes.Executable && request.Channel != "email" &&
            string.IsNullOrWhiteSpace(request.RequiredToolCapability))
            throw new InvalidOperationException("Choose a configured tool capability before this activity can execute automatically.");

        var activity = new SalesCampaignActivity(Guid.NewGuid(), companyId, campaignId, request.Name, request.ActivityType,
            request.Channel, request.ExecutionMode, request.PlannedStartUtc, request.DueUtc, request.TimeZoneId,
            request.OwnerUserId, request.OwnerAgentId, request.DependsOnActivityId, request.MilestoneId,
            request.SalesSequenceStepId, request.RequiredToolCapability);
        campaign.Activities.Add(activity);
        Audit(companyId, userId, "sales.campaign.activity_created", activity.Id,
            request.ExecutionMode == CampaignExecutionModes.Manual
                ? "A tracked manual campaign activity was planned."
                : "A governed campaign activity was planned.");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(activity);
    }

    public async Task<CampaignPerformanceResponse?> GetPerformanceAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await InitiativeQuery(companyId, tracking: false).SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        if (campaign is null) return null;
        var steps = await _db.SalesSequenceExecutionSteps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId).ToListAsync(cancellationToken);
        var touches = await _db.SalesSourceTouches.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CampaignId == campaignId).ToListAsync(cancellationToken);
        var recordedCosts = await _db.SalesCampaignCosts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId).ToListAsync(cancellationToken);
        var definitions = await _db.SalesCampaignKpiDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId)
            .OrderBy(x => x.Key).ThenByDescending(x => x.Version).ToListAsync(cancellationToken);
        var dealIds = touches.Where(x => x.SubjectType == "deal").Select(x => x.SubjectId).Distinct().ToArray();
        var deals = await _db.Deals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && dealIds.Contains(x.Id) && !x.IsDeleted).ToListAsync(cancellationToken);
        var revenue = deals.Where(x => x.Status == SalesStatuses.Won).GroupBy(x => x.Currency)
            .Select(x => new CampaignCurrencyAmountResponse(x.Sum(d => d.Amount), x.Key, "directly attributed")).ToList();
        var budget = campaign.PlannedBudget.HasValue && campaign.BudgetCurrency is not null
            ? new[] { new CampaignCurrencyAmountResponse(campaign.PlannedBudget.Value, campaign.BudgetCurrency, "planned") }
            : [];
        var replied = touches.Count(x => x.InteractionType == "reply");
        var progress = campaign.PrimaryObjectiveType switch
        {
            "replies" when campaign.PrimaryObjectiveTarget > 0 => replied / campaign.PrimaryObjectiveTarget * 100m,
            "opportunities" when campaign.PrimaryObjectiveTarget > 0 => deals.Count / campaign.PrimaryObjectiveTarget * 100m,
            "revenue" when campaign.PrimaryObjectiveTarget > 0 && revenue.Count == 1 &&
                           campaign.PrimaryObjectiveUnit.Equals(revenue[0].Currency, StringComparison.OrdinalIgnoreCase)
                => revenue[0].Amount / campaign.PrimaryObjectiveTarget * 100m,
            _ => null
        };
        var costs = recordedCosts
            .Select(x => new CampaignCurrencyAmountResponse(x.Amount, x.Currency, x.Classification))
            .Concat(touches.Where(x => x.Cost.HasValue && x.Currency != null)
                .Select(x => new CampaignCurrencyAmountResponse(x.Cost!.Value, x.Currency!, "observed provider cost")))
            .GroupBy(x => new { x.Currency, x.Classification })
            .Select(x => new CampaignCurrencyAmountResponse(x.Sum(v => v.Amount), x.Key.Currency, x.Key.Classification))
            .OrderBy(x => x.Currency).ThenBy(x => x.Classification).ToList();
        var latestDefinitions = definitions.GroupBy(x => x.Key).Select(x => x.First()).ToList();
        var metrics = latestDefinitions.Select(x =>
        {
            decimal? value = x.Key switch
            {
                "reply_rate" when steps.Count(s => s.SentUtc.HasValue) > 0 =>
                    replied * 100m / steps.Count(s => s.SentUtc.HasValue),
                "opportunity_rate" when campaign.Contacts.Count > 0 =>
                    deals.Count * 100m / campaign.Contacts.Count,
                "primary_objective" => progress,
                _ => null
            };
            return new CampaignMetricResponse(x.Key, x.Label, value, x.Unit, x.Target, x.Version,
                x.Key == "reply_rate" ? $"{replied} replies from {steps.Count(s => s.SentUtc.HasValue)} sent messages."
                : x.Key == "opportunity_rate" ? $"{deals.Count} linked opportunities from {campaign.Contacts.Count} campaign contacts."
                : "Calculated from the campaign's authoritative objective and linked outcomes.");
        }).ToList();
        var attribution = deals.Select(deal =>
        {
            var evidence = touches.Where(x => x.SubjectType == "deal" && x.SubjectId == deal.Id)
                .OrderBy(x => x.ObservedUtc).ToList();
            var direct = evidence.Any(x => x.InteractionType is "conversion" or "opportunity_created" or "purchase");
            return new CampaignAttributionEvidenceResponse(deal.Id, "deal", direct ? "direct stable identifier" : "influenced",
                direct ? "directly attributed" : "associated influence", direct ? 1m : 0.65m, 180,
                evidence.Select(x => x.Id).ToList());
        }).ToList();
        var timeline = steps.Where(x => x.SentUtc.HasValue)
            .Select(x => new CampaignEventResponse(x.Id, "sent", x.SentUtc!.Value, "Campaign email sent.",
                "sequence execution", x.ContactId, null, null))
            .Concat(touches.Select(x => new CampaignEventResponse(x.Id, x.InteractionType, x.ObservedUtc,
                string.IsNullOrWhiteSpace(x.Evidence) ? $"Recorded {x.InteractionType.Replace("_", " ")} event." : x.Evidence!,
                x.Provider, x.SubjectType == "contact" ? x.SubjectId : null,
                x.SubjectType == "deal" ? x.SubjectId : null, null)))
            .Concat(campaign.Activities.Where(x => x.CompletedUtc.HasValue)
                .Select(x => new CampaignEventResponse(x.Id, "activity_completed", x.CompletedUtc!.Value,
                    x.ResultSummary ?? $"{x.Name} completed.", "campaign activity", null, null, x.Id)))
            .OrderByDescending(x => x.OccurredUtc).Take(100).ToList();
        return new CampaignPerformanceResponse(campaign.Id, campaign.LifecycleStatus,
            campaign.PrimaryObjectiveType is null || campaign.PrimaryObjectiveTarget is null || campaign.PrimaryObjectiveUnit is null || campaign.PrimaryObjectiveTargetUtc is null
                ? null
                : new CampaignObjectiveResponse(campaign.PrimaryObjectiveType, campaign.PrimaryObjectiveTarget.Value,
                    campaign.PrimaryObjectiveUnit, campaign.PrimaryObjectiveTargetUtc.Value),
            progress, campaign.Contacts.Count, steps.Count(x => x.SentUtc.HasValue),
            steps.Count(x => x.DeliveryStatus == "delivered"), replied,
            steps.Count(x => x.BounceStatus != null || x.DeliveryStatus == SalesStatuses.Bounced),
            deals.Count, deals.Count(x => x.Status == SalesStatuses.Won), revenue, budget, costs, metrics,
            attribution, timeline, DateTime.UtcNow);
    }

    public async Task<CampaignPerformanceResponse?> CapturePerformanceSnapshotAsync(
        Guid companyId, Guid userId, Guid campaignId, CancellationToken cancellationToken)
    {
        var performance = await GetPerformanceAsync(companyId, campaignId, cancellationToken);
        if (performance is null) return null;
        var definitions = await _db.SalesCampaignKpiDefinitions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId)
            .GroupBy(x => x.Key)
            .Select(x => x.OrderByDescending(v => v.Version).First())
            .ToListAsync(cancellationToken);
        foreach (var definition in definitions)
        {
            var metric = performance.Metrics.FirstOrDefault(x => x.Key == definition.Key);
            if (metric is null) continue;
            var (numerator, denominator) = definition.Key switch
            {
                "reply_rate" => ((decimal?)performance.Replied, performance.Sent > 0 ? performance.Sent : null),
                "opportunity_rate" => ((decimal?)performance.Opportunities, performance.Audience > 0 ? performance.Audience : null),
                "primary_objective" => (performance.ObjectiveProgress, (decimal?)100m),
                _ => ((decimal?)null, null)
            };
            _db.SalesCampaignKpiSnapshots.Add(new SalesCampaignKpiSnapshot(
                Guid.NewGuid(), companyId, campaignId, definition.Id, definition.Version,
                numerator, denominator, metric.Value, performance.ObservedUtc, metric.EvidenceSummary));
        }
        Audit(companyId, userId, "sales.campaign.performance_snapshotted", campaignId,
            $"Captured {definitions.Count} versioned campaign KPI snapshots.");
        await _db.SaveChangesAsync(cancellationToken);
        return performance;
    }

    private async Task<List<CampaignAudiencePreviewMemberResponse>> EvaluateSegment(
        Guid companyId, SalesCampaignAudienceSegment segment, CancellationToken cancellationToken)
    {
        var contacts = await _db.Contacts.IgnoreQueryFilters().AsNoTracking().Include(x => x.CustomerCompany)
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status == SalesStatuses.Active)
            .OrderBy(x => x.FullName).ToListAsync(cancellationToken);
        var contactIds = contacts.Select(x => x.Id).ToArray();
        var permissions = await _db.SalesContactPermissions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && contactIds.Contains(x.ContactId) && x.Channel == "email")
            .OrderByDescending(x => x.ObservedUtc).ToListAsync(cancellationToken);
        var latestPermission = permissions.GroupBy(x => x.ContactId).ToDictionary(x => x.Key, x => x.First());
        var suppressions = await _db.SalesSuppressions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && (!x.ExpiresUtc.HasValue || x.ExpiresUtc > DateTime.UtcNow))
            .ToListAsync(cancellationToken);
        var criticalContacts = segment.ExcludeOpenCriticalSupportCases
            ? await _db.SupportCases.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ContactId.HasValue && x.Priority == "critical" &&
                            x.Status != "resolved" && x.Status != "closed")
                .Select(x => x.ContactId!.Value).Distinct().ToListAsync(cancellationToken)
            : [];
        var duplicateEmails = contacts.GroupBy(x => x.Email.Trim().ToLowerInvariant())
            .Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return contacts.Select(contact =>
        {
            var reason = "Matches the saved audience criteria.";
            var status = "eligible";
            var consent = latestPermission.TryGetValue(contact.Id, out var permission) ? permission.Status : "not_recorded";
            if (duplicateEmails.Contains(contact.Email)) { status = "ambiguous"; reason = "More than one contact uses this email address."; }
            else if (!string.IsNullOrWhiteSpace(segment.Industry) &&
                     !string.Equals(contact.CustomerCompany?.Industry, segment.Industry, StringComparison.OrdinalIgnoreCase))
            { status = "excluded"; reason = "Company industry does not match."; }
            else if (!string.IsNullOrWhiteSpace(segment.BuyingRole) &&
                     !(contact.Title?.Contains(segment.BuyingRole, StringComparison.OrdinalIgnoreCase) ?? false))
            { status = "excluded"; reason = "Contact role does not match."; }
            else if (!string.IsNullOrWhiteSpace(segment.PreferredLanguage) &&
                     !string.Equals(contact.PreferredLanguage, segment.PreferredLanguage, StringComparison.OrdinalIgnoreCase))
            { status = "excluded"; reason = "Preferred language does not match."; }
            else if (suppressions.Any(x => (x.ScopeType == "email" && x.ScopeValue == contact.Email) ||
                                           (x.ScopeType == "contact" && x.ScopeValue == contact.Id.ToString("D").ToLowerInvariant())))
            { status = "suppressed"; reason = "This contact is on the company suppression list."; }
            else if (criticalContacts.Contains(contact.Id))
            { status = "suppressed"; reason = "Outreach is held while a critical support case is open."; }
            else if (segment.RequireCommunicationPermission && !PositiveConsent.Contains(consent, StringComparer.OrdinalIgnoreCase))
            { status = consent == "not_recorded" ? "missing_data" : "suppressed"; reason = consent == "not_recorded" ? "Email permission has not been recorded." : "Email permission does not allow outreach."; }
            return new CampaignAudiencePreviewMemberResponse(contact.Id, contact.FullName, contact.Email,
                contact.CustomerCompanyId, contact.CustomerCompany?.Name, status, reason, consent,
                contact.PreferredLanguage);
        }).ToList();
    }

    private IQueryable<SalesCampaign> InitiativeQuery(Guid companyId, bool tracking)
    {
        var query = _db.SalesCampaigns.IgnoreQueryFilters()
            .Include(x => x.Objectives).Include(x => x.Offers).Include(x => x.Activities).Include(x => x.Contacts)
            .Where(x => x.CompanyId == companyId);
        return tracking ? query : query.AsNoTracking();
    }

    private async Task<bool> IsCompanyMember(Guid companyId, Guid userId, CancellationToken cancellationToken) =>
        await _db.CompanyMemberships.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId, cancellationToken);

    private static CampaignInitiativeResponse Map(SalesCampaign campaign) =>
        new(campaign.Id, campaign.Name, campaign.CampaignType, campaign.LifecycleStatus, campaign.Description,
            campaign.OwnerUserId, campaign.OwnerAgentId,
            campaign.PrimaryObjectiveType is null || campaign.PrimaryObjectiveTarget is null || campaign.PrimaryObjectiveUnit is null || campaign.PrimaryObjectiveTargetUtc is null
                ? null
                : new CampaignObjectiveResponse(campaign.PrimaryObjectiveType, campaign.PrimaryObjectiveTarget.Value,
                    campaign.PrimaryObjectiveUnit, campaign.PrimaryObjectiveTargetUtc.Value),
            campaign.PlanningStartsUtc, campaign.ScheduledLaunchUtc, campaign.EndsUtc, campaign.ReviewDueUtc,
            campaign.TimeZoneId, campaign.PlannedBudget, campaign.BudgetCurrency, campaign.LegacySetupRequired,
            campaign.ConcurrencyVersion, campaign.ReadinessGaps());

    private static CampaignSegmentResponse Map(SalesCampaignAudienceSegment x) =>
        new(x.Id, x.Name, x.SegmentKind, x.Version, x.IsActive, x.Industry, x.Country, x.MinEmployees, x.MaxEmployees,
            x.BuyingRole, x.CustomerLifecycle, x.ProductInterest, x.PreferredLanguage,
            x.RequireCommunicationPermission, x.ExcludeOpenCriticalSupportCases);

    private static CampaignActivityResponse Map(SalesCampaignActivity x) =>
        new(x.Id, x.Name, x.ActivityType, x.Channel, x.ExecutionMode, x.Status, x.PlannedStartUtc, x.DueUtc,
            x.OwnerUserId, x.OwnerAgentId, x.DependsOnActivityId, x.RequiredToolCapability, x.AttemptCount,
            x.ResultSummary, x.FailureReason);

    private void Audit(Guid companyId, Guid userId, string action, Guid targetId, string summary) =>
        _db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.Human, userId.ToString("D"),
            action, "sales_campaign", targetId.ToString("D"), AuditEventOutcomes.Succeeded, summary, DateTime.UtcNow));
}
