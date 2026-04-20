using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyBriefingService : ICompanyBriefingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string BriefingConversationChannel = "executive_briefing";
    private const string SystemSenderType = "system";
    private static readonly TimeSpan AggregateFreshnessWindow = TimeSpan.FromHours(2);

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IExecutiveDashboardAggregateCache _aggregateCache;
    private readonly ILogger<CompanyBriefingService> _logger;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    private readonly IBriefingUpdateJobProducer _briefingUpdateJobProducer;
    private readonly IBriefingInsightAggregationService _insightAggregationService;
    private readonly IFinanceCashPositionWorkflowService _financeCashPositionWorkflowService;

    public CompanyBriefingService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IExecutiveDashboardAggregateCache aggregateCache,
        ICompanyOutboxEnqueuer outboxEnqueuer,
        IBriefingUpdateJobProducer briefingUpdateJobProducer,
        IBriefingInsightAggregationService insightAggregationService,
        IFinanceCashPositionWorkflowService financeCashPositionWorkflowService,
        ILogger<CompanyBriefingService> logger)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _aggregateCache = aggregateCache;
        _outboxEnqueuer = outboxEnqueuer;
        _logger = logger;
        _insightAggregationService = insightAggregationService;
        _briefingUpdateJobProducer = briefingUpdateJobProducer;
        _financeCashPositionWorkflowService = financeCashPositionWorkflowService;
    }

    public async Task<BriefingAggregateResultDto> AggregateAsync(
        Guid companyId,
        GenerateCompanyBriefingCommand command,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new BriefingValidationException(new Dictionary<string, string[]> { [nameof(companyId)] = ["Company id is required."] });
        }

        await RequireMembershipAsync(companyId, cancellationToken);

        var briefingType = CompanyBriefingTypeValues.Parse(command.BriefingType);
        var nowUtc = NormalizeUtc(command.NowUtc ?? DateTime.UtcNow);
        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");
        var window = ResolveWindow(company.Timezone, briefingType, nowUtc);
        var aggregate = await BuildAggregateResultAsync(companyId, briefingType, window, cancellationToken);
        await CacheAggregateAsync(aggregate, cancellationToken);
        return aggregate;
    }

    public async Task<CompanyBriefingGenerationResult> GenerateAsync(
        Guid companyId,
        GenerateCompanyBriefingCommand command,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new BriefingValidationException(new Dictionary<string, string[]> { [nameof(companyId)] = ["Company id is required."] });
        }

        var briefingType = CompanyBriefingTypeValues.Parse(command.BriefingType);
        var nowUtc = NormalizeUtc(command.NowUtc ?? DateTime.UtcNow);
        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var window = ResolveWindow(company.Timezone, briefingType, nowUtc);
        var existing = await _dbContext.CompanyBriefings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Sections.Where(section => section.CompanyId == companyId))
                .ThenInclude(section => section.Contributions.Where(contribution => contribution.CompanyId == companyId))
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.BriefingType == briefingType &&
                     x.PeriodStartUtc == window.StartUtc &&
                     x.PeriodEndUtc == window.EndUtc,
                cancellationToken);

        if (existing is not null && !command.Force)
        {
            return new CompanyBriefingGenerationResult(await ToDtoAsync(existing, cancellationToken), true, 0);
        }

        var ownerUserId = await ResolveOwnerUserIdAsync(companyId, cancellationToken);
        var effectivePreferences = ownerUserId.HasValue
            ? await ResolveEffectivePreferenceSnapshotAsync(companyId, ownerUserId.Value, cancellationToken)
            : TenantBriefingDefault.CreateSystemDefault(companyId, Guid.Empty);
        var aggregate = await ResolveAggregateForGenerationAsync(companyId, briefingType, window, cancellationToken);
        aggregate = ApplyPreferences(aggregate, effectivePreferences);
        var title = briefingType == CompanyBriefingType.Daily
            ? $"Daily briefing for {company.Name}"
            : $"Weekly executive summary for {company.Name}";
        var body = RenderBody(company.Name, briefingType, window, aggregate);
        var structuredPayload = BuildPayload(company.Name, briefingType, window, aggregate, effectivePreferences);
        var sourceRefs = BuildSourceReferences(aggregate.SourceReferences);

        var conversation = ownerUserId.HasValue
            ? await ResolveBriefingConversationAsync(companyId, ownerUserId.Value, cancellationToken)
            : null;
        var message = conversation is null
            ? null
            : new Message(
                Guid.NewGuid(),
                companyId,
                conversation.Id,
                SystemSenderType,
                senderId: null,
                briefingType.ToStorageValue(),
                body,
                structuredPayload);

        if (message is not null)
        {
            _dbContext.Messages.Add(message);
            conversation.Touch();
        }

        var briefing = new CompanyBriefing(
            Guid.NewGuid(),
            companyId,
            briefingType,
            window.StartUtc,
            window.EndUtc,
            title,
            body,
            structuredPayload,
            sourceRefs,
            BuildPreferenceSnapshot(effectivePreferences),
            message?.Id);
        _dbContext.CompanyBriefings.Add(briefing);
        AddStructuredSections(companyId, briefing.Id, aggregate.StructuredSections);

        var notificationsCreated = await AddNotificationsAsync(briefing, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Generated {BriefingType} briefing {BriefingId} for company {CompanyId}; notifications created: {NotificationsCreated}.",
            briefingType.ToStorageValue(),
            briefing.Id,
            companyId,
            notificationsCreated);

        return new CompanyBriefingGenerationResult(await ToDtoAsync(briefing, cancellationToken), false, notificationsCreated);
    }

    public async Task<BriefingSchedulerRunResult> GenerateDueAsync(
        GenerateDueBriefingsCommand command,
        CancellationToken cancellationToken)
    {
        var nowUtc = NormalizeUtc(command.NowUtc);
        var companyIds = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new DueCompany(x.Id, x.Name, x.Timezone))
            .Take(Math.Max(1, command.BatchSize))
            .ToListAsync(cancellationToken);

        var generated = 0;
        var notifications = 0;
        var failures = 0;

        foreach (var company in companyIds)
        {
            var companyId = company.Id;
            using var scope = _companyExecutionScopeFactory.BeginScope(companyId);
            foreach (var briefingType in new[] { CompanyBriefingType.Daily, CompanyBriefingType.Weekly })
            {
                try
                {
                    if (!await IsDueForScheduledGenerationAsync(company, briefingType, nowUtc, cancellationToken))
                    {
                        continue;
                    }

                    var result = await EnqueueScheduledBriefingJobAsync(company, briefingType, nowUtc, cancellationToken);
                    if (result.Created)
                    {
                        generated++;
                        _logger.LogInformation(
                            "Scheduled {BriefingType} briefing update job {JobId} for company {CompanyId}.",
                            briefingType.ToStorageValue(),
                            result.JobId,
                            companyId);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    _logger.LogError(ex, "Failed to generate {BriefingType} briefing for company {CompanyId}.", briefingType.ToStorageValue(), companyId);
                }
            }
        }

        return new BriefingSchedulerRunResult(true, companyIds.Count, generated, notifications, failures);
    }

    public async Task<DashboardBriefingCardDto> GetLatestDashboardBriefingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var daily = await GetLatestBriefingAsync(companyId, CompanyBriefingType.Daily, cancellationToken);
        var weekly = await GetLatestBriefingAsync(companyId, CompanyBriefingType.Weekly, cancellationToken);
        return new DashboardBriefingCardDto(
            daily is null ? null : await ToDtoAsync(daily, cancellationToken),
            weekly is null ? null : await ToDtoAsync(weekly, cancellationToken));
    }

    private async Task<CompanyBriefing?> GetLatestBriefingAsync(
        Guid companyId,
        CompanyBriefingType briefingType,
        CancellationToken cancellationToken) =>
        await _dbContext.CompanyBriefings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BriefingType == briefingType)
            .OrderByDescending(x => x.GeneratedUtc)
            .Include(x => x.Sections.Where(section => section.CompanyId == companyId))
                .ThenInclude(section => section.Contributions.Where(contribution => contribution.CompanyId == companyId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<CompanyBriefingDeliveryPreferenceDto> GetDeliveryPreferenceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var preference = await _dbContext.CompanyBriefingDeliveryPreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == membership.UserId, cancellationToken);

        return preference is null
            ? new CompanyBriefingDeliveryPreferenceDto(companyId, membership.UserId, true, false, true, true, new TimeOnly(8, 0), null, null)
            : ToDto(preference);
    }

    public async Task<CompanyBriefingDeliveryPreferenceDto> UpdateDeliveryPreferenceAsync(
        Guid companyId,
        UpdateCompanyBriefingDeliveryPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var preference = await _dbContext.CompanyBriefingDeliveryPreferences
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == membership.UserId, cancellationToken);

        if (preference is null)
        {
            preference = new CompanyBriefingDeliveryPreference(Guid.NewGuid(), companyId, membership.UserId);
            _dbContext.CompanyBriefingDeliveryPreferences.Add(preference);
        }

        preference.Update(command.InAppEnabled, command.MobileEnabled, command.DailyEnabled, command.WeeklyEnabled, command.PreferredDeliveryTime ?? new TimeOnly(8, 0), command.PreferredTimezone);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(preference);
    }

    public async Task<BriefingPreferenceDto> GetUserBriefingPreferenceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        return ToDto(await ResolveEffectivePreferenceSnapshotAsync(companyId, membership.UserId, cancellationToken));
    }

    public async Task<BriefingPreferenceDto> UpsertUserBriefingPreferenceAsync(
        Guid companyId,
        UpsertBriefingPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var parsed = ParsePreferenceCommand(command);
        var preference = await _dbContext.UserBriefingPreferences
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == membership.UserId, cancellationToken);

        if (preference is null)
        {
            preference = new UserBriefingPreference(Guid.NewGuid(), companyId, membership.UserId, parsed.Frequency, parsed.FocusAreas, parsed.PriorityThreshold);
            _dbContext.UserBriefingPreferences.Add(preference);
        }
        else
        {
            preference.Update(parsed.Frequency, parsed.FocusAreas, parsed.PriorityThreshold);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(preference);
    }

    public async Task<TenantBriefingDefaultDto?> GetTenantBriefingDefaultAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var defaults = await _dbContext.TenantBriefingDefaults
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        return defaults is null ? null : ToDto(defaults);
    }

    public async Task<TenantBriefingDefaultDto> UpsertTenantBriefingDefaultAsync(
        Guid companyId,
        UpsertBriefingPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var parsed = ParsePreferenceCommand(command);
        var defaults = await _dbContext.TenantBriefingDefaults
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (defaults is null)
        {
            defaults = new TenantBriefingDefault(Guid.NewGuid(), companyId, parsed.Frequency, parsed.FocusAreas, parsed.PriorityThreshold);
            _dbContext.TenantBriefingDefaults.Add(defaults);
        }
        else
        {
            defaults.Update(parsed.Frequency, parsed.FocusAreas, parsed.PriorityThreshold);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(defaults);
    }

    public async Task<EffectiveBriefingPreferenceDto> ResolveEffectiveBriefingPreferenceAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var snapshot = await ResolveEffectivePreferenceSnapshotAsync(companyId, userId, cancellationToken);
        return new EffectiveBriefingPreferenceDto(
            snapshot.CompanyId,
            snapshot.UserId,
            snapshot.Source.ToStorageValue(),
            snapshot.DeliveryFrequency.ToStorageValue(),
            snapshot.IncludedFocusAreas,
            snapshot.PriorityThreshold.ToStorageValue(),
            snapshot.PreferenceUpdatedUtc);
    }

    private async Task<bool> IsDueForScheduledGenerationAsync(DueCompany company, CompanyBriefingType briefingType, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var activeUserIds = await _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == company.Id && x.Status == CompanyMembershipStatus.Active && x.UserId.HasValue)
            .Select(x => x.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (activeUserIds.Count == 0)
        {
            return false;
        }

        var preferences = await _dbContext.CompanyBriefingDeliveryPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == company.Id && activeUserIds.Contains(x.UserId))
            .ToListAsync(cancellationToken);
        var userPreferences = await _dbContext.UserBriefingPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == company.Id && activeUserIds.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, cancellationToken);
        var tenantDefault = await _dbContext.TenantBriefingDefaults
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == company.Id, cancellationToken);

        var cadenceEnabled = activeUserIds.Any(userId =>
        {
            var preference = preferences.SingleOrDefault(x => x.UserId == userId);
            if (preference?.InAppEnabled == false)
            {
                return false;
            }

            var cadenceAllowed = briefingType == CompanyBriefingType.Daily
                ? preference?.DailyEnabled ?? true
                : preference?.WeeklyEnabled ?? true;

            return cadenceAllowed &&
                IsBriefingFrequencyEnabled(
                    userPreferences.TryGetValue(userId, out var briefingPreference)
                        ? briefingPreference.DeliveryFrequency
                        : tenantDefault?.DeliveryFrequency ?? BriefingDeliveryFrequency.DailyAndWeekly,
                    briefingType);
        });

        if (!cadenceEnabled)
        {
            return false;
        }

        var deliveryTime = preferences
            .Where(x => x.InAppEnabled && (briefingType == CompanyBriefingType.Daily ? x.DailyEnabled : x.WeeklyEnabled))
            .Select(x => x.PreferredDeliveryTime)
            .DefaultIfEmpty(new TimeOnly(8, 0))
            .Min();
        var zone = ResolveTimezone(preferences.Select(x => x.PreferredTimezone).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? company.Timezone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        return TimeOnly.FromDateTime(localNow) >= deliveryTime;
    }

    private Task<BriefingUpdateJobEnqueueResult> EnqueueScheduledBriefingJobAsync(
        DueCompany company,
        CompanyBriefingType briefingType,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var window = ResolveWindow(company.Timezone, briefingType, nowUtc);
        var triggerType = briefingType == CompanyBriefingType.Daily
            ? CompanyBriefingUpdateJobTriggerTypeValues.Daily
            : CompanyBriefingUpdateJobTriggerTypeValues.Weekly;
        var briefingTypeValue = briefingType.ToStorageValue();
        var idempotencyKey = $"briefing:{company.Id:N}:{triggerType}:{window.StartUtc:yyyyMMddHHmmss}:{window.EndUtc:yyyyMMddHHmmss}";
        var correlationId = $"briefing-schedule:{idempotencyKey}";

        return _briefingUpdateJobProducer.EnqueueScheduledAsync(
            company.Id,
            triggerType,
            briefingTypeValue,
            correlationId,
            idempotencyKey,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["triggerSource"] = JsonValue.Create(BriefingUpdateJobSources.Schedule),
                ["briefingType"] = JsonValue.Create(briefingTypeValue),
                ["scheduleCadence"] = JsonValue.Create(triggerType),
                ["scheduledAtUtc"] = JsonValue.Create(nowUtc),
                ["periodStartUtc"] = JsonValue.Create(window.StartUtc),
                ["periodEndUtc"] = JsonValue.Create(window.EndUtc),
                ["scheduleWindowKey"] = JsonValue.Create($"{triggerType}:{window.StartUtc:O}:{window.EndUtc:O}"),
                ["pipeline"] = JsonValue.Create("company_briefing_generation")
            },
            cancellationToken);
    }

    private async Task<int> AddNotificationsAsync(CompanyBriefing briefing, CancellationToken cancellationToken)
    {
        var users = await _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == briefing.CompanyId && x.Status == CompanyMembershipStatus.Active && x.UserId.HasValue)
            .Select(x => x.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
        {
            return 0;
        }

        var preferences = await _dbContext.CompanyBriefingDeliveryPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == briefing.CompanyId && users.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, cancellationToken);
        var userPreferences = await _dbContext.UserBriefingPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == briefing.CompanyId && users.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, cancellationToken);
        var tenantDefault = await _dbContext.TenantBriefingDefaults
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == briefing.CompanyId, cancellationToken);

        var created = 0;
        foreach (var userId in users)
        {
            preferences.TryGetValue(userId, out var preference);
            var briefingFrequency = userPreferences.TryGetValue(userId, out var briefingPreference)
                ? briefingPreference.DeliveryFrequency
                : tenantDefault?.DeliveryFrequency ?? BriefingDeliveryFrequency.DailyAndWeekly;
            var cadenceEnabled = briefing.BriefingType == CompanyBriefingType.Daily
                ? preference?.DailyEnabled ?? true
                : preference?.WeeklyEnabled ?? true;
            if (!cadenceEnabled || !IsBriefingFrequencyEnabled(briefingFrequency, briefing.BriefingType))
            {
                continue;
            }

            if (preference?.InAppEnabled ?? true)
            {
                _outboxEnqueuer.Enqueue(
                    briefing.CompanyId,
                    CompanyOutboxTopics.NotificationDeliveryRequested,
                    new NotificationDeliveryRequestedMessage(
                        briefing.CompanyId,
                        CompanyNotificationType.BriefingAvailable.ToStorageValue(),
                        CompanyNotificationPriority.Normal.ToStorageValue(),
                        briefing.Title,
                        briefing.SummaryBody,
                        "company_briefing",
                        briefing.Id,
                        $"/dashboard?companyId={briefing.CompanyId}",
                        userId,
                        null,
                        briefing.Id,
                        null,
                        $"briefing-available:{briefing.Id:N}:{userId:N}",
                        null),
                    idempotencyKey: $"notification:briefing-available:{briefing.Id:N}:{userId:N}",
                    causationId: briefing.Id.ToString("N"));
                created++;
            }
        }

        return created;
    }

    private async Task<BriefingAggregate> ResolveAggregateForGenerationAsync(Guid companyId, CompanyBriefingType type, BriefingWindow window, CancellationToken cancellationToken)
    {
        var briefingType = type.ToStorageValue();
        var snapshot = await _aggregateCache.TryGetAsync(companyId, briefingType, window.StartUtc, window.EndUtc, cancellationToken);
        if (snapshot is not null && IsFresh(snapshot.GeneratedUtc))
        {
            _logger.LogInformation(
                "Using cached dashboard aggregate for {BriefingType} briefing generation for company {CompanyId}.",
                briefingType,
                companyId);
            return ToBriefingAggregate(snapshot.Aggregate);
        }

        if (snapshot is null)
        {
            _logger.LogInformation(
                "Dashboard aggregate cache miss for {BriefingType} briefing generation for company {CompanyId}; rebuilding.",
                briefingType,
                companyId);
        }
        else
        {
            _logger.LogInformation(
                "Dashboard aggregate cache entry for {BriefingType} briefing generation for company {CompanyId} is stale; rebuilding.",
                briefingType,
                companyId);
        }

        return await BuildAggregateAsync(companyId, type, window, cancellationToken);
    }

    private async Task<BriefingAggregate> BuildAggregateAsync(Guid companyId, CompanyBriefingType type, BriefingWindow window, CancellationToken cancellationToken)
    {
        var aggregate = await BuildAggregateResultAsync(companyId, type, window, cancellationToken);
        await CacheAggregateAsync(aggregate, cancellationToken);
        return ToBriefingAggregate(aggregate);
    }

    private async Task<BriefingAggregateResultDto> BuildAggregateResultAsync(Guid companyId, CompanyBriefingType type, BriefingWindow window, CancellationToken cancellationToken)
    {
        var severityRules = await LoadSeverityRulesAsync(companyId, cancellationToken);
        var briefingType = type.ToStorageValue();

        var openApprovalsCount = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending, cancellationToken);
        var blockedWorkflowCount = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId && x.State == WorkflowInstanceStatus.Blocked, cancellationToken);
        var cashPosition = await _financeCashPositionWorkflowService.EvaluateAsync(
            new EvaluateFinanceCashPositionWorkflowCommand(companyId),
            cancellationToken);
        var criticalAlertsCount = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId &&
                             x.Severity == AlertSeverity.Critical &&
                             x.Status != AlertStatus.Resolved &&
                             x.Status != AlertStatus.Closed, cancellationToken);
        var approvalRows = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(10)
            .Select(x => new { x.Id, x.AgentId, x.ApprovalType, x.TargetEntityType, x.TargetEntityId, x.Status, x.CreatedUtc, x.ToolExecutionAttemptId })
            .ToListAsync(cancellationToken);
        var pendingApprovals = approvalRows
            .Select(x => ApplyPriority(new BriefingAggregateItemDto(
                "pending_approvals",
                $"{x.ApprovalType} approval pending",
                $"Review {x.TargetEntityType} {x.TargetEntityId:N}.",
                "medium",
                x.Status.ToStorageValue(),
                "approval_request",
                x.Id,
                x.CreatedUtc,
                [],
                [CreateApprovalReference(companyId, x.Id, x.ApprovalType, x.Status.ToStorageValue())]),
                ResolvePriority(severityRules, "pending_approvals", "approval", "status", x.Status.ToStorageValue())) with
            {
                AgentId = x.AgentId == Guid.Empty ? null : x.AgentId,
                CompanyEntityId = x.TargetEntityId,
                Assessment = x.Status.ToStorageValue()
            })
            .ToList();

        var alertRows = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status != AlertStatus.Resolved &&
                        x.Status != AlertStatus.Closed)
            .OrderByDescending(x => x.LastDetectedUtc ?? x.UpdatedUtc)
            .Take(10)
            .Select(x => new { x.Id, x.SourceAgentId, x.Severity, x.Status, x.Title, x.Summary, x.LastDetectedUtc, x.UpdatedUtc, x.Metadata })
            .ToListAsync(cancellationToken);
        var alerts = alertRows
            .Select(x => ApplyPriority(new BriefingAggregateItemDto(
                "alerts",
                x.Title,
                x.Summary,
                x.Severity.ToStorageValue(),
                x.Status.ToStorageValue(),
                "alert",
                x.Id,
                x.LastDetectedUtc ?? x.UpdatedUtc,
                x.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
                [new BriefingSourceReferenceDto("alert", x.Id, x.Title, x.Status.ToStorageValue(), $"/dashboard?companyId={companyId}")]),
                ResolvePriority(severityRules, "alerts", "alert", "severity", x.Severity.ToStorageValue())) with
            {
                AgentId = x.SourceAgentId,
                CompanyEntityId = x.Id,
                Assessment = x.Severity.ToStorageValue()
            })
            .ToList();

        var overdueTaskRows = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.DueUtc.HasValue &&
                        x.DueUtc.Value < window.EndUtc &&
                        x.Status != WorkTaskStatus.Completed &&
                        x.Status != WorkTaskStatus.Failed)
            .Select(x => new { x.Id })
            .ToListAsync(cancellationToken);

        var taskRows = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.UpdatedUtc >= window.StartUtc && x.UpdatedUtc < window.EndUtc)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(25)
            .Select(x => new { x.Id, x.Title, x.Status, x.Priority, x.AssignedAgentId, x.WorkflowInstanceId, x.CorrelationId, x.RationaleSummary, x.ConfidenceScore, x.DueUtc, x.UpdatedUtc })
            .ToListAsync(cancellationToken);
        var overdueTaskIds = overdueTaskRows.Select(x => x.Id).ToHashSet();
        var kpiHighlights = taskRows
            .Where(x => x.Status == WorkTaskStatus.Completed)
            .Take(10)
            .Select(x => ApplyPriority(new BriefingAggregateItemDto(
                "kpi_highlights",
                x.Title,
                "Task completed during the briefing period.",
                null,
                x.Status.ToStorageValue(),
                "task",
                x.Id,
                x.UpdatedUtc,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["priority"] = JsonValue.Create(x.Priority.ToStorageValue())
                },
                CreateTaskReferences(companyId, x.Id, x.Title, x.Status.ToStorageValue(), x.WorkflowInstanceId))
            , ResolvePriority(severityRules, "kpi_highlights", "task", "status", x.Status.ToStorageValue())) with
            {
                AgentId = x.AssignedAgentId,
                CompanyEntityId = x.Id,
                WorkflowInstanceId = x.WorkflowInstanceId,
                TaskId = x.Id,
                EventCorrelationId = x.CorrelationId,
                Assessment = x.Status.ToStorageValue()
            })
            .ToList();
        var anomalies = taskRows
            .Where(x => x.Status is WorkTaskStatus.Blocked or WorkTaskStatus.Failed or WorkTaskStatus.AwaitingApproval)
            .Take(10)
            .Select(x => ApplyPriority(new BriefingAggregateItemDto(
                "anomalies",
                x.Title,
                $"Task is {x.Status.ToStorageValue()}.",
                x.Status is WorkTaskStatus.Blocked or WorkTaskStatus.Failed ? "high" : "medium",
                x.Status.ToStorageValue(),
                "task",
                x.Id,
                x.UpdatedUtc,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["priority"] = JsonValue.Create(x.Priority.ToStorageValue())
                },
                CreateTaskReferences(companyId, x.Id, x.Title, x.Status.ToStorageValue(), x.WorkflowInstanceId))
            , overdueTaskIds.Contains(x.Id) ? ResolvePriority(severityRules, "anomalies", "task", "due", "overdue")
                : ResolvePriority(severityRules, "anomalies", "task", "status", x.Status.ToStorageValue())) with
            {
                AgentId = x.AssignedAgentId,
                CompanyEntityId = x.Id,
                WorkflowInstanceId = x.WorkflowInstanceId,
                TaskId = x.Id,
                EventCorrelationId = x.CorrelationId,
                Assessment = x.Status.ToStorageValue()
            })
            .ToList();

        var notableAgentRows = await _dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.OccurredUtc >= window.StartUtc && x.OccurredUtc < window.EndUtc)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(10)
            .Select(x => new { x.Id, x.Action, x.TargetType, x.TargetId, x.Outcome, x.RationaleSummary, x.OccurredUtc })
            .ToListAsync(cancellationToken);
        var notableAgentUpdates = notableAgentRows
            .Select(x => ApplyPriority(new BriefingAggregateItemDto(
                "agent_updates",
                x.Action,
                string.IsNullOrWhiteSpace(x.RationaleSummary) ? $"{x.TargetType} {x.Outcome}." : x.RationaleSummary!,
                null,
                x.Outcome,
                "audit_event",
                x.Id,
                x.OccurredUtc,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["targetType"] = JsonValue.Create(x.TargetType),
                    ["targetId"] = JsonValue.Create(x.TargetId)
                },
                [new BriefingSourceReferenceDto("audit_event", x.Id, x.Action, x.Outcome, null)]),
                new BriefingPriorityResolution(BriefingSectionPriorityCategory.Informational, 15, "informational_agent_update")))
            .ToList();

        kpiHighlights.Add(ApplyPriority(new BriefingAggregateItemDto(
            "kpi_highlights",
            "Cash runway",
            $"Available cash is {cashPosition.AvailableBalance:0.##} {cashPosition.Currency}; estimated runway is {(cashPosition.EstimatedRunwayDays.HasValue ? $"{cashPosition.EstimatedRunwayDays.Value} days" : "unavailable")}. {cashPosition.Rationale}",
            cashPosition.AlertState.IsLowCash ? cashPosition.RiskLevel : null,
            cashPosition.AlertState.AlertStatus,
            "finance_cash_position",
            companyId,
            cashPosition.AsOfUtc,
            BuildCashPositionMetadata(cashPosition),
            []), ResolvePriority(severityRules, "kpi_highlights", "task", "status", "completed")));
        if (cashPosition.AlertState.IsLowCash)
        {
            alerts.Add(ApplyPriority(new BriefingAggregateItemDto(
                "alerts",
                "Low cash alert",
                cashPosition.Rationale,
                cashPosition.RiskLevel,
                cashPosition.AlertState.AlertStatus,
                "finance_cash_position",
                cashPosition.AlertState.AlertId ?? companyId,
                cashPosition.AsOfUtc,
                BuildCashPositionMetadata(cashPosition),
                [new BriefingSourceReferenceDto("finance_cash_position", cashPosition.AlertState.AlertId ?? companyId, "Cash position", cashPosition.AlertState.AlertStatus, $"/dashboard?companyId={companyId}")]),
                ResolvePriority(severityRules, "alerts", "alert", "severity", cashPosition.RiskLevel)));
        }

        alerts = OrderAggregateItems(alerts);
        pendingApprovals = OrderAggregateItems(pendingApprovals);
        anomalies = OrderAggregateItems(anomalies);
        kpiHighlights = OrderAggregateItems(kpiHighlights);
        notableAgentUpdates = OrderAggregateItems(notableAgentUpdates);

        var result = new BriefingAggregateResultDto(companyId, briefingType, window.StartUtc, window.EndUtc, alerts, pendingApprovals, kpiHighlights, anomalies, notableAgentUpdates)
        {
            SummaryCounts = new BriefingSummaryCountsDto(
                criticalAlertsCount,
                openApprovalsCount,
                blockedWorkflowCount,
                overdueTaskRows.Count),
            CashPosition = cashPosition
        };
        var insightPayload = _insightAggregationService.Aggregate(new BriefingInsightAggregationRequest(companyId, null, BuildInsightContributions(companyId, result)));
        var structuredSections = await EnrichStructuredSectionsAsync(companyId, OrderStructuredSections(insightPayload.Sections, result), result, cancellationToken);
        return result with
        {
            NarrativeText = insightPayload.NarrativeText,
            StructuredSections = structuredSections
        };
    }

    private static List<BriefingAggregateItemDto> OrderAggregateItems(IEnumerable<BriefingAggregateItemDto> items) =>
        items
            .OrderByDescending(x => x.PriorityScore)
            .ThenByDescending(x => (int)x.PriorityCategory)
            .ThenByDescending(x => x.OccurredUtc)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static BriefingAggregateItemDto ApplyPriority(BriefingAggregateItemDto item, BriefingPriorityResolution priority) =>
        item with
        {
            PriorityCategory = priority.Category,
            PriorityScore = priority.Score,
            PriorityRuleCode = priority.RuleCode
        };

    private Task CacheAggregateAsync(BriefingAggregateResultDto aggregate, CancellationToken cancellationToken) =>
        _aggregateCache.SetAsync(
            new CachedExecutiveDashboardAggregateDto(
                aggregate.CompanyId,
                aggregate.BriefingType,
                aggregate.PeriodStartUtc,
                aggregate.PeriodEndUtc,
                DateTime.UtcNow,
                aggregate),
            cancellationToken);

    private static bool IsFresh(DateTime generatedUtc) =>
        NormalizeUtc(generatedUtc) >= DateTime.UtcNow.Subtract(AggregateFreshnessWindow);

    private static BriefingAggregate ApplyPreferences(BriefingAggregate aggregate, BriefingPreferenceSnapshot preferences)
    {
        var focusAreas = preferences.IncludedFocusAreas.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var threshold = preferences.PriorityThreshold;
        var sections = aggregate.StructuredSections
            .Where(section => focusAreas.Contains(NormalizeFocusArea(section.SectionType)) || focusAreas.Contains(NormalizeFocusArea(section.GroupingType)))
            .Where(section => BriefingSectionPriorityCategoryValues.Parse(section.PriorityCategory) >= threshold)
            .ToList();

        var allowedContributionIds = sections
            .SelectMany(section => section.Contributions.Select(contribution => contribution.SourceReference.EntityId))
            .ToHashSet();

        IReadOnlyList<BriefingItem> FilterItems(string area, IReadOnlyList<BriefingItem> items) =>
            focusAreas.Contains(area)
                ? items
                    .Where(item => item.PriorityCategory >= threshold)
                    .Where(item => sections.Count == 0 || item.EntityId == Guid.Empty || allowedContributionIds.Count == 0 || allowedContributionIds.Contains(item.EntityId))
                    .ToList()
                : [];

        var filtered = aggregate with
        {
            Approvals = FilterItems(BriefingFocusAreaValues.PendingApprovals, aggregate.Approvals),
            KpiHighlights = FilterItems(BriefingFocusAreaValues.KpiHighlights, aggregate.KpiHighlights),
            Alerts = FilterItems(BriefingFocusAreaValues.Alerts, aggregate.Alerts),
            Anomalies = FilterItems(BriefingFocusAreaValues.Anomalies, aggregate.Anomalies),
            AgentHighlights = FilterItems(BriefingFocusAreaValues.NotableAgentUpdates, aggregate.AgentHighlights),
            StructuredSections = sections
        };

        var references = DeduplicateReferences(
            filtered.Approvals.SelectMany(x => x.References)
                .Concat(filtered.KpiHighlights.SelectMany(x => x.References))
                .Concat(filtered.Alerts.SelectMany(x => x.References))
                .Concat(filtered.Anomalies.SelectMany(x => x.References))
                .Concat(filtered.AgentHighlights.SelectMany(x => x.References)));

        return filtered with { SourceReferences = references };
    }

    private static string NormalizeFocusArea(string value) =>
        value.Equals("approval", StringComparison.OrdinalIgnoreCase) ? BriefingFocusAreaValues.PendingApprovals :
        value.Equals("agent_updates", StringComparison.OrdinalIgnoreCase) ? BriefingFocusAreaValues.NotableAgentUpdates :
        value.Trim().ToLowerInvariant();

    private static BriefingAggregate ToBriefingAggregate(BriefingAggregateResultDto aggregate)
    {
        var approvals = aggregate.PendingApprovals.Select(ToBriefingItem).ToList();
        var kpiHighlights = aggregate.KpiHighlights.Select(ToBriefingItem).ToList();
        var alerts = aggregate.Alerts.Select(ToBriefingItem).ToList();
        var anomalies = aggregate.Anomalies.Select(ToBriefingItem).ToList();
        var agentHighlights = aggregate.NotableAgentUpdates.Select(ToBriefingItem).ToList();
        var sourceReferences = DeduplicateReferences(
            approvals.SelectMany(x => x.References)
                .Concat(kpiHighlights.SelectMany(x => x.References))
                .Concat(alerts.SelectMany(x => x.References))
                .Concat(anomalies.SelectMany(x => x.References))
                .Concat(agentHighlights.SelectMany(x => x.References)));

        return new BriefingAggregate(
            approvals, kpiHighlights, alerts, anomalies, agentHighlights, sourceReferences,
            aggregate.NarrativeText, aggregate.StructuredSections, aggregate.CashPosition, aggregate.SummaryCounts);
    }

    private static BriefingItem ToBriefingItem(BriefingAggregateItemDto item)
    {
        var text = string.IsNullOrWhiteSpace(item.Summary)
            ? item.Title
            : $"{item.Title} - {item.Summary}";
        return new BriefingItem(
            item.Section,
            text,
            item.SourceEntityId ?? Guid.Empty,
            item.OccurredUtc,
            item.References.Count > 0
                ? item.References
                : CreateSourceReference(item),
            item.PriorityCategory,
            item.PriorityScore,
            item.Metadata);
    }

    private static IReadOnlyList<BriefingInsightContributionDto> BuildInsightContributions(Guid companyId, BriefingAggregateResultDto aggregate) =>
        aggregate.Alerts
            .Concat(aggregate.PendingApprovals)
            .Concat(aggregate.KpiHighlights)
            .Concat(aggregate.Anomalies)
            .Concat(aggregate.NotableAgentUpdates)
            .Select(item => new BriefingInsightContributionDto(
                companyId,
                null,
                item.AgentId ?? Guid.Empty,
                item.References.FirstOrDefault() ?? CreateSourceReference(item).FirstOrDefault() ?? new BriefingSourceReferenceDto(item.SourceEntityType ?? "briefing_item", item.SourceEntityId ?? Guid.Empty, item.Title, item.Status, null),
                item.OccurredUtc,
                TryReadDecimal(item.Metadata, "confidence"),
                item.CompanyEntityId ?? item.SourceEntityId,
                item.WorkflowInstanceId,
                item.TaskId,
                item.EventCorrelationId,
                item.Title,
                string.IsNullOrWhiteSpace(item.Summary) ? item.Title : item.Summary,
                item.Assessment ?? item.Status ?? item.Severity,
                item.Metadata)
            {
                ConfidenceMetadata = item.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase)
            })
            .ToList();

    private void AddStructuredSections(Guid companyId, Guid briefingId, IReadOnlyList<AggregatedBriefingSectionDto> sections)
    {
        foreach (var section in sections)
        {
            var sectionEntity = new CompanyBriefingSection(
                Guid.NewGuid(),
                companyId,
                briefingId,
                section.SectionKey,
                section.Title,
                section.GroupingType,
                section.GroupingKey,
                section.Narrative,
                section.IsConflicting,
                section.IsConflicting ? section.Narrative : null,
                section.Contributions.Select(x => x.CompanyEntityId).FirstOrDefault(x => x.HasValue),
                section.SectionType,
                BriefingSectionPriorityCategoryValues.Parse(section.PriorityCategory),
                section.PriorityScore,
                section.PriorityRuleCode,
                section.Contributions.Select(x => x.WorkflowInstanceId).FirstOrDefault(x => x.HasValue),
                section.Contributions.Select(x => x.TaskId).FirstOrDefault(x => x.HasValue),
                section.Contributions.Select(x => x.EventCorrelationId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                BuildSectionSourceReferences(section));

            _dbContext.CompanyBriefingSections.Add(sectionEntity);

            foreach (var contribution in section.Contributions.Where(x => x.CompanyId == companyId))
            {
                var reference = contribution.SourceReference;
                var confidenceMetadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["confidence"] = JsonValue.Create(contribution.Confidence)
                };
                foreach (var pair in contribution.Metadata)
                {
                    confidenceMetadata[pair.Key] = pair.Value?.DeepClone();
                }

                _dbContext.CompanyBriefingContributions.Add(new CompanyBriefingContribution(
                    contribution.ContributionId ?? Guid.NewGuid(),
                    companyId,
                    sectionEntity.Id,
                    contribution.AgentId,
                    reference.EntityType,
                    reference.EntityId,
                    reference.Label,
                    reference.Status,
                    reference.Route,
                    contribution.TimestampUtc,
                    contribution.Confidence,
                    contribution.ConfidenceMetadata.Count == 0 ? confidenceMetadata : contribution.ConfidenceMetadata,
                    contribution.CompanyEntityId,
                    contribution.WorkflowInstanceId,
                    contribution.TaskId,
                    contribution.EventCorrelationId,
                    contribution.Topic,
                    contribution.Narrative,
                    contribution.Assessment,
                    contribution.Metadata));
            }
        }
    }

    private static IReadOnlyList<AggregatedBriefingSectionDto> ToStructuredSectionDtos(CompanyBriefing briefing)
    {
        if (briefing.Sections.Count == 0)
        {
            return [];
        }

        return briefing.Sections
            .Where(section => section.CompanyId == briefing.CompanyId)
            .OrderByDescending(section => section.PriorityScore)
            .ThenByDescending(section => (int)section.PriorityCategory)
            .ThenByDescending(section => section.CreatedUtc)
            .ThenBy(section => section.SectionKey, StringComparer.OrdinalIgnoreCase)
            .Select(section => ToStructuredSectionDto(briefing.CompanyId, section))
            .ToList();
    }

    private static string RenderBody(string companyName, CompanyBriefingType type, BriefingWindow window, BriefingAggregate aggregate)
    {
        var builder = new StringBuilder();
        builder.AppendLine(type == CompanyBriefingType.Daily
            ? $"Daily briefing for {companyName}"
            : $"Weekly executive summary for {companyName}");
        builder.AppendLine($"Period: {window.StartUtc:u} to {window.EndUtc:u}");
        AppendSection(builder, "Approvals", aggregate.Approvals);
        AppendSection(builder, "KPI highlights", aggregate.KpiHighlights);
        AppendSection(builder, "Alerts", aggregate.Alerts);
        AppendSection(builder, "Anomalies", aggregate.Anomalies);
        AppendSection(builder, "Agent and workflow updates", aggregate.AgentHighlights);
        if (!string.IsNullOrWhiteSpace(aggregate.NarrativeText))
        {
            builder.AppendLine();
            builder.AppendLine(aggregate.NarrativeText);
        }
        return builder.ToString().Trim();
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<BriefingItem> items)
    {
        builder.AppendLine();
        builder.AppendLine(title);
        if (items.Count == 0)
        {
            builder.AppendLine("- No notable items.");
            return;
        }

        foreach (var item in items.Take(5))
        {
            builder.AppendLine($"- {item.Text}");
        }
    }

    private static Dictionary<string, JsonNode?> BuildPayload(string companyName, CompanyBriefingType type, BriefingWindow window, BriefingAggregate aggregate, BriefingPreferenceSnapshot preferences) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["companyName"] = JsonValue.Create(companyName),
            ["briefingType"] = JsonValue.Create(type.ToStorageValue()),
            ["periodStartUtc"] = JsonValue.Create(window.StartUtc),
            ["periodEndUtc"] = JsonValue.Create(window.EndUtc),
            ["alerts"] = JsonSerializer.SerializeToNode(aggregate.Alerts.Select(x => x.Text), SerializerOptions),
            ["pendingApprovals"] = JsonSerializer.SerializeToNode(aggregate.Approvals.Select(x => x.Text), SerializerOptions),
            ["kpiHighlights"] = JsonSerializer.SerializeToNode(aggregate.KpiHighlights.Select(x => x.Text), SerializerOptions),
            ["anomalies"] = JsonSerializer.SerializeToNode(aggregate.Anomalies.Select(x => x.Text), SerializerOptions),
            ["notableAgentUpdates"] = JsonSerializer.SerializeToNode(aggregate.AgentHighlights.Select(x => x.Text), SerializerOptions),
            ["summaryItems"] = JsonSerializer.SerializeToNode(BuildSummaryItems(aggregate), SerializerOptions),
            ["sourceReferences"] = JsonSerializer.SerializeToNode(aggregate.SourceReferences, SerializerOptions),
            ["narrativeText"] = JsonValue.Create(aggregate.NarrativeText),
            ["structuredSections"] = JsonSerializer.SerializeToNode(aggregate.StructuredSections, SerializerOptions),
            ["summaryCounts"] = JsonSerializer.SerializeToNode(aggregate.SummaryCounts, SerializerOptions),
            ["cashPosition"] = JsonSerializer.SerializeToNode(aggregate.CashPosition, SerializerOptions),
            ["financeWorkflowOutputs"] = JsonSerializer.SerializeToNode(aggregate.KpiHighlights.Concat(aggregate.Alerts).Concat(aggregate.Anomalies).Select(x => x.Metadata.TryGetValue("workflowOutput", out var output) ? output : null).Where(x => x is not null), SerializerOptions),
            ["appliedFocusAreas"] = JsonSerializer.SerializeToNode(preferences.IncludedFocusAreas, SerializerOptions),
            ["minimumPriorityThresholdApplied"] = JsonValue.Create(preferences.PriorityThreshold.ToStorageValue()),
            ["preferenceSource"] = JsonValue.Create(preferences.Source.ToStorageValue())
        };

    private static Dictionary<string, JsonNode?> BuildSourceReferences(IReadOnlyList<BriefingSourceReferenceDto> references) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = JsonSerializer.SerializeToNode(references, SerializerOptions)
        };

    private static Dictionary<string, JsonNode?> BuildPreferenceSnapshot(BriefingPreferenceSnapshot snapshot)
    {
        var metadata = snapshot.ToMetadata();
        metadata["fallbackApplied"] = JsonValue.Create(snapshot.Source is BriefingPreferenceSource.TenantDefault or BriefingPreferenceSource.SystemDefault);
        metadata["appliedDefaultId"] = JsonValue.Create(snapshot.Source == BriefingPreferenceSource.TenantDefault ? snapshot.PreferenceId : null);
        metadata["appliedDefaultUpdatedUtc"] = JsonValue.Create(snapshot.Source == BriefingPreferenceSource.TenantDefault ? snapshot.PreferenceUpdatedUtc : null);
        return metadata;
    }

    private static Dictionary<string, JsonNode?> BuildSectionSourceReferences(AggregatedBriefingSectionDto section) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = JsonSerializer.SerializeToNode(section.RelatedReferences, SerializerOptions),
            ["linkedEntities"] = JsonSerializer.SerializeToNode(section.LinkedEntities, SerializerOptions),
            ["priority"] = JsonSerializer.SerializeToNode(new BriefingPriorityDto(section.PriorityCategory, section.PriorityScore, section.PriorityRuleCode), SerializerOptions)
        };

    private static IReadOnlyList<BriefingSummaryPayloadItem> BuildSummaryItems(BriefingAggregate aggregate) =>
        SectionItems("approvals", aggregate.Approvals)
            .Concat(SectionItems("kpi_highlights", aggregate.KpiHighlights))
            .Concat(SectionItems("alerts", aggregate.Alerts))
            .Concat(SectionItems("anomalies", aggregate.Anomalies))
            .Concat(SectionItems("agent_updates", aggregate.AgentHighlights))
            .ToList();

    private static IEnumerable<BriefingSummaryPayloadItem> SectionItems(string section, IReadOnlyList<BriefingItem> items) =>
        items.Select(item => new BriefingSummaryPayloadItem(section, item.Text, item.OccurredUtc, item.References));

    private static IReadOnlyList<BriefingSourceReferenceDto> CreateTaskReferences(Guid companyId, Guid taskId, string title, string status, Guid? workflowInstanceId)
    {
        var references = new List<BriefingSourceReferenceDto>
        {
            new("task", taskId, title, status, $"/tasks?companyId={companyId}&taskId={taskId}")
        };
        if (workflowInstanceId is { } id && id != Guid.Empty)
        {
            references.Add(CreateWorkflowReference(companyId, id, "Related workflow", null));
        }

        return references;
    }

    private static BriefingSourceReferenceDto CreateApprovalReference(Guid companyId, Guid approvalId, string approvalType, string status) =>
        new("approval", approvalId, $"{approvalType} approval", status, $"/approvals?companyId={companyId}&approvalId={approvalId}");

    private static BriefingSourceReferenceDto CreateWorkflowReference(Guid companyId, Guid workflowInstanceId, string label, string? status) =>
        new("workflow_instance", workflowInstanceId, label, status, $"/workflows?companyId={companyId}&workflowInstanceId={workflowInstanceId}");

    private static IReadOnlyList<BriefingSourceReferenceDto> CreateSourceReference(BriefingAggregateItemDto item)
    {
        if (item.SourceEntityId is null || item.SourceEntityId == Guid.Empty || string.IsNullOrWhiteSpace(item.SourceEntityType))
        {
            return [];
        }

        return [new BriefingSourceReferenceDto(item.SourceEntityType, item.SourceEntityId.Value, item.Title, item.Status, null)];
    }

    private static Dictionary<string, JsonNode?> BuildCashPositionMetadata(FinanceCashPositionDto cashPosition) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["availableBalance"] = JsonValue.Create(cashPosition.AvailableBalance),
            ["currency"] = JsonValue.Create(cashPosition.Currency),
            ["averageMonthlyBurn"] = JsonValue.Create(cashPosition.AverageMonthlyBurn),
            ["estimatedRunwayDays"] = cashPosition.EstimatedRunwayDays.HasValue ? JsonValue.Create(cashPosition.EstimatedRunwayDays.Value) : null,
            ["warningRunwayDays"] = JsonValue.Create(cashPosition.Thresholds.WarningRunwayDays),
            ["criticalRunwayDays"] = JsonValue.Create(cashPosition.Thresholds.CriticalRunwayDays),
            ["isLowCash"] = JsonValue.Create(cashPosition.AlertState.IsLowCash),
            ["alertId"] = cashPosition.AlertState.AlertId.HasValue ? JsonValue.Create(cashPosition.AlertState.AlertId.Value) : null,
            ["alertStatus"] = JsonValue.Create(cashPosition.AlertState.AlertStatus),
            ["classification"] = JsonValue.Create(cashPosition.Classification),
            ["riskLevel"] = JsonValue.Create(cashPosition.RiskLevel),
            ["recommendedAction"] = JsonValue.Create(cashPosition.RecommendedAction),
            ["rationale"] = JsonValue.Create(cashPosition.Rationale),
            ["confidence"] = JsonValue.Create(cashPosition.Confidence),
            ["sourceWorkflow"] = JsonValue.Create(cashPosition.SourceWorkflow),
            ["workflowOutput"] = JsonSerializer.SerializeToNode(cashPosition.WorkflowOutput, SerializerOptions)
        };

    private static decimal? TryReadDecimal(Dictionary<string, JsonNode?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        try
        {
            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue;
            }
        }
        catch (InvalidOperationException)
        {
        }

        return null;
    }

    private static IReadOnlyList<BriefingSourceReferenceDto> DeduplicateReferences(IEnumerable<BriefingSourceReferenceDto> references) =>
        references
            .Where(x => x.EntityId != Guid.Empty && !string.IsNullOrWhiteSpace(x.EntityType))
            .GroupBy(x => new { EntityType = x.EntityType.Trim().ToLowerInvariant(), x.EntityId })
            .Select(x => x.First())
            .ToList();

    private async Task<Conversation> ResolveBriefingConversationAsync(Guid companyId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var conversation = await _dbContext.Conversations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ChannelType == BriefingConversationChannel, cancellationToken);
        if (conversation is not null)
        {
            return conversation;
        }

        conversation = new Conversation(Guid.NewGuid(), companyId, BriefingConversationChannel, "Executive briefings", ownerUserId, agentId: null);
        _dbContext.Conversations.Add(conversation);
        return conversation;
    }

    private async Task<Guid?> ResolveOwnerUserIdAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == CompanyMembershipStatus.Active && x.UserId.HasValue)
            .OrderBy(x => x.Role == CompanyMembershipRole.Owner ? 0 : x.Role == CompanyMembershipRole.Admin ? 1 : 2)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipContextResolver.ResolveAsync(companyId, cancellationToken);
        return membership ?? throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
    }

    private async Task<CompanyBriefingDto> ToDtoAsync(CompanyBriefing briefing, CancellationToken cancellationToken)
    {
        var dto = ToDto(briefing);
        if (dto.StructuredSections.Count == 0)
        {
            return dto;
        }

        var enrichedSections = await EnrichPersistedLinkedEntitiesAsync(briefing.CompanyId, dto.StructuredSections, cancellationToken);
        var payload = dto.StructuredPayload.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
        payload["structuredSections"] = JsonSerializer.SerializeToNode(enrichedSections, SerializerOptions);

        return dto with
        {
            StructuredPayload = payload,
            StructuredSections = enrichedSections
        };
    }

    private static CompanyBriefingDto ToDto(CompanyBriefing briefing)
    {
        var references = briefing.SourceReferences.TryGetValue("items", out var node) && node is not null
            ? JsonSerializer.Deserialize<List<BriefingSourceReferenceDto>>(node.ToJsonString(), SerializerOptions) ?? []
            : [];
        var narrativeText = briefing.StructuredPayload.TryGetValue("narrativeText", out var narrativeNode) && narrativeNode is JsonValue narrativeValue && narrativeValue.TryGetValue<string>(out var narrative)
            ? narrative
            : briefing.SummaryBody;
        var structuredSections = briefing.StructuredPayload.TryGetValue("structuredSections", out var sectionsNode) && sectionsNode is not null
            ? JsonSerializer.Deserialize<List<AggregatedBriefingSectionDto>>(sectionsNode.ToJsonString(), SerializerOptions) ?? []
            : [];
        var summaryCounts = briefing.StructuredPayload.TryGetValue("summaryCounts", out var countsNode) && countsNode is not null
            ? JsonSerializer.Deserialize<BriefingSummaryCountsDto>(countsNode.ToJsonString(), SerializerOptions) ?? new BriefingSummaryCountsDto(0, 0, 0, 0)
            : new BriefingSummaryCountsDto(0, 0, 0, 0);
        structuredSections = ToStructuredSectionDtos(briefing).ToList() is { Count: > 0 } persistedSections ? persistedSections : structuredSections;

        return new CompanyBriefingDto(
            briefing.Id,
            briefing.CompanyId,
            briefing.BriefingType.ToStorageValue(),
            briefing.PeriodStartUtc,
            briefing.PeriodEndUtc,
            briefing.Title,
            briefing.SummaryBody,
            briefing.StructuredPayload.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
            references,
            briefing.MessageId,
            briefing.GeneratedUtc,
            briefing.PreferenceSnapshot.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase))
        {
            NarrativeText = narrativeText,
            StructuredSections = structuredSections,
            SummaryCounts = summaryCounts
        };
    }

    private static AggregatedBriefingSectionDto ToStructuredSectionDto(Guid companyId, CompanyBriefingSection section)
    {
        var contributions = section.Contributions
            .Where(contribution => contribution.CompanyId == companyId && contribution.SectionId == section.Id)
            .OrderByDescending(contribution => contribution.TimestampUtc)
            .ThenBy(contribution => contribution.AgentId)
            .Select(contribution => (new BriefingInsightContributionDto(
                companyId,
                null,
                contribution.AgentId,
                new BriefingSourceReferenceDto(
                    contribution.SourceEntityType,
                    contribution.SourceEntityId,
                    contribution.SourceLabel,
                    contribution.SourceStatus,
                    contribution.SourceRoute),
                contribution.TimestampUtc,
                contribution.ConfidenceScore,
                contribution.CompanyEntityId,
                contribution.WorkflowInstanceId,
                contribution.TaskId,
                contribution.EventCorrelationId,
                contribution.Topic,
                contribution.Narrative,
                contribution.Assessment,
                contribution.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
                contribution.Id)) with
            {
                ConfidenceMetadata = contribution.ConfidenceMetadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase)
            })
            .ToList();

        var viewpoints = contributions
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Assessment))
            .GroupBy(contribution => contribution.Assessment!.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BriefingConflictViewpointDto(
                group.First().Assessment!,
                group.Select(contribution => contribution.AgentId).Where(agentId => agentId != Guid.Empty).Distinct().OrderBy(agentId => agentId).ToList(),
                group.Select(contribution => contribution.Narrative).Where(narrative => !string.IsNullOrWhiteSpace(narrative)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList(),
                group.Select(contribution => contribution.SourceReference)
                    .GroupBy(reference => new { EntityType = reference.EntityType.Trim().ToLowerInvariant(), reference.EntityId })
                    .Select(grouping => grouping.First())
                    .ToList()))
            .ToList();

        var relatedReferences = section.SourceReferences.TryGetValue("items", out var referencesNode) && referencesNode is not null
            ? JsonSerializer.Deserialize<List<BriefingSourceReferenceDto>>(referencesNode.ToJsonString(), SerializerOptions) ?? []
            : contributions.Select(contribution => contribution.SourceReference)
                .GroupBy(reference => new { EntityType = reference.EntityType.Trim().ToLowerInvariant(), reference.EntityId })
                .Select(group => group.First())
                .ToList();
        var linkedEntities = section.SourceReferences.TryGetValue("linkedEntities", out var linkedNode) && linkedNode is not null
            ? JsonSerializer.Deserialize<List<BriefingLinkedEntityReferenceDto>>(linkedNode.ToJsonString(), SerializerOptions) ?? []
            : [];

        return new AggregatedBriefingSectionDto(
            section.SectionKey,
            section.Title,
            section.GroupingType,
            section.GroupingKey,
            section.Narrative,
            section.IsConflicting,
            contributions,
            section.IsConflicting ? viewpoints : [],
            relatedReferences)
        {
            SectionType = section.SectionType,
            PriorityCategory = section.PriorityCategory.ToStorageValue(),
            PriorityScore = section.PriorityScore,
            PriorityRuleCode = section.PriorityRuleCode,
            LinkedEntities = linkedEntities
        };
    }

    private static CompanyBriefingDeliveryPreferenceDto ToDto(CompanyBriefingDeliveryPreference preference) =>
        new(preference.CompanyId, preference.UserId, preference.InAppEnabled, preference.MobileEnabled, preference.DailyEnabled, preference.WeeklyEnabled, preference.PreferredDeliveryTime, preference.PreferredTimezone, preference.UpdatedUtc);

    private async Task<BriefingPreferenceSnapshot> ResolveEffectivePreferenceSnapshotAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        var userPreference = await _dbContext.UserBriefingPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == userId, cancellationToken);
        if (userPreference is not null)
        {
            return userPreference.ToSnapshot(BriefingPreferenceSource.User);
        }

        var tenantDefault = await _dbContext.TenantBriefingDefaults
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        return tenantDefault is null
            ? TenantBriefingDefault.CreateSystemDefault(companyId, userId)
            : tenantDefault.ToSnapshot(userId);
    }

    private static bool IsBriefingFrequencyEnabled(BriefingDeliveryFrequency frequency, CompanyBriefingType briefingType) =>
        frequency == BriefingDeliveryFrequency.DailyAndWeekly ||
        frequency == BriefingDeliveryFrequency.Daily && briefingType == CompanyBriefingType.Daily ||
        frequency == BriefingDeliveryFrequency.Weekly && briefingType == CompanyBriefingType.Weekly;

    private static ParsedBriefingPreferenceCommand ParsePreferenceCommand(UpsertBriefingPreferenceCommand command)
    {
        if (command is null)
        {
            throw new BriefingValidationException(new Dictionary<string, string[]>
            {
                ["body"] =
                [
                    "briefing_preferences.invalid_payload",
                    "A briefing preference payload is required."
                ]
            });
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var frequency = default(BriefingDeliveryFrequency);
        var focusAreas = BriefingFocusAreaValues.AllowedValues;
        var threshold = default(BriefingSectionPriorityCategory);

        try
        {
            frequency = BriefingDeliveryFrequencyValues.Parse(command.DeliveryFrequency);
        }
        catch (ArgumentOutOfRangeException)
        {
            errors[nameof(command.DeliveryFrequency)] =
            [
                BriefingPreferenceErrorCodes.InvalidDeliveryFrequency,
                $"Allowed values: {string.Join(", ", BriefingDeliveryFrequencyValues.AllowedValues)}."
            ];
        }

        try
        {
            focusAreas = BriefingFocusAreaValues.NormalizeOrThrow(command.IncludedFocusAreas);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            var details = new List<string>
            {
                BriefingPreferenceErrorCodes.UnsupportedFocusArea,
                $"Allowed values: {string.Join(", ", BriefingFocusAreaValues.AllowedValues)}."
            };

            if (ex.ActualValue is string attempted && !string.IsNullOrWhiteSpace(attempted))
            {
                details.Add($"Unsupported focus areas: {attempted}.");
            }

            errors[nameof(command.IncludedFocusAreas)] = details.ToArray();
        }

        try
        {
            threshold = BriefingSectionPriorityCategoryValues.Parse(command.PriorityThreshold);
        }
        catch (ArgumentOutOfRangeException)
        {
            errors[nameof(command.PriorityThreshold)] =
            [
                BriefingPreferenceErrorCodes.InvalidPriorityThreshold,
                "Allowed values: informational, medium, high, critical."
            ];
        }

        if (errors.Count > 0)
        {
            throw new BriefingValidationException(errors);
        }

        return new ParsedBriefingPreferenceCommand(frequency, focusAreas, threshold);
    }

    private static BriefingPreferenceDto ToDto(UserBriefingPreference preference) =>
        new(preference.CompanyId, preference.UserId, preference.DeliveryFrequency.ToStorageValue(), preference.IncludedFocusAreas, preference.PriorityThreshold.ToStorageValue(), BriefingPreferenceSource.User.ToStorageValue(), preference.UpdatedUtc);

    private static BriefingPreferenceDto ToDto(BriefingPreferenceSnapshot snapshot) =>
        new(snapshot.CompanyId, snapshot.UserId, snapshot.DeliveryFrequency.ToStorageValue(), snapshot.IncludedFocusAreas, snapshot.PriorityThreshold.ToStorageValue(), snapshot.Source.ToStorageValue(), snapshot.PreferenceUpdatedUtc);

    private static TenantBriefingDefaultDto ToDto(TenantBriefingDefault defaults) =>
        new(defaults.CompanyId, defaults.DeliveryFrequency.ToStorageValue(), defaults.IncludedFocusAreas, defaults.PriorityThreshold.ToStorageValue(), defaults.UpdatedUtc);

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static BriefingWindow ResolveWindow(string? timezone, CompanyBriefingType type, DateTime nowUtc)
    {
        var zone = ResolveTimezone(timezone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var localEnd = localNow.Date;
        if (type == CompanyBriefingType.Weekly)
        {
            var daysSinceMonday = ((int)localEnd.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            localEnd = localEnd.AddDays(-daysSinceMonday);
        }

        var localStart = type == CompanyBriefingType.Daily ? localEnd.AddDays(-1) : localEnd.AddDays(-7);
        return new BriefingWindow(
            TimeZoneInfo.ConvertTimeToUtc(localStart, zone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
    }

    private static TimeZoneInfo ResolveTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private async Task<IReadOnlyList<BriefingSeverityRuleProjection>> LoadSeverityRulesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var persisted = await _dbContext.CompanyBriefingSeverityRules
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == BriefingSeverityRuleStatus.Active)
            .OrderByDescending(x => x.PriorityScore)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.RuleCode)
            .Select(x => new BriefingSeverityRuleProjection(
                x.RuleCode,
                x.SectionType,
                x.EntityType,
                x.ConditionKey,
                x.ConditionValue,
                x.PriorityCategory,
                x.PriorityScore))
            .ToListAsync(cancellationToken);

        return persisted.Count > 0 ? persisted : DefaultSeverityRules;
    }

    private static BriefingPriorityResolution ResolvePriority(
        IReadOnlyList<BriefingSeverityRuleProjection> rules,
        string sectionType,
        string entityType,
        string conditionKey,
        string conditionValue)
    {
        var rule = rules.FirstOrDefault(x =>
            string.Equals(x.SectionType, sectionType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ConditionKey, conditionKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ConditionValue, conditionValue, StringComparison.OrdinalIgnoreCase));

        return rule is null
            ? new BriefingPriorityResolution(BriefingSectionPriorityCategory.Informational, 10, "default_informational")
            : new BriefingPriorityResolution(rule.PriorityCategory, rule.PriorityScore, rule.RuleCode);
    }

    private async Task<IReadOnlyList<AggregatedBriefingSectionDto>> EnrichStructuredSectionsAsync(
        Guid companyId,
        IReadOnlyList<AggregatedBriefingSectionDto> sections,
        BriefingAggregateResultDto aggregate,
        CancellationToken cancellationToken)
    {
        var priorityByKey = BuildPriorityMap(aggregate);
        var linkedReferences = await BuildLinkedReferenceMapAsync(companyId, sections, cancellationToken);
        return sections
            .Select(section =>
            {
                var priority = priorityByKey.TryGetValue(section.SectionKey, out var resolved)
                    ? resolved
                    : ResolveSectionPriorityFromContributions(section, aggregate);
                var links = ResolveSectionLinks(companyId, section, linkedReferences);

                return section with
                {
                    SectionType = section.Contributions.FirstOrDefault()?.SourceReference.EntityType ?? section.GroupingType,
                    PriorityCategory = priority.Category.ToStorageValue(),
                    PriorityScore = priority.Score,
                    PriorityRuleCode = priority.RuleCode,
                    LinkedEntities = links
                };
            })
            .OrderByDescending(x => x.PriorityScore)
            .ThenByDescending(x => BriefingSectionPriorityCategoryValues.Parse(x.PriorityCategory))
            .ThenByDescending(x => x.Contributions.Select(contribution => contribution.TimestampUtc).DefaultIfEmpty(DateTime.MinValue).Max())
            .ThenBy(x => x.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<AggregatedBriefingSectionDto>> EnrichPersistedLinkedEntitiesAsync(
        Guid companyId,
        IReadOnlyList<AggregatedBriefingSectionDto> sections,
        CancellationToken cancellationToken)
    {
        var linkedReferences = await BuildLinkedReferenceMapAsync(companyId, sections, cancellationToken);
        return sections
            .Select(section => section with
            {
                LinkedEntities = ResolveSectionLinks(companyId, section, linkedReferences)
            })
            .ToList();
    }

    private static IReadOnlyList<AggregatedBriefingSectionDto> OrderStructuredSections(
        IReadOnlyList<AggregatedBriefingSectionDto> sections,
        BriefingAggregateResultDto aggregate)
    {
        var priorityByKey = BuildPriorityMap(aggregate);
        return sections
            .OrderByDescending(section => priorityByKey.TryGetValue(section.SectionKey, out var priority) ? priority.Score : 0)
            .ThenBy(section => section.SectionKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, BriefingPriorityResolution> BuildPriorityMap(BriefingAggregateResultDto aggregate) =>
        aggregate.Alerts
            .Concat(aggregate.PendingApprovals)
            .Concat(aggregate.KpiHighlights)
            .Concat(aggregate.Anomalies)
            .Concat(aggregate.NotableAgentUpdates)
            .Where(item => item.SourceEntityId.HasValue)
            .GroupBy(item => $"{NormalizeLinkedEntityType(item.SourceEntityType ?? string.Empty)}:{item.SourceEntityId!.Value:N}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.PriorityScore).Select(item => new BriefingPriorityResolution(item.PriorityCategory, item.PriorityScore, item.PriorityRuleCode)).First(),
                StringComparer.OrdinalIgnoreCase);

    private static BriefingPriorityResolution ResolveSectionPriorityFromContributions(AggregatedBriefingSectionDto section, BriefingAggregateResultDto aggregate)
    {
        var items = aggregate.Alerts
            .Concat(aggregate.PendingApprovals)
            .Concat(aggregate.KpiHighlights)
            .Concat(aggregate.Anomalies)
            .Concat(aggregate.NotableAgentUpdates)
            .Where(item => section.Contributions.Any(contribution =>
                (item.SourceEntityId.HasValue && contribution.SourceReference.EntityId == item.SourceEntityId.Value) ||
                (item.TaskId.HasValue && contribution.TaskId == item.TaskId) ||
                (item.WorkflowInstanceId.HasValue && contribution.WorkflowInstanceId == item.WorkflowInstanceId)));

        return items
            .OrderByDescending(item => item.PriorityScore)
            .Select(item => new BriefingPriorityResolution(item.PriorityCategory, item.PriorityScore, item.PriorityRuleCode))
            .FirstOrDefault() ?? new BriefingPriorityResolution(BriefingSectionPriorityCategory.Informational, 10, "default_informational");
    }

    private async Task<Dictionary<string, BriefingLinkedEntityReferenceDto>> BuildLinkedReferenceMapAsync(
        Guid companyId,
        IReadOnlyList<AggregatedBriefingSectionDto> sections,
        CancellationToken cancellationToken)
    {
        var references = sections.SelectMany(section => BuildLinkedEntityCandidates(companyId, section)).ToList();
        var taskIds = references.Where(x => string.Equals(NormalizeLinkedEntityType(x.EntityType), "task", StringComparison.OrdinalIgnoreCase)).Select(x => x.EntityId).Distinct().ToList();
        var workflowIds = references.Where(x => string.Equals(NormalizeLinkedEntityType(x.EntityType), "workflow_instance", StringComparison.OrdinalIgnoreCase)).Select(x => x.EntityId).Distinct().ToList();
        var approvalIds = references.Where(x => string.Equals(NormalizeLinkedEntityType(x.EntityType), "approval", StringComparison.OrdinalIgnoreCase)).Select(x => x.EntityId).Distinct().ToList();

        var tasks = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && taskIds.Contains(x.Id))
            .Select(x => new BriefingLinkedEntityReferenceDto("task", x.Id, x.Title, BriefingLinkedEntityResolutionState.Available.ToStorageValue(), x.Status.ToStorageValue(), true, null, null))
            .ToListAsync(cancellationToken);
        var workflows = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && workflowIds.Contains(x.Id))
            .Select(x => new BriefingLinkedEntityReferenceDto("workflow_instance", x.Id, x.Definition.Name, BriefingLinkedEntityResolutionState.Available.ToStorageValue(), x.State.ToStorageValue(), true, null, $"/workflows?companyId={companyId}&workflowInstanceId={x.Id}"))
            .ToListAsync(cancellationToken);
        var approvals = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && approvalIds.Contains(x.Id))
            .Select(x => new BriefingLinkedEntityReferenceDto("approval", x.Id, $"{x.ApprovalType} approval", BriefingLinkedEntityResolutionState.Available.ToStorageValue(), x.Status.ToStorageValue(), true, null, $"/approvals?companyId={companyId}&approvalId={x.Id}"))
            .ToListAsync(cancellationToken);

        var resolved = tasks.Concat(workflows).Concat(approvals)
            .ToDictionary(x => $"{x.EntityType}:{x.EntityId:N}", StringComparer.OrdinalIgnoreCase);

        var missingTasks = taskIds.Where(id => !resolved.ContainsKey($"task:{id:N}")).ToList();
        var inaccessibleTasks = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId != companyId && missingTasks.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in missingTasks)
        {
            resolved[$"task:{id:N}"] = CreateUnavailableReference(
                "task",
                id,
                "Unavailable task",
                inaccessibleTasks.Contains(id));
        }

        var missingWorkflows = workflowIds.Where(id => !resolved.ContainsKey($"workflow_instance:{id:N}")).ToList();
        var inaccessibleWorkflows = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId != companyId && missingWorkflows.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in missingWorkflows)
        {
            resolved[$"workflow_instance:{id:N}"] = CreateUnavailableReference(
                "workflow_instance",
                id,
                "Unavailable workflow",
                inaccessibleWorkflows.Contains(id));
        }

        var missingApprovals = approvalIds.Where(id => !resolved.ContainsKey($"approval:{id:N}")).ToList();
        var inaccessibleApprovals = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId != companyId && missingApprovals.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in missingApprovals)
        {
            resolved[$"approval:{id:N}"] = CreateUnavailableReference(
                "approval",
                id,
                "Unavailable approval",
                inaccessibleApprovals.Contains(id));
        }

        return resolved;
    }

    private static BriefingLinkedEntityReferenceDto CreateUnavailableReference(string entityType, Guid entityId, string label, bool inaccessible) =>
        new(
            entityType,
            entityId,
            label,
            inaccessible ? BriefingLinkedEntityResolutionState.Inaccessible.ToStorageValue() : BriefingLinkedEntityResolutionState.Deleted.ToStorageValue(),
            null,
            false,
            BriefingLinkedEntityPlaceholderReason.DeletedOrInaccessible.ToStorageValue(),
            null);

    private static IReadOnlyList<BriefingLinkedEntityReferenceDto> ResolveSectionLinks(
        Guid companyId,
        AggregatedBriefingSectionDto section,
        IReadOnlyDictionary<string, BriefingLinkedEntityReferenceDto> availableReferences) =>
        BuildLinkedEntityCandidates(companyId, section)
            .Select(reference => NormalizeLinkedEntityReference(reference, availableReferences))
            .Where(reference => reference is not null)
            .Cast<BriefingLinkedEntityReferenceDto>()
            .GroupBy(reference => $"{reference.EntityType}:{reference.EntityId:N}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reference => LinkTypeSortRank(reference.EntityType))
            .ThenBy(reference => reference.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<BriefingSourceReferenceDto> BuildLinkedEntityCandidates(Guid companyId, AggregatedBriefingSectionDto section)
    {
        var candidates = new List<BriefingSourceReferenceDto>();
        candidates.AddRange(section.RelatedReferences);
        candidates.AddRange(section.Contributions.Select(contribution => contribution.SourceReference));
        foreach (var contribution in section.Contributions)
        {
            if (contribution.TaskId is { } taskId && taskId != Guid.Empty)
            {
                candidates.Add(new BriefingSourceReferenceDto("task", taskId, contribution.Topic, null, null));
            }

            if (contribution.WorkflowInstanceId is { } workflowInstanceId && workflowInstanceId != Guid.Empty)
            {
                candidates.Add(CreateWorkflowReference(companyId, workflowInstanceId, "Related workflow", null));
            }
        }
        if (Guid.TryParse(section.GroupingKey, out var groupingId) && groupingId != Guid.Empty)
        {
            if (string.Equals(section.GroupingType, BriefingInsightGroupingTypes.Task, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new BriefingSourceReferenceDto("task", groupingId, section.Title, null, null));
            }
            else if (string.Equals(section.GroupingType, BriefingInsightGroupingTypes.Workflow, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(CreateWorkflowReference(companyId, groupingId, section.Title, null));
            }
        }

        return candidates
            .Where(reference => reference.EntityId != Guid.Empty)
            .Select(reference => reference with { EntityType = NormalizeLinkedEntityType(reference.EntityType) })
            .Where(reference => BriefingLinkedEntityTypeValues.TryParse(reference.EntityType, out _))
            .GroupBy(reference => $"{reference.EntityType}:{reference.EntityId:N}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static BriefingLinkedEntityReferenceDto? NormalizeLinkedEntityReference(
        BriefingSourceReferenceDto reference,
        IReadOnlyDictionary<string, BriefingLinkedEntityReferenceDto> availableReferences)
    {
        var entityType = NormalizeLinkedEntityType(reference.EntityType);
        if (entityType.Equals("workflow_exception", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key = $"{entityType}:{reference.EntityId:N}";
        if (availableReferences.TryGetValue(key, out var resolved))
        {
            return resolved;
        }
        return null;
    }

    private static string NormalizeLinkedEntityType(string entityType) =>
        entityType.Equals("approval_request", StringComparison.OrdinalIgnoreCase) ? "approval" : entityType;

    private static int LinkTypeSortRank(string entityType) =>
        entityType.Equals("task", StringComparison.OrdinalIgnoreCase) ? 0 :
        entityType.Equals("workflow_instance", StringComparison.OrdinalIgnoreCase) ? 1 :
        entityType.Equals("approval", StringComparison.OrdinalIgnoreCase) ? 2 : 3;

    private sealed record BriefingWindow(DateTime StartUtc, DateTime EndUtc);
    private sealed record DueCompany(Guid Id, string Name, string? Timezone);
    private sealed record BriefingItem(string Kind, string Text, Guid EntityId, DateTime OccurredUtc, IReadOnlyList<BriefingSourceReferenceDto> References, BriefingSectionPriorityCategory PriorityCategory, int PriorityScore, IReadOnlyDictionary<string, JsonNode?> Metadata);
    private sealed record BriefingAggregate(
        IReadOnlyList<BriefingItem> Approvals,
        IReadOnlyList<BriefingItem> KpiHighlights,
        IReadOnlyList<BriefingItem> Alerts,
        IReadOnlyList<BriefingItem> Anomalies,
        IReadOnlyList<BriefingItem> AgentHighlights,
        IReadOnlyList<BriefingSourceReferenceDto> SourceReferences,
        string NarrativeText,
        IReadOnlyList<AggregatedBriefingSectionDto> StructuredSections,
        FinanceCashPositionDto? CashPosition,
        BriefingSummaryCountsDto SummaryCounts = null!)
    {
        public BriefingSummaryCountsDto SummaryCounts { get; init; } = SummaryCounts ?? new BriefingSummaryCountsDto(0, 0, 0, 0);
    }

    private sealed record BriefingSummaryPayloadItem(string Section, string Title, DateTime OccurredUtc, IReadOnlyList<BriefingSourceReferenceDto> References);
    private sealed record BriefingSeverityRuleProjection(
        string RuleCode,
        string SectionType,
        string EntityType,
        string ConditionKey,
        string ConditionValue,
        BriefingSectionPriorityCategory PriorityCategory,
        int PriorityScore);
    private sealed record BriefingPriorityResolution(BriefingSectionPriorityCategory Category, int Score, string? RuleCode);
    private sealed record ParsedBriefingPreferenceCommand(BriefingDeliveryFrequency Frequency, IReadOnlyList<string> FocusAreas, BriefingSectionPriorityCategory PriorityThreshold);

    private static readonly IReadOnlyList<BriefingSeverityRuleProjection> DefaultSeverityRules =
    [
        new("critical_workflow_blocked", "alerts", "workflow_instance", "status", "blocked", BriefingSectionPriorityCategory.Critical, 100),
        new("high_workflow_failed", "alerts", "workflow_instance", "status", "failed", BriefingSectionPriorityCategory.High, 90),
        new("high_task_overdue", "anomalies", "task", "due", "overdue", BriefingSectionPriorityCategory.High, 85),
        new("high_task_blocked", "anomalies", "task", "status", "blocked", BriefingSectionPriorityCategory.High, 80),
        new("high_task_failed", "anomalies", "task", "status", "failed", BriefingSectionPriorityCategory.High, 75),
        new("medium_approval_pending", "pending_approvals", "approval", "status", "pending", BriefingSectionPriorityCategory.Medium, 60),
        new("medium_task_awaiting_approval", "anomalies", "task", "status", "awaiting_approval", BriefingSectionPriorityCategory.Medium, 55),
        new("informational_task_completed", "kpi_highlights", "task", "status", "completed", BriefingSectionPriorityCategory.Informational, 20),
        new("critical_alert", "alerts", "alert", "severity", "critical", BriefingSectionPriorityCategory.Critical, 100),
        new("critical_finance_cash_position", "alerts", "alert", "severity", "critical", BriefingSectionPriorityCategory.Critical, 100),
        new("high_finance_cash_position", "alerts", "alert", "severity", "high", BriefingSectionPriorityCategory.High, 90),
        new("medium_finance_cash_position", "alerts", "alert", "severity", "medium", BriefingSectionPriorityCategory.Medium, 50),
        new("high_alert", "alerts", "alert", "severity", "high", BriefingSectionPriorityCategory.High, 90),
        new("medium_alert", "alerts", "alert", "severity", "medium", BriefingSectionPriorityCategory.Medium, 50),
    ];
}
