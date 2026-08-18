using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class GuidedDialogueHealthCheck(IOptions<GuidedDialogueOptions> configured) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = configured.Value;
        if (!options.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Guided workshops are disabled by configuration.",
                new Dictionary<string, object> { ["state"] = "disabled", ["voice"] = "disabled" }));
        }

        var apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            : options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Guided workshops are enabled but provider credentials are unavailable.",
                data: new Dictionary<string, object> { ["state"] = "unavailable", ["voice"] = "unavailable" }));
        }

        if (!options.RealtimeEnabled)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Text guided workshops are configured; Realtime voice is disabled.",
                data: new Dictionary<string, object> { ["state"] = "degraded", ["voice"] = "disabled" }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Guided text and Realtime voice configuration are available.",
            new Dictionary<string, object> { ["state"] = "available", ["voice"] = "available" }));
    }
}
