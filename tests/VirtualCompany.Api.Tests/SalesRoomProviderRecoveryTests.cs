using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Fact]
    public async Task Provider_recovery_fences_pending_speech_preserves_usage_and_requires_explicit_resume()
    {
        await using var f = await Fixture.Create(true, true);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        room.RecordAgentAudio(f.Work.LeaseOwnerId, f.Work.Generation, 500, 250, 500, 250, 12, 13);
        room.AgentSpeechCompleted(f.Work.LeaseOwnerId, f.Work.Generation, 250);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.PauseAt(120, floor.TurnGeneration, f.Source.Clock.Now);
        await f.Db.SaveChangesAsync();
        var turn = room.AgentTurnGeneration;
        var response = floor.ResponseGeneration;
        Assert.True(await f.Recover());
        f.Db.ChangeTracker.Clear();
        room = await f.Db.SalesBrowserRooms.SingleAsync();
        floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(SalesRoomAgentHealthStates.Starting, room.AgentHealth);
        Assert.Equal("provider_reconnecting", room.AgentLastErrorCode);
        Assert.Equal(f.Work.Generation, room.AgentGeneration);
        Assert.True(room.AgentTurnGeneration > turn);
        Assert.True(floor.ResponseGeneration > response);
        Assert.Equal("paused", floor.State);
        Assert.Equal(120, floor.ResumeOffsetMilliseconds);
        Assert.Equal(500, room.AgentForwardedAudioMilliseconds);
        Assert.Equal(250, room.AgentOutputAudioMilliseconds);
        Assert.Equal(SalesRoomAgentSpeechStates.Interrupted, (await f.Db.SalesRoomAgentSpeech.SingleAsync()).Status);
        Assert.Equal("sales.browser_room.provider_recovery", (await f.Db.AuditEvents.SingleAsync(x =>
            x.Action == "sales.browser_room.provider_recovery")).Action);
        Assert.Equal(0, f.Media.SentFrames);
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("ended")]
    [InlineData("expired")]
    [InlineData("owner")]
    [InlineData("tenant")]
    [InlineData("budget")]
    [InlineData("manual")]
    [InlineData("assisted")]
    [InlineData("presenter")]
    [InlineData("rollback")]
    [InlineData("takeover")]
    [InlineData("removed")]
    public async Task Provider_recovery_rechecks_current_authority_without_mutating_rejected_work(string change)
    {
        await using var f = await Fixture.Create(true, true);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        if (change == "consent") (await f.Db.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
        if (change == "ended") room.Ended();
        if (change == "expired") f.Source.Clock.Now = room.ExpiresUtc;
        if (change == "owner") room.StopAgent("host_stopped", null, f.Source.Clock.Now);
        if (change == "budget") room.RecordAgentAudio(f.Work.LeaseOwnerId, f.Work.Generation, 0, 0,
            f.Options.MaximumInputAudioSeconds * 1000L, 0, 0, 0);
        if (change is "manual" or "assisted") (await f.Db.SalesRoomFloors.SingleAsync()).SetMode(change, 1, f.Source.Clock.Now, true);
        if (change == "presenter") f.Presenter.Denied = true;
        if (change == "rollback") f.Options.SemanticConversationInputEnabled = false;
        if (change == "takeover") room.TakeOverAgent("Host took over");
        if (change == "removed") (await f.Db.SalesRoomParticipants.SingleAsync()).Removed();
        await f.Db.SaveChangesAsync();
        Assert.False(await f.Recover(change == "tenant" ? f.Work with { CompanyId = Guid.NewGuid() } : null));
        Assert.Equal(SalesRoomAgentSpeechStates.Queued, (await f.Db.SalesRoomAgentSpeech.SingleAsync()).Status);
        Assert.Empty(await f.Db.AuditEvents.Where(x => x.Action == "sales.browser_room.provider_recovery").ToListAsync());
        Assert.Equal(0, f.Pcm.Connections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Running_provider_rollover_is_bounded_and_never_replays_old_narration(bool exhausted)
    {
        await using var f = await Fixture.Create(true, true);
        f.First.Fail(SalesRoomAgentSpeechStates.Interrupted, "fixture_pause", "Fixture pause", f.Source.Clock.Now);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.PauseAt(120, floor.TurnGeneration, f.Source.Clock.Now);
        await f.Db.SaveChangesAsync();
        f.Pcm.ExpiresUtc = f.Source.Clock.Now.AddSeconds(1);
        if (!exhausted) f.Pcm.ExpiresUtcAfterFirst = f.Source.Clock.Now.AddHours(1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = f.Run(deadline.Token);
        try
        {
            await f.Pcm.Connected.Task.WaitAsync(deadline.Token);
            f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(2);
            while (!running.IsCompleted && f.Pcm.Connections < (exhausted ? 3 : 2)) await Task.Delay(50, deadline.Token);
            if (exhausted) await running.WaitAsync(deadline.Token);
            else
            {
                await Task.Delay(350, deadline.Token);
                Assert.False(running.IsCompleted);
            }
            await using var other = f.OtherDb();
            var room = await other.SalesBrowserRooms.SingleAsync();
            Assert.Equal(exhausted ? 3 : 2, f.Pcm.Connections);
            Assert.Equal(exhausted ? SalesRoomAgentHealthStates.Paused : SalesRoomAgentHealthStates.Ready, room.AgentHealth);
            if (exhausted) Assert.Equal("provider_recovery_exhausted", room.AgentLastErrorCode);
            Assert.Equal(TimeSpan.FromMinutes(55), f.Pcm.Request!.MaximumDuration);
            Assert.Equal("paused", (await other.SalesRoomFloors.SingleAsync()).State);
            Assert.Equal(120, (await other.SalesRoomFloors.SingleAsync()).ResumeOffsetMilliseconds);
            Assert.Equal(0, f.Media.SentFrames);
        }
        finally { await deadline.CancelAsync(); await running; }
        Assert.Equal(f.Pcm.Connections, f.Pcm.Terminations);
        Assert.True(f.Media.Disposed);
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("rollback")]
    [InlineData("host_stop")]
    public async Task Authority_change_during_recovery_backoff_prevents_a_new_connection(string change)
    {
        await using var f = await Fixture.Create(true, true);
        f.Options.ProviderRecoveryBackoffSeconds = 2;
        f.First.Fail(SalesRoomAgentSpeechStates.Interrupted, "fixture_pause", "Fixture pause", f.Source.Clock.Now);
        (await f.Db.SalesRoomFloors.SingleAsync()).PauseAt(120, f.First.TurnGeneration, f.Source.Clock.Now);
        await f.Db.SaveChangesAsync();
        f.Pcm.ExpiresUtc = f.Source.Clock.Now.AddSeconds(1);
        f.Pcm.ExpiresUtcAfterFirst = f.Source.Clock.Now.AddHours(1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var running = f.Run(deadline.Token);
        try
        {
            await f.Pcm.Connected.Task.WaitAsync(deadline.Token);
            f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(2);
            await using var other = f.OtherDb();
            SalesBrowserRoom room;
            do
            {
                await Task.Delay(50, deadline.Token);
                other.ChangeTracker.Clear(); room = await other.SalesBrowserRooms.SingleAsync(deadline.Token);
            } while (room.AgentLastErrorCode != "provider_reconnecting");
            if (change == "consent") (await other.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
            if (change == "rollback") f.Options.SemanticConversationInputEnabled = false;
            if (change == "host_stop") room.StopAgent("host_stopped", null, f.Source.Clock.Now);
            await other.SaveChangesAsync();
            await running.WaitAsync(deadline.Token);
            Assert.Equal(1, f.Pcm.Connections);
            Assert.Equal(1, f.Pcm.Terminations);
            Assert.Equal(0, f.Media.SentFrames);
            other.ChangeTracker.Clear(); room = await other.SalesBrowserRooms.SingleAsync();
            Assert.Equal(change == "host_stop" ? "host_stopped" : "provider_recovery_denied", room.AgentLastErrorCode);
        }
        finally { await deadline.CancelAsync(); await running; }
    }

    private sealed class RecoveryPresenter(Guid agentId) : ITeamsMeetingPresenterService
    {
        public bool Denied;
        public Task<TeamsPresenterRuntime> ResolveAsync(Guid companyId, Guid meetingId, CancellationToken ct) =>
            Denied ? throw new UnauthorizedAccessException() : Task.FromResult(new TeamsPresenterRuntime(agentId, "Fixture presenter", SalesRoomDialoguePolicy.Tools));
        public Task<TeamsMeetingPresenterDto> GetAsync(Guid c, Guid u, Guid m, CancellationToken ct) => throw new NotSupportedException();
        public Task<TeamsMeetingPresenterDto> SelectAsync(Guid c, Guid u, Guid m, SelectTeamsMeetingPresenter r, CancellationToken ct) => throw new NotSupportedException();
    }
}

public sealed class SalesRoomDialogueConfigurationTests
{
    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("auto")]
    public void Supported_profile_eagerness_and_bounded_recovery_validate(string eagerness)
    {
        var options = new SalesRoomAgentOptions { HybridConversationEnabled = true, SemanticConversationInputEnabled = true, SemanticVadEagerness = eagerness };
        Assert.Null(options.DialogueConfigurationProblem);
        options.HybridConversationEnabled = false;
        Assert.Equal("semantic_profile_requires_conversation", options.DialogueConfigurationProblem);
        options.SemanticConversationInputEnabled = false;
        Assert.Null(options.DialogueConfigurationProblem);
    }

    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(56, 2, 1)]
    [InlineData(55, -1, 1)]
    [InlineData(55, 4, 1)]
    [InlineData(55, 2, 0)]
    [InlineData(55, 2, 11)]
    public void Invalid_provider_bounds_fail_closed(int minutes, int attempts, int backoff)
    {
        var options = new SalesRoomAgentOptions { ProviderSessionMinutes = minutes, MaximumProviderSessionRecoveries = attempts, ProviderRecoveryBackoffSeconds = backoff };
        Assert.Equal("invalid_provider_recovery_limits", options.DialogueConfigurationProblem);
    }

    [Fact]
    public void Unknown_eagerness_fails_closed() => Assert.Equal("invalid_semantic_eagerness",
        new SalesRoomAgentOptions { SemanticVadEagerness = "instant" }.DialogueConfigurationProblem);
}
