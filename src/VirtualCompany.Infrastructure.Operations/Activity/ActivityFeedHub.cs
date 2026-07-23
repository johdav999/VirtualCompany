using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Activity;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Activity;

[Authorize(Policy = CompanyPolicies.AuthenticatedUser)]
public sealed class ActivityFeedHub : Hub
{
    public const string Route = "/hubs/activity-feed";
    public const string EventName = "activityEventReceived";

    private readonly ICompanyMembershipContextResolver _membershipContextResolver;

    public ActivityFeedHub(ICompanyMembershipContextResolver membershipContextResolver)
    {
        _membershipContextResolver = membershipContextResolver;
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryReadTenantId(out var tenantId) ||
            await _membershipContextResolver.ResolveAsync(tenantId, Context.ConnectionAborted) is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid tenantId) => $"activity-feed:{tenantId:N}";

    private bool TryReadTenantId(out Guid tenantId)
    {
        var request = Context.GetHttpContext()?.Request;
        var value = request?.Query["tenantId"].FirstOrDefault() ?? request?.Query["companyId"].FirstOrDefault();
        return Guid.TryParse(value, out tenantId) && tenantId != Guid.Empty;
    }
}

public sealed class SignalRActivityEventPublisher : IActivityEventPublisher
{
    private readonly IHubContext<ActivityFeedHub> _hubContext;
    private readonly ILogger<SignalRActivityEventPublisher> _logger;

    public SignalRActivityEventPublisher(
        IHubContext<ActivityFeedHub> hubContext,
        ILogger<SignalRActivityEventPublisher> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(ActivityFeedHub.GroupName(activityEvent.TenantId))
            .SendAsync(ActivityFeedHub.EventName, activityEvent, cancellationToken);

        _logger.LogInformation(
            "Published activity event {ActivityEventId} to tenant activity feed for tenant {TenantId} with correlation {CorrelationId}.",
            activityEvent.EventId,
            activityEvent.TenantId,
            activityEvent.CorrelationId);
    }
}
