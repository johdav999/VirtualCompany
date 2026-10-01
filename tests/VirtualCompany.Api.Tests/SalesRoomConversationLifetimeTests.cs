using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Theory]
    [InlineData("paused", "consent")]
    [InlineData("deck_completed", "ended")]
    [InlineData("paused", "host_stopped")]
    public async Task Running_worker_keeps_media_and_provider_connected_while_idle_and_disposes_on_terminal_event(string stage, string terminal)
    {
        await using var f = await Fixture.Create(true);
        if (stage == "deck_completed")
        {
            await f.Play(f.First.Id);
            await f.Play((await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Status == SalesRoomAgentSpeechStates.Queued)).Id);
        }
        else
        {
            f.First.Fail(SalesRoomAgentSpeechStates.Interrupted, "paused", "Fixture pause", f.Source.Clock.Now);
            var floor = await f.Db.SalesRoomFloors.SingleAsync();
            floor.PauseAt(20, floor.TurnGeneration, f.Source.Clock.Now);
            await f.Db.SaveChangesAsync();
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = f.Run(deadline.Token);
        try
        {
            await f.Pcm.Connected.Task.WaitAsync(deadline.Token);
            f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(20);
            await Task.Delay(1400, deadline.Token);
            Assert.False(running.IsCompleted);
            Assert.Equal(1, f.Transport.Connections);
            Assert.Equal(1, f.Pcm.Connections);
            Assert.Equal(0, f.Pcm.Terminations);
            Assert.False(f.Media.Disposed);
            await using var other = f.OtherDb();
            var room = await other.SalesBrowserRooms.SingleAsync();
            Assert.Equal(f.Source.Clock.Now.AddSeconds(30), room.AgentLeaseExpiresUtc);
            if (terminal == "consent") (await other.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
            if (terminal == "ended") room.Ended();
            if (terminal == "host_stopped") room.StopAgent("host_stopped", null, f.Source.Clock.Now);
            await other.SaveChangesAsync();
            await running.WaitAsync(deadline.Token);
            Assert.True(f.Media.Disposed);
            Assert.Equal(1, f.Pcm.Terminations);
        }
        finally { await deadline.CancelAsync(); await running; }
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("presenting")]
    [InlineData("paused")]
    [InlineData("deck_completed")]
    [InlineData("late_reply")]
    public async Task Fresh_input_enters_conversation_independently_of_playback_or_answer_history(string stage)
    {
        await using var f = await Fixture.Create(true);
        if (stage == "late_reply")
        {
            await PrepareContinuation(f);
            var active = await f.Db.SalesBrowserRooms.SingleAsync();
            Assert.True(active.RenewAgentLease(f.Work.LeaseOwnerId, f.Work.Generation,
                f.Source.Clock.Now.AddMinutes(3), f.Source.Clock.Now));
            await f.Db.SaveChangesAsync();
            f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(60);
        }
        if (stage == "deck_completed")
        {
            await f.Play(f.First.Id);
            var next = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Status == SalesRoomAgentSpeechStates.Queued);
            await f.Play(next.Id);
        }
        f.Db.ChangeTracker.Clear();
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        if (stage is "initial" or "paused")
        {
            floor.PauseAt(20, floor.TurnGeneration, f.Source.Clock.Now);
            await f.Db.SaveChangesAsync();
        }
        var checkpoint = (floor.SlideNumber, floor.TalkingPointIndex, floor.ResumeOffsetMilliseconds);
        f.Reasoner.Intent = AgentConversationIntent.Acknowledgement;
        var result = await f.FreshInput("Welcome to the meeting");
        Assert.NotNull(result);
        Assert.Equal(AgentConversationIntent.Acknowledgement, result.Intent);
        Assert.Equal("Welcome to the meeting", Assert.Single(f.Conversation.Inputs).Text);
        if (stage != "late_reply") Assert.Null(result.PlayedSpeechId);
        f.Db.ChangeTracker.Clear();
        floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(checkpoint, (floor.SlideNumber, floor.TalkingPointIndex, floor.ResumeOffsetMilliseconds));
        Assert.True((await f.Db.SalesBrowserRooms.SingleAsync()).IsAgentOwner(f.Work.LeaseOwnerId, f.Work.Generation, f.Source.Clock.Now));
        Assert.False(f.Media.Disposed);
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
    public async Task Fresh_input_rechecks_current_relational_authority(string change)
    {
        await using var f = await Fixture.Create(true);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        var session = new AgentConversationSession(room.CompanyId, room.AgentId!.Value, room.Id,
            room.MeetingSessionId!.Value, f.Work.LeaseOwnerId, f.Work.Generation);
        if (change == "consent") (await f.Db.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
        if (change == "ended") room.Ended();
        if (change == "expired") f.Source.Clock.Now = room.ExpiresUtc;
        if (change == "owner")
        {
            room.StopAgent("host_stopped", null, f.Source.Clock.Now);
            room.StartAgent(room.AgentId!.Value, room.OrganizerUserId, Guid.NewGuid(), f.Source.Clock.Now.AddMinutes(1), f.Source.Clock.Now);
        }
        if (change == "tenant") session = new(Guid.NewGuid(), room.AgentId!.Value, room.Id,
            room.MeetingSessionId!.Value, f.Work.LeaseOwnerId, f.Work.Generation);
        if (change == "budget") room.RecordAgentAudio(f.Work.LeaseOwnerId, f.Work.Generation, 0, 0,
            f.Options.MaximumInputAudioSeconds * 1000L, 0, 0, 0);
        if (change is "manual" or "assisted")
        {
            var floor = await f.Db.SalesRoomFloors.SingleAsync();
            floor.SetMode(change, floor.PresentationVersion, f.Source.Clock.Now, true);
        }
        await f.Db.SaveChangesAsync();
        Assert.Null(await f.FreshInput("Hello", session));
        Assert.Empty(f.Conversation.Inputs);
    }

    [Fact]
    public async Task Rejected_input_keeps_session_available_for_the_next_question()
    {
        await using var f = await Fixture.Create(true);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        var session = new AgentConversationSession(room.CompanyId, room.AgentId!.Value, room.Id,
            room.MeetingSessionId!.Value, f.Work.LeaseOwnerId, f.Work.Generation);
        f.Conversation.Reject = true;
        Assert.Null(await f.FreshInput("Hello", session));
        f.Conversation.Reject = false;
        f.Reasoner.Intent = AgentConversationIntent.Question;
        Assert.Equal(AgentConversationIntent.Question, (await f.FreshInput("How does onboarding work?", session))!.Intent);
        Assert.False(session.Stopped);
        Assert.False(f.Media.Disposed);
        Assert.Single(f.Conversation.Inputs);
    }

    [Fact]
    public async Task Authority_change_while_interpreting_cannot_authorize_an_old_turn()
    {
        await using var f = await Fixture.Create(true);
        f.Reasoner.BeforeInterpret = async () =>
        {
            await using var other = f.OtherDb();
            var room = await other.SalesBrowserRooms.SingleAsync();
            room.TakeOverAgent("Host took over");
            await other.SaveChangesAsync();
        };
        Assert.Null(await f.FreshInput("How does onboarding work?"));
        Assert.Single(f.Conversation.Inputs);
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("ended")]
    [InlineData("budget")]
    public async Task Long_provider_wait_is_cancelled_when_terminal_authority_changes(string change)
    {
        await using var f = await Fixture.Create(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancelled = false;
        var pending = f.RenewDuring(async ct =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); return true; }
            finally { cancelled = true; }
        }, deadline.Token);
        await using (var other = f.OtherDb())
        {
            var room = await other.SalesBrowserRooms.SingleAsync();
            if (change == "consent") (await other.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
            if (change == "ended") room.Ended();
            if (change == "budget") room.RecordAgentAudio(f.Work.LeaseOwnerId, f.Work.Generation, 0, 0,
                f.Options.MaximumInputAudioSeconds * 1000L, 0, 0, 0);
            await other.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<SalesRoomAgentException>(() => pending);
        Assert.True(cancelled);
    }
}
