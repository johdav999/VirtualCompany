using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingTranscriptHealthCheck(
    VirtualCompanyDbContext db,
    IOptions<SalesMeetingTranscriptOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        if (!value.Enabled) return HealthCheckResult.Healthy("Microsoft Graph meeting transcript integration is disabled.");
        if (!Uri.TryCreate(value.NotificationUrl, UriKind.Absolute, out var notification) || notification.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(value.LifecycleNotificationUrl, UriKind.Absolute, out var lifecycle) || lifecycle.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(value.ClientState) || value.ClientState.Length is < 32 or > 128)
            return HealthCheckResult.Unhealthy("Microsoft Graph meeting transcript webhook configuration is incomplete.",
                data: new Dictionary<string, object> { ["readinessCode"] = "graph_transcript_configuration_invalid" });
        var needsAttention = await db.SalesMeetingTranscriptSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.Status == SalesMeetingTranscriptSubscriptionStatus.PermissionRequired ||
                             x.Status == SalesMeetingTranscriptSubscriptionStatus.RenewalRequired ||
                             x.Status == SalesMeetingTranscriptSubscriptionStatus.Expired ||
                             x.Status == SalesMeetingTranscriptSubscriptionStatus.Failed, cancellationToken);
        return needsAttention == 0
            ? HealthCheckResult.Healthy("Microsoft Graph meeting transcript configuration is ready.")
            : HealthCheckResult.Degraded("One or more Microsoft Graph transcript subscriptions require operator attention.",
                data: new Dictionary<string, object> { ["readinessCode"] = "graph_transcript_subscription_attention", ["subscriptionCount"] = needsAttention });
    }
}
