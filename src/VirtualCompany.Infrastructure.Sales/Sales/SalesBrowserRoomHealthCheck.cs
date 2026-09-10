using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesBrowserRoomHealthCheck(
    IServiceScopeFactory scopes,
    ISalesRoomMediaTransport media,
    IRealtimeAgentSessionGateway realtime,
    IApprovedSpeechGateway speech,
    IOptionsMonitor<SalesRoomLifecycleOptions> lifecycle,
    IOptionsMonitor<SalesRoomAgentOptions> agent,
    TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var roomOptions = lifecycle.CurrentValue;
        var agentOptions = agent.CurrentValue;
        var mediaReadiness = media.GetReadiness();
        var now = clock.GetUtcNow().UtcDateTime;
        var data = new Dictionary<string, object>
        {
            ["enabled"] = roomOptions.Enabled,
            ["admission"] = !roomOptions.Enabled ? "disabled" : roomOptions.DrainEnabled ? "draining" : "open",
            ["mediaState"] = mediaReadiness.State,
            ["mediaReason"] = mediaReadiness.ReasonCode ?? "none",
            ["agentEnabled"] = agentOptions.Enabled,
            ["agentAdmission"] = agentOptions.EmergencyDisabled ? "emergency_disabled" : agentOptions.DrainEnabled ? "draining" : "open",
            ["maximumLiveRoomsPerCompany"] = roomOptions.MaximumLiveRoomsPerCompany,
            ["maximumActiveAgentsGlobal"] = agentOptions.MaximumActiveAgentsGlobal,
            ["maximumActiveAgentsPerCompany"] = agentOptions.MaximumActiveAgentsPerCompany,
            ["maximumSpendPerCall"] = agentOptions.MaximumSpendPerCallUsd,
            ["maximumMonthlySpendPerCompany"] = agentOptions.MaximumMonthlySpendPerCompanyUsd,
            ["costCurrency"] = agentOptions.CostCurrency,
            ["providerRateCheckedUtc"] = agentOptions.ProviderRateCheckedUtc,
        };

        if (!roomOptions.Enabled)
            return HealthCheckResult.Healthy("Browser Sales rooms are disabled; Teams and other meeting routes remain independent.", data);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var activeStates = new[] { SalesRoomAgentHealthStates.Starting, SalesRoomAgentHealthStates.Ready,
            SalesRoomAgentHealthStates.Speaking, SalesRoomAgentHealthStates.Paused };
        var activeRooms = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.State == SalesBrowserRoomStates.Live && x.ExpiresUtc > now, cancellationToken);
        var ambiguousRooms = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.State == SalesBrowserRoomStates.Reconciliation, cancellationToken);
        var activeOwners = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.AgentLeaseOwnerId != null && x.AgentLeaseExpiresUtc > now && activeStates.Contains(x.AgentHealth), cancellationToken);
        var staleOwners = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.AgentLeaseOwnerId != null && x.AgentLeaseExpiresUtc <= now && activeStates.Contains(x.AgentHealth), cancellationToken);
        var unhealthyVoice = await db.SalesBrowserRooms.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.State == SalesBrowserRoomStates.Live && x.AgentId != null &&
            x.AgentVoiceHealth != "healthy" && activeStates.Contains(x.AgentHealth), cancellationToken);
        data["activeRooms"] = activeRooms;
        data["lifecycleAmbiguities"] = ambiguousRooms;
        data["activeAgentOwners"] = activeOwners;
        data["staleAgentOwners"] = staleOwners;
        data["unhealthyVoiceRooms"] = unhealthyVoice;

        var blockers = new List<string>();
        if (!mediaReadiness.Configured) blockers.Add(mediaReadiness.ReasonCode ?? "media_unavailable");
        if (roomOptions.DrainEnabled) blockers.Add("admission_draining");
        if (ambiguousRooms > 0) blockers.Add("lifecycle_reconciliation_required");
        if (staleOwners > 0) blockers.Add("stale_agent_owners");
        if (agentOptions.Enabled)
        {
            var costProblem = SalesRoomOperationsPolicy.ConfigurationProblem(agentOptions, now);
            if (costProblem is not null) blockers.Add(costProblem);
            if (agentOptions.EmergencyDisabled) blockers.Add("agent_emergency_disabled");
            if (agentOptions.DrainEnabled) blockers.Add("agent_draining");
            var provider = await realtime.GetHealthAsync(cancellationToken);
            var voice = await speech.GetProfileAsync(cancellationToken);
            data["providerState"] = provider.Status;
            data["voiceState"] = voice.Available ? "available" : "unavailable";
            if (!provider.Available) blockers.Add(provider.ReasonCode ?? "provider_unavailable");
            if (!voice.Available) blockers.Add("voice_unavailable");
        }
        data["blockingChecks"] = blockers.Distinct(StringComparer.Ordinal).ToArray();
        return blockers.Count == 0
            ? HealthCheckResult.Healthy("Browser Sales-room admission and optional AI are operationally ready.", data)
            : HealthCheckResult.Unhealthy("Browser Sales rooms remain available only through their documented human or typed fallback until blocking checks clear.", data: data);
    }
}
