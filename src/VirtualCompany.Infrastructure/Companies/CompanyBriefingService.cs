using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
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

    public CompanyBriefingService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IExecutiveDashboardAggregateCache aggregateCache,
        ICompanyOutboxEnqueuer outboxEnqueuer,
        ILogger<CompanyBriefingService> logger)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _aggregateCache = aggregateCache;
        _outboxEnqueuer = outboxEnqueuer;
        _logger = logger;
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

        var aggregate = await ResolveAggregateForGenerationAsync(companyId, briefingType, window, cancellationToken);
        var title = briefingType == CompanyBriefingType.Daily
            ? $"Daily briefing for {company.Name}"
            : $"Weekly executive summary for {company.Name}";
        var body = RenderBody(company.Name, briefingType, window, aggregate);
        var structuredPayload = BuildPayload(company.Name, briefingType, window, aggregate);
        var sourceRefs = BuildSourceReferences(aggregate.SourceReferences);

        var ownerUserId = await ResolveOwnerUserIdAsync(companyId, cancellationToken);
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
            message?.Id);
        _dbContext.CompanyBriefings.Add(briefing);

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

                    var result = await GenerateAsync(
                        companyId,
                        new GenerateCompanyBriefingCommand(briefingType.ToStorageValue(), nowUtc),
                        cancellationToken);
                    if (!result.AlreadyExisted)
                    {
                        generated++;
                        notifications += result.NotificationsCreated;
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

        var briefings = await _dbContext.CompanyBriefings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.BriefingType)
            .Select(x => x.OrderByDescending(briefing => briefing.GeneratedUtc).First())
            .ToListAsync(cancellationToken);

        var daily = briefings.SingleOrDefault(x => x.BriefingType == CompanyBriefingType.Daily);
        var weekly = briefings.SingleOrDefault(x => x.BriefingType == CompanyBriefingType.Weekly);
        return new DashboardBriefingCardDto(
            daily is null ? null : ToDto(daily),
            weekly is null ? null : ToDto(weekly));
    }

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

        var cadenceEnabled = activeUserIds.Any(userId =>
        {
            var preference = preferences.SingleOrDefault(x => x.UserId == userId);
            if (preference?.InAppEnabled == false)
            {
                return false;
            }

            return briefingType == CompanyBriefingType.Daily
                ? preference?.DailyEnabled ?? true
                : preference?.WeeklyEnabled ?? true;
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

        var created = 0;
        foreach (var userId in users)
        {
            preferences.TryGetValue(userId, out var preference);
            var cadenceEnabled = briefing.BriefingType == CompanyBriefingType.Daily
                ? preference?.DailyEnabled ?? true
                : preference?.WeeklyEnabled ?? true;
            if (!cadenceEnabled)
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
        var briefingType = type.ToStorageValue();

        var approvalRows = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == ApprovalRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(10)
            .Select(x => new { x.Id, x.ApprovalType, x.TargetEntityType, x.TargetEntityId, x.Status, x.CreatedUtc })
            .ToListAsync(cancellationToken);
        var pendingApprovals = approvalRows
            .Select(x => new BriefingAggregateItemDto(
                "pending_approvals",
                $"{x.ApprovalType} approval pending",
                $"Review {x.TargetEntityType} {x.TargetEntityId:N}.",
                "medium",
                x.Status.ToStorageValue(),
                "approval_request",
                x.Id,
                x.CreatedUtc,
                [],
                [CreateApprovalReference(companyId, x.Id, x.ApprovalType, x.Status.ToStorageValue())]))
            .ToList();

        var taskRows = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.UpdatedUtc >= window.StartUtc && x.UpdatedUtc < window.EndUtc)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(25)
            .Select(x => new { x.Id, x.Title, x.Status, x.Priority, x.UpdatedUtc })
            .ToListAsync(cancellationToken);
        var kpiHighlights = taskRows
            .Where(x => x.Status == WorkTaskStatus.Completed)
            .Take(10)
            .Select(x => new BriefingAggregateItemDto(
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
                [CreateTaskReference(x.Id, x.Title, x.Status.ToStorageValue())]))
            .ToList();
        var anomalies = taskRows
            .Where(x => x.Status is WorkTaskStatus.Blocked or WorkTaskStatus.Failed or WorkTaskStatus.AwaitingApproval)
            .Take(10)
            .Select(x => new BriefingAggregateItemDto(
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
                [CreateTaskReference(x.Id, x.Title, x.Status.ToStorageValue())]))
            .ToList();

        var workflowExceptionRows = await _dbContext.WorkflowExceptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == WorkflowExceptionStatus.Open)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(10)
            .Select(x => new { x.Id, x.Title, x.Details, x.Status, x.ExceptionType, x.OccurredUtc })
            .ToListAsync(cancellationToken);
        var alerts = workflowExceptionRows
            .Select(x => new BriefingAggregateItemDto(
                "alerts",
                x.Title,
                x.Details,
                "high",
                x.Status.ToStorageValue(),
                "workflow_exception",
                x.Id,
                x.OccurredUtc,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["exceptionType"] = JsonValue.Create(x.ExceptionType.ToStorageValue())
                },
                [new BriefingSourceReferenceDto("workflow_exception", x.Id, x.Title, x.Status.ToStorageValue(), $"/workflows?companyId={companyId}")]))
            .ToList();

        var agentRows = await _dbContext.ToolExecutionAttempts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.StartedUtc >= window.StartUtc && x.StartedUtc < window.EndUtc)
            .OrderByDescending(x => x.StartedUtc)
            .Take(25)
            .Select(x => new { x.AgentId, x.Status, x.ToolName, x.StartedUtc })
            .ToListAsync(cancellationToken);
        var notableAgentUpdates = agentRows
            .GroupBy(x => new { x.AgentId, x.Status })
            .Select(x => new BriefingAggregateItemDto(
                "notable_agent_updates",
                $"{x.Count()} agent tool execution(s) {x.Key.Status.ToStorageValue()}",
                string.Join(", ", x.Select(item => item.ToolName).Distinct().Take(3)),
                null,
                x.Key.Status.ToStorageValue(),
                "agent",
                x.Key.AgentId,
                x.Max(item => item.StartedUtc),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["toolNames"] = JsonSerializer.SerializeToNode(x.Select(item => item.ToolName).Distinct().Take(5), SerializerOptions)
                },
                []))
            .OrderByDescending(x => x.OccurredUtc)
            .Take(10)
            .ToList();

        return new BriefingAggregateResultDto(companyId, briefingType, window.StartUtc, window.EndUtc, alerts, pendingApprovals, kpiHighlights, anomalies, notableAgentUpdates);
    }

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

        return new BriefingAggregate(approvals, kpiHighlights, alerts, anomalies, agentHighlights, sourceReferences);
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
                : CreateSourceReference(item));
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

    private static Dictionary<string, JsonNode?> BuildPayload(string companyName, CompanyBriefingType type, BriefingWindow window, BriefingAggregate aggregate) =>
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
            ["sourceReferences"] = JsonSerializer.SerializeToNode(aggregate.SourceReferences, SerializerOptions)
        };

    private static Dictionary<string, JsonNode?> BuildSourceReferences(IReadOnlyList<BriefingSourceReferenceDto> references) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = JsonSerializer.SerializeToNode(references, SerializerOptions)
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

    private static BriefingSourceReferenceDto CreateTaskReference(Guid taskId, string title, string status) =>
        new("task", taskId, title, status, null);

    private static BriefingSourceReferenceDto CreateApprovalReference(Guid companyId, Guid approvalId, string approvalType, string status) =>
        new("approval", approvalId, $"{approvalType} approval", status, $"/approvals?companyId={companyId}&approvalId={approvalId}");

    private static IReadOnlyList<BriefingSourceReferenceDto> CreateSourceReference(BriefingAggregateItemDto item)
    {
        if (item.SourceEntityId is null || item.SourceEntityId == Guid.Empty || string.IsNullOrWhiteSpace(item.SourceEntityType))
        {
            return [];
        }

        return [new BriefingSourceReferenceDto(item.SourceEntityType, item.SourceEntityId.Value, item.Title, item.Status, null)];
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

    private Task<CompanyBriefingDto> ToDtoAsync(CompanyBriefing briefing, CancellationToken cancellationToken) =>
        Task.FromResult(ToDto(briefing));

    private static CompanyBriefingDto ToDto(CompanyBriefing briefing)
    {
        var references = briefing.SourceReferences.TryGetValue("items", out var node) && node is not null
            ? JsonSerializer.Deserialize<List<BriefingSourceReferenceDto>>(node.ToJsonString(), SerializerOptions) ?? []
            : [];
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
            briefing.GeneratedUtc);
    }

    private static CompanyBriefingDeliveryPreferenceDto ToDto(CompanyBriefingDeliveryPreference preference) =>
        new(preference.CompanyId, preference.UserId, preference.InAppEnabled, preference.MobileEnabled, preference.DailyEnabled, preference.WeeklyEnabled, preference.PreferredDeliveryTime, preference.PreferredTimezone, preference.UpdatedUtc);

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

    private sealed record BriefingWindow(DateTime StartUtc, DateTime EndUtc);
    private sealed record DueCompany(Guid Id, string Name, string? Timezone);
    private sealed record BriefingItem(string Kind, string Text, Guid EntityId, DateTime OccurredUtc, IReadOnlyList<BriefingSourceReferenceDto> References);
    private sealed record BriefingAggregate(
        IReadOnlyList<BriefingItem> Approvals,
        IReadOnlyList<BriefingItem> KpiHighlights,
        IReadOnlyList<BriefingItem> Alerts,
        IReadOnlyList<BriefingItem> Anomalies,
        IReadOnlyList<BriefingItem> AgentHighlights,
        IReadOnlyList<BriefingSourceReferenceDto> SourceReferences);
    private sealed record BriefingSummaryPayloadItem(string Section, string Title, DateTime OccurredUtc, IReadOnlyList<BriefingSourceReferenceDto> References);
}