using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class TeamsMeetingMediaCoordinator(
    IServiceScopeFactory scopes,
    ITeamsMeetingMediaAdapter media,
    ITeamsAudioSocketPlatform mediaPlatform,
    ITeamsRealtimeAudioBridge bridge,
    IRealtimeAgentPcmSessionGateway realtime,
    IOptions<TeamsPresenterOptions> configured,
    ITeamsMediaHostRuntime mediaHost,
    TimeProvider timeProvider,
    ILogger<TeamsMeetingMediaCoordinator> logger) : ITeamsMeetingMediaCoordinator, IDisposable
{
    private readonly ConcurrentDictionary<Guid, ActiveRun> _runs = new();

    private readonly SemaphoreSlim _startGate = new(1, 1);

    public async Task StartIfReadyAsync(Guid companyId, Guid teamsCallId, CancellationToken ct)
    {
        await _startGate.WaitAsync(ct);
        try { await StartCoreAsync(companyId, teamsCallId, ct); }
        finally { _startGate.Release(); }
    }

    private async Task StartCoreAsync(Guid companyId, Guid teamsCallId, CancellationToken ct)
    {
        if (_runs.ContainsKey(teamsCallId)) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var call = await db.TeamsMeetingCalls.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == teamsCallId, ct);
        if (call is null || call.State != TeamsMeetingCallStates.Connected ||
            !call.MediaStartAuthorizedUtc.HasValue || string.IsNullOrWhiteSpace(call.ProviderCallId)) return;
        var meeting = await db.SalesMeetingSessions.IgnoreQueryFilters().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == call.MeetingSessionId, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (meeting.ConsentStatus != SalesMeetingConsentStatus.Granted || meeting.RetentionUntilUtc <= now) return;

        var active = await db.SalesMeetingVoiceSessions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.CompanyId == companyId && x.MeetingSessionId == meeting.Id && x.EndedUtc == null && x.ExpiresUtc > now, ct);
        if (active is not null) return;
        var rollout = await scope.ServiceProvider.GetRequiredService<ITeamsPresenterRolloutPolicy>()
            .EvaluateAsync(companyId, call.OrganizerUserId, null, ct, meeting.Id);
        if (!rollout.Allowed) throw new TeamsCallControlException(rollout.ReasonCode, rollout.Message);
        var presenter = await scope.ServiceProvider.GetRequiredService<ITeamsMeetingPresenterService>().ResolveAsync(companyId, meeting.Id, ct);
        if (call.PresenterAgentId != presenter.AgentId) throw new TeamsCallControlException(TeamsCallControlProblemCodes.NotReady, "The call presenter binding changed.");
        var options = configured.Value;
        if (!string.Equals(call.MediaHostInstanceId, mediaHost.GetStatus().HostInstanceId, StringComparison.Ordinal)) return;
        await mediaHost.ReserveCallAsync(teamsCallId, ct);
        var health = await media.GetHealthAsync(ct);
        if (!health.Available)
        {
            await ReleaseIfTerminalAsync(teamsCallId, ct);
            return;
        }
        var duration = TimeSpan.FromMinutes(Math.Clamp(options.MaximumMediaMinutes, 1, 120));
        if (meeting.RetentionUntilUtc < now.Add(duration)) duration = meeting.RetentionUntilUtc - now;
        if (duration <= TimeSpan.FromSeconds(30))
        {
            await ReleaseIfTerminalAsync(teamsCallId, ct);
            return;
        }
        var voice = new SalesMeetingVoiceSession(Guid.NewGuid(), companyId, meeting.Id, presenter.AgentId,
            call.OrganizerUserId, health.Route, now.Add(duration), now);
        db.SalesMeetingVoiceSessions.Add(voice);
        await db.SaveChangesAsync(ct);

        RealtimeAgentPcmSessionConnection? connection = null;
        try
        {
            connection = await realtime.CreatePcmSessionAsync(new RealtimeAgentPcmSessionCreateRequest(companyId,
                call.OrganizerUserId, presenter.AgentId, "sales_meeting_teams_audio", presenter.Instructions,
                presenter.Tools, duration, $"teams-call:{teamsCallId:N}"), ct);
            voice.Activate(connection.Provider, connection.ProviderSessionId, connection.Model, "server_pcm_websocket",
                connection.ExpiresUtc < voice.ExpiresUtc ? connection.ExpiresUtc : voice.ExpiresUtc,
                timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
            var binding = new TeamsMeetingMediaBinding(companyId, meeting.Id, call.Id, voice.Id, presenter.AgentId,
                call.ConsentEvidenceVersion, call.MediaHostInstanceId, call.ProviderCallId);
            await media.AttachAsync(new TeamsMeetingMediaAttachRequest(binding, TeamsAudioFormat.Pcm16KMono20Ms,
                options.MediaBufferFrames, duration), ct);
            var cancellation = new CancellationTokenSource();
            var task = bridge.RunAsync(binding, connection.ProviderSessionId, cancellation.Token);
            var run = new ActiveRun(voice.Id, connection.ProviderSessionId, cancellation, task);
            if (!_runs.TryAdd(teamsCallId, run))
            {
                cancellation.Cancel();
                await media.TerminateAsync(voice.Id, "duplicate_start", CancellationToken.None);
                await realtime.TerminatePcmSessionAsync(connection.ProviderSessionId, CancellationToken.None);
                return;
            }
            _ = ObserveAsync(teamsCallId, run);
        }
        catch (Exception exception)
        {
            voice.Fail("teams_media_start_failed", "Teams audio could not start; typed meeting controls remain available.",
                SalesMeetingVoiceSessionStatus.Failed, timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(CancellationToken.None);
            if (connection is not null) await realtime.TerminatePcmSessionAsync(connection.ProviderSessionId, CancellationToken.None);
            await ReleaseIfTerminalAsync(teamsCallId, CancellationToken.None);
            logger.LogWarning(exception, "Teams media start failed safely for call {CallId}.", teamsCallId);
        }
    }

    public async Task StopAsync(Guid teamsCallId, string reason, CancellationToken ct)
    {
        if (!_runs.TryRemove(teamsCallId, out var run))
        {
            await ReleaseIfTerminalAsync(teamsCallId, ct);
            return;
        }
        run.Cancellation.Cancel();
        try
        {
            await media.TerminateAsync(run.VoiceSessionId, reason, ct);
            await realtime.TerminatePcmSessionAsync(run.ProviderSessionId, ct);
        }
        finally
        {
            await MarkVoiceStoppedAsync(run.VoiceSessionId, reason, CancellationToken.None);
            await ReleaseIfTerminalAsync(teamsCallId, CancellationToken.None);
            run.Cancellation.Dispose();
        }
    }

    public async Task ReconcileHostAsync(string mediaHostInstanceId, CancellationToken ct)
    {
        if (!string.Equals(mediaHostInstanceId, mediaHost.GetStatus().HostInstanceId, StringComparison.Ordinal)) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var owned = await db.TeamsMeetingCalls.IgnoreQueryFilters().AsNoTracking()
            .Where(call => call.MediaHostInstanceId == mediaHostInstanceId)
            .Select(call => new { call.Id, call.CompanyId, call.State, call.OrganizerUserId, call.MeetingSessionId })
            .ToListAsync(ct);
        var deniedIds = new HashSet<Guid>();
        foreach (var call in owned.Where(c => TeamsMeetingCallStates.IsActive(c.State)))
        {
            var decision = await scope.ServiceProvider.GetRequiredService<ITeamsPresenterRolloutPolicy>()
                .EvaluateAsync(call.CompanyId, call.OrganizerUserId, null, ct, call.MeetingSessionId);
            if (!decision.Allowed)
            {
                deniedIds.Add(call.Id);
                await StopAsync(call.Id, decision.ReasonCode, ct);
                await scope.ServiceProvider.GetRequiredService<ITeamsMediaHostDrainExecutor>()
                    .ForceTerminateCallAsync(call.CompanyId, call.Id, decision.ReasonCode, ct);
            }
        }
        var terminalIds = owned.Where(call => call.State is TeamsMeetingCallStates.Ended or
                TeamsMeetingCallStates.Rejected or TeamsMeetingCallStates.Failed)
            .Select(call => call.Id).ToHashSet();
        foreach (var callId in terminalIds)
            await StopAsync(callId, "provider_terminal", ct);
        foreach (var call in owned.Where(call => call.State == TeamsMeetingCallStates.Connected && !deniedIds.Contains(call.Id)))
        {
            if (string.Equals(configured.Value.MediaRoute, "teams_application_hosted", StringComparison.Ordinal) &&
                !mediaPlatform.HasPreparedCall(call.Id))
            {
                using var terminationScope = scopes.CreateScope();
                await terminationScope.ServiceProvider.GetRequiredService<ITeamsMediaHostDrainExecutor>()
                    .ForceTerminateCallAsync(call.CompanyId, call.Id, "owner_media_socket_lost", ct);
                continue;
            }
            await StartIfReadyAsync(call.CompanyId, call.Id, ct);
        }
    }

    private async Task ReleaseIfTerminalAsync(Guid callId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        // Muting audio does not release the VM's still-active Graph call or its native socket.
        // Keep scale-in protection until the durable provider state is terminal.
        if (await db.TeamsMeetingCalls.IgnoreQueryFilters().AsNoTracking().AnyAsync(call => call.Id == callId &&
            call.State != TeamsMeetingCallStates.Ended && call.State != TeamsMeetingCallStates.Failed &&
            call.State != TeamsMeetingCallStates.Rejected, ct)) return;
        await mediaHost.ReleaseCallAsync(callId, ct);
    }

    private async Task ObserveAsync(Guid callId, ActiveRun run)
    {
        try { await run.Task; }
        catch (OperationCanceledException) { }
        catch (Exception exception) { logger.LogWarning(exception, "Teams media bridge stopped in a degraded state for call {CallId}.", callId); }
        finally
        {
            var endedUnexpectedly = _runs.TryRemove(new KeyValuePair<Guid, ActiveRun>(callId, run));
            if (endedUnexpectedly)
            {
                await MarkVoiceFailedAsync(run.VoiceSessionId, CancellationToken.None);
                await ReleaseIfTerminalAsync(callId, CancellationToken.None);
            }
            run.Cancellation.Dispose();
        }
    }

    private async Task MarkVoiceStoppedAsync(Guid voiceSessionId, string reason, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var voice = await db.SalesMeetingVoiceSessions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == voiceSessionId, ct);
        if (voice is null || voice.EndedUtc.HasValue) return;
        voice.Stop(voice.ConcurrencyVersion, reason, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkVoiceFailedAsync(Guid voiceSessionId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var voice = await db.SalesMeetingVoiceSessions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == voiceSessionId, ct);
        if (voice is null || voice.EndedUtc.HasValue) return;
        voice.Fail("teams_media_stream_ended",
            "Teams audio ended unexpectedly; typed meeting controls remain available.",
            SalesMeetingVoiceSessionStatus.Failed, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    public void Dispose()
    {
        foreach (var run in _runs.Values) run.Cancellation.Cancel();
        _runs.Clear();
    }

    private sealed record ActiveRun(Guid VoiceSessionId, string ProviderSessionId,
        CancellationTokenSource Cancellation, Task Task);
}
