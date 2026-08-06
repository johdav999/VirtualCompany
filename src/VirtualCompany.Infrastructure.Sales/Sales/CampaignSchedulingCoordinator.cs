using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class CampaignSchedulingCoordinator : ICampaignSchedulingCoordinator
{
    private readonly VirtualCompanyDbContext _db;
    private readonly ISequenceExecutionService _sequences;
    private readonly ICompanyTaskCommandService _tasks;
    private readonly IApprovalRequestService _approvals;
    private readonly IAgentHandoffService _handoffs;
    private readonly ILogger<CampaignSchedulingCoordinator> _logger;

    public CampaignSchedulingCoordinator(
        VirtualCompanyDbContext db,
        ISequenceExecutionService sequences,
        ICompanyTaskCommandService tasks,
        IApprovalRequestService approvals,
        IAgentHandoffService handoffs,
        ILogger<CampaignSchedulingCoordinator> logger)
    {
        _db = db;
        _sequences = sequences;
        _tasks = tasks;
        _approvals = approvals;
        _handoffs = handoffs;
        _logger = logger;
    }

    public async Task<CampaignSchedulingResult> RunDueWorkAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        utcNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        batchSize = Math.Clamp(batchSize, 1, 100);
        var campaigns = await _db.SalesCampaigns.IgnoreQueryFilters()
            .Where(x => x.LifecycleStatus == CampaignLifecycleStatuses.Scheduled &&
                        x.ScheduledLaunchUtc <= utcNow)
            .OrderBy(x => x.ScheduledLaunchUtc).Take(batchSize).ToListAsync(cancellationToken);
        var started = 0;
        foreach (var campaign in campaigns)
        {
            campaign.Start(utcNow);
            await _db.SaveChangesAsync(cancellationToken);
            await _sequences.ScheduleExecutionsForCampaignAsync(campaign.CompanyId, campaign.Id, cancellationToken);
            started++;
        }

        var activities = await _db.SalesCampaignActivities.IgnoreQueryFilters()
            .Include(x => x.SalesCampaign)
            .Where(x => (x.Status == CampaignActivityStatuses.Planned || x.Status == CampaignActivityStatuses.Ready ||
                         x.Status == CampaignActivityStatuses.Retrying) &&
                        x.PlannedStartUtc <= utcNow && x.DueUtc <= utcNow &&
                        x.SalesCampaign.LifecycleStatus == CampaignLifecycleStatuses.Running)
            .OrderBy(x => x.DueUtc).Take(batchSize).ToListAsync(cancellationToken);
        var advanced = 0;
        var failed = 0;
        foreach (var activity in activities)
        {
            try
            {
                if (activity.DependsOnActivityId.HasValue &&
                    !await _db.SalesCampaignActivities.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(x => x.CompanyId == activity.CompanyId && x.Id == activity.DependsOnActivityId &&
                                       x.Status == CampaignActivityStatuses.Completed, cancellationToken))
                    continue;
                activity.MarkReady();
                if (activity.ExecutionMode == CampaignExecutionModes.Approval)
                {
                    if (!await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                            .AnyAsync(x => x.CompanyId == activity.CompanyId && x.TargetEntityType == "sales_campaign_activity" &&
                                           x.TargetEntityId == activity.Id, cancellationToken))
                    {
                        var requester = activity.OwnerAgentId ?? activity.SalesCampaign.OwnerAgentId
                            ?? throw new InvalidOperationException("Assign an agent before creating campaign approval work.");
                        await _approvals.CreateAsync(activity.CompanyId,
                            new CreateApprovalRequestCommand("sales_campaign_activity", activity.Id, "agent", requester,
                                "campaign_activity", new Dictionary<string, JsonNode?>
                                {
                                    ["campaignId"] = activity.SalesCampaignId.ToString("D"),
                                    ["activityName"] = activity.Name
                                }, RequiredRole: "owner"), cancellationToken);
                    }
                    activity.HoldForApproval();
                }
                else if (activity.ExecutionMode == CampaignExecutionModes.Manual)
                {
                    var token = $"campaign-activity:{activity.Id:N}";
                    await _tasks.CreateTaskAsync(activity.CompanyId,
                        new CreateTaskCommand("campaign_activity", activity.Name,
                            $"Complete the tracked {activity.Channel} activity for campaign {activity.SalesCampaign.Name}.",
                            "normal", activity.DueUtc, activity.OwnerAgentId,
                            new Dictionary<string, JsonNode?>
                            {
                                ["campaignId"] = activity.SalesCampaignId.ToString("D"),
                                ["campaignActivityId"] = activity.Id.ToString("D"),
                                ["executionMode"] = CampaignExecutionModes.Manual
                            }, CorrelationId: token), cancellationToken);
                    activity.TryClaim(token, utcNow);
                }
                else if (activity.ExecutionMode == CampaignExecutionModes.Handoff)
                {
                    var sender = activity.SalesCampaign.OwnerAgentId
                        ?? throw new InvalidOperationException("Assign Alex as campaign owner before creating a handoff.");
                    var receiver = activity.OwnerAgentId
                        ?? throw new InvalidOperationException("Choose the agent receiving this handoff.");
                    await _handoffs.CreateAsync(activity.CompanyId, sender,
                        new CreateAgentHandoffCommand("campaign_activity", receiver, activity.Name,
                            $"Complete the {activity.Channel} campaign activity.", activity.DueUtc,
                            [$"campaign-activity:{activity.Id:D}"]),
                        cancellationToken);
                    activity.TryClaim($"campaign-handoff:{activity.Id:N}", utcNow);
                }
                else if (activity.Channel == "email")
                {
                    await _sequences.ScheduleExecutionsForCampaignAsync(activity.CompanyId, activity.SalesCampaignId, cancellationToken);
                    activity.Complete("Delegated to the governed email sequence executor.");
                }
                else
                {
                    activity.Fail("The required provider tool is not registered for this channel.", retryable: false);
                }
                await _db.SaveChangesAsync(cancellationToken);
                advanced++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity.Fail("Campaign activity could not be advanced. Review its configuration and retry.", retryable: true);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogError(ex,
                    "Campaign activity scheduling failed. CompanyId={CompanyId} CampaignId={CampaignId} ActivityId={ActivityId} Mode={Mode} Channel={Channel}",
                    activity.CompanyId, activity.SalesCampaignId, activity.Id, activity.ExecutionMode, activity.Channel);
                failed++;
            }
        }
        return new CampaignSchedulingResult(started, advanced, failed);
    }
}

public sealed class CampaignSchedulingWorkerOptions
{
    public const string SectionName = "Sales:CampaignSchedulingWorker";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 50;
}

public sealed class CampaignSchedulingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<CampaignSchedulingWorkerOptions> _options;
    private readonly ILogger<CampaignSchedulingBackgroundService> _logger;

    public CampaignSchedulingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<CampaignSchedulingWorkerOptions> options,
        ILogger<CampaignSchedulingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var result = await scope.ServiceProvider.GetRequiredService<ICampaignSchedulingCoordinator>()
                        .RunDueWorkAsync(DateTime.UtcNow, options.BatchSize, stoppingToken);
                    if (result.CampaignsStarted + result.ActivitiesAdvanced + result.ActivitiesFailed > 0)
                        _logger.LogInformation(
                            "Campaign scheduling completed. CampaignsStarted={CampaignsStarted} ActivitiesAdvanced={ActivitiesAdvanced} ActivitiesFailed={ActivitiesFailed}",
                            result.CampaignsStarted, result.ActivitiesAdvanced, result.ActivitiesFailed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Campaign scheduling loop failed.");
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 5, 300)), stoppingToken);
        }
    }
}
