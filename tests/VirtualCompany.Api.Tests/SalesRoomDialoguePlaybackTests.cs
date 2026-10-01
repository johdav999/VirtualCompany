using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    private sealed class StageRecorder : ISalesPresentationEventPublisher
    {
        public List<SalesPresentationAuthoritativeSnapshotDto> Snapshots { get; } = [];
        public Task PublishAsync(Guid company, Guid session, SalesPresentationAuthoritativeSnapshotDto snapshot, CancellationToken ct)
        { Snapshots.Add(snapshot); return Task.CompletedTask; }
    }

    [Theory]
    [InlineData(SalesRoomDialoguePolicy.Start)]
    [InlineData(SalesRoomDialoguePolicy.Resume)]
    public async Task Voice_playback_command_in_fresh_scope_publishes_new_tenant_bound_render_version(string action)
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await PlaybackTurn(f);
        var result = await f.PlaybackCommand(turn, action);
        Assert.Equal("presentation_queued", result.Code);
        var snapshot = Assert.Single(f.StageEvents.Snapshots);
        Assert.Equal(f.Source.Session, snapshot.Stage.SessionId);
        Assert.True(snapshot.Stage.Version > turn.Binding.PresentationVersion);
        f.Db.ChangeTracker.Clear();
        Assert.Equal((await f.Db.SalesMeetingSessions.SingleAsync()).ConcurrencyVersion, snapshot.Stage.Version);
        Assert.Single(await f.Db.SalesRoomAgentSpeech.Where(x => x.Status == SalesRoomAgentSpeechStates.Queued).ToListAsync());
        // Old render receipts cannot silently authorize the new version.
        Assert.False(await f.Db.SalesRoomPresentationAudience.AnyAsync(x => x.PresentationVersion == snapshot.Stage.Version));
    }

    private static async Task<SalesRoomDialogueTurn> PlaybackTurn(Fixture f)
    {
        await DialogueTurn(f);
        var session = await f.Db.SalesMeetingSessions.SingleAsync();
        session.SetPresentationControlMode("autonomous", session.ConcurrencyVersion, f.Source.Actor, f.Source.Clock.Now);
        session.ApplyPresentationCommand(VirtualCompany.Domain.Enums.SalesPresentationCommandType.Goto, Guid.NewGuid(),
            session.LastPresentationSequence + 1, session.ConcurrencyVersion, 1, 1, null, f.Source.Actor, f.Source.Clock.Now);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.SetMode("autonomous", session.ConcurrencyVersion, f.Source.Clock.Now);
        var deck = await f.Db.SalesPresentationDecks.SingleAsync();
        if (!await f.Db.SalesRoomPresentationAudience.AnyAsync(x => x.PresentationVersion == session.ConcurrencyVersion))
        {
            var audience = new SalesRoomPresentationAudience(f.Source.Company, f.Work.RoomId, f.Participant.Id, f.Participant.Generation,
                deck.Id, deck.Version, 1, session.LastPresentationSequence, session.ConcurrencyVersion, f.Source.Clock.Now, f.Source.Clock.Now.AddSeconds(5));
            audience.Render(f.Source.Clock.Now); f.Db.SalesRoomPresentationAudience.Add(audience);
        }
        await f.Db.SaveChangesAsync();
        var authority = await SalesRoomConversationPolicy.LoadAsync(f.Db, f.Options, f.Source.Company, f.Work.RoomId, f.Participant.Id, f.Source.Clock.Now, default);
        return new(authority!.Binding, Guid.NewGuid(), (await f.Db.SalesRoomParticipants.SingleAsync()).Version, f.Source.Clock.Now.AddSeconds(15));
    }
    [Theory]
    [InlineData(SalesRoomDialoguePolicy.Start, 0)]
    [InlineData(SalesRoomDialoguePolicy.Resume, 20)]
    [InlineData(SalesRoomDialoguePolicy.Pause, 20)]
    public async Task Fresh_dialogue_commands_use_durable_checkpoint_without_answer_history_and_replay_once(string action, int offset)
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await PlaybackTurn(f);
        var service = ConversationService(f, f.Db);
        var result = await service.ExecuteDialoguePlaybackAsync(turn, action, default);
        Assert.True(result.Accepted, result.Code + ": " + result.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(offset, (await f.Db.SalesRoomFloors.SingleAsync()).ResumeOffsetMilliseconds);
        Assert.Empty(await f.Db.SalesMeetingQuestions.ToListAsync());
        var queued = await f.Db.SalesRoomAgentSpeech.Where(x => x.Status == SalesRoomAgentSpeechStates.Queued).ToListAsync();
        Assert.Equal(action == SalesRoomDialoguePolicy.Pause ? 0 : 1, queued.Count);
        if (queued.Count > 0) Assert.Equal(offset, queued[0].OffsetMilliseconds);
        Assert.True((await service.ExecuteDialoguePlaybackAsync(turn, action, default)).Accepted);
        Assert.Single(await f.Db.SalesRoomOperations.Where(x => x.Action.StartsWith("dialogue_")).ToListAsync());
        Assert.False((await service.ExecuteDialoguePlaybackAsync(turn,
            action == SalesRoomDialoguePolicy.Start ? SalesRoomDialoguePolicy.Resume : SalesRoomDialoguePolicy.Start, default)).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_restart_from_later_interrupted_or_answering_slide_resets_both_cursors(bool answering)
    {
        await using var f = await Fixture.Create(true, true);
        await PlaybackTurn(f);
        var now = f.Source.Clock.Now;
        var session = await f.Db.SalesMeetingSessions.SingleAsync();
        session.ApplyPresentationCommand(VirtualCompany.Domain.Enums.SalesPresentationCommandType.Goto, Guid.NewGuid(),
            session.LastPresentationSequence + 1, session.ConcurrencyVersion, 2, 8, null, f.Source.Actor, now);
        session.TransitionTo(VirtualCompany.Domain.Enums.SalesMeetingSessionStatus.Interrupted, 2, 8, "point:8", null, f.Source.Actor, now);
        if (answering) session.TransitionTo(VirtualCompany.Domain.Enums.SalesMeetingSessionStatus.Answering, 2, 8, "point:8", null, f.Source.Actor, now);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        floor.PresentationMoved(f.Participant.Id, session.ConcurrencyVersion, 2, 8, "point:8", now);
        floor.PauseAt(20, floor.TurnGeneration, now);
        var deck = await f.Db.SalesPresentationDecks.SingleAsync();
        var audience = new SalesRoomPresentationAudience(f.Source.Company, f.Work.RoomId, f.Participant.Id, f.Participant.Generation,
            deck.Id, deck.Version, 2, session.LastPresentationSequence, session.ConcurrencyVersion, now, now.AddSeconds(5));
        audience.Render(now); f.Db.SalesRoomPresentationAudience.Add(audience); await f.Db.SaveChangesAsync();
        var authority = await SalesRoomConversationPolicy.LoadAsync(f.Db, f.Options, f.Source.Company, f.Work.RoomId, f.Participant.Id, now, default);
        var turn = new SalesRoomDialogueTurn(authority!.Binding, Guid.NewGuid(), (await f.Db.SalesRoomParticipants.SingleAsync()).Version, now.AddSeconds(15));
        Assert.True((await ConversationService(f, f.Db).ExecuteDialoguePlaybackAsync(turn, SalesRoomDialoguePolicy.Start, default)).Accepted);
        f.Db.ChangeTracker.Clear(); floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(1, floor.SlideNumber); Assert.Equal(1, floor.TalkingPointIndex); Assert.Equal(0, floor.ResumeOffsetMilliseconds);
        var queued = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Status == SalesRoomAgentSpeechStates.Queued);
        Assert.Equal(f.First.NarrationSegmentId, queued.NarrationSegmentId);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("session")]
    [InlineData("expired")]
    [InlineData("manual")]
    [InlineData("assisted")]
    [InlineData("consent")]
    [InlineData("retention")]
    [InlineData("worker")]
    [InlineData("checkpoint")]
    [InlineData("budget")]
    [InlineData("audience")]
    [InlineData("asset")]
    [InlineData("revoked")]
    [InlineData("pending")]
    public async Task Fresh_playback_fails_closed_when_authority_or_release_changes(string change)
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await PlaybackTurn(f);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        switch (change)
        {
            case "company": turn = turn with { Binding = turn.Binding with { CompanyId = Guid.NewGuid() } }; break;
            case "session": turn = turn with { Binding = turn.Binding with { SessionId = Guid.NewGuid() } }; break;
            case "expired": turn = turn with { ExpiresUtc = f.Source.Clock.Now }; break;
            case "manual": case "assisted": floor.SetMode(change, floor.PresentationVersion, f.Source.Clock.Now, true); break;
            case "consent": (await f.Db.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false); break;
            case "retention": (await f.Db.SalesRoomParticipants.SingleAsync()).Consent("retained_transcript", false); break;
            case "worker": turn = turn with { Binding = turn.Binding with { OwnerGeneration = turn.Binding.OwnerGeneration + 1 } }; break;
            case "checkpoint": floor.PauseAt(40, floor.TurnGeneration, f.Source.Clock.Now); break;
            case "budget": f.Options.MaximumSpendPerCallUsd = 0; break;
            case "audience": f.Db.SalesRoomPresentationAudience.RemoveRange(await f.Db.SalesRoomPresentationAudience.ToListAsync()); break;
            case "asset": foreach (var asset in await f.Db.SalesNarrationAssets.ToListAsync()) asset.Status = SalesNarrationAsset.NeedsReview; break;
            case "revoked": (await f.Db.SalesNarrationRevisions.SingleAsync()).RevokedUtc = f.Source.Clock.Now; break;
            case "pending": f.Db.SalesRoomAgentSpeech.Add(new(Guid.NewGuid(), f.Source.Company, f.Work.RoomId, f.Source.Session,
                Guid.NewGuid(), f.First.AgentId, f.Work.Generation, f.First.TurnGeneration, SalesRoomAgentSpeechKinds.Narration,
                f.Source.Actor, f.Source.Clock.Now, f.First.NarrationRevisionId, f.First.NarrationSegmentId)); break;
        }
        await f.Db.SaveChangesAsync();
        var result = await ConversationService(f, f.Db).ExecuteDialoguePlaybackAsync(turn, SalesRoomDialoguePolicy.Resume, default);
        Assert.False(result.Accepted, result.Code);
        Assert.Empty(await f.Db.SalesRoomOperations.Where(x => x.Action.StartsWith("dialogue_")).ToListAsync());
    }

    [Fact]
    public async Task Tool_offer_has_playable_start_resume_and_truthful_partial_receipt_without_invented_heard_text()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await PlaybackTurn(f);
        var request = await f.PlaybackRequest(turn);
        Assert.Contains(SalesRoomDialoguePolicy.Start, request.AvailableTools!);
        Assert.Contains(SalesRoomDialoguePolicy.Resume, request.AvailableTools!);
        Assert.DoesNotContain(SalesRoomDialoguePolicy.Pause, request.AvailableTools!);
        Assert.Contains("savedNarrationOffsetMilliseconds\":20", request.PlaybackContext);
        Assert.Contains("\"completed\":[]", request.PlaybackContext);
        (await f.Db.SalesNarrationRevisions.SingleAsync()).RevokedUtc = f.Source.Clock.Now;
        await f.Db.SaveChangesAsync();
        Assert.DoesNotContain((await f.PlaybackRequest(turn)).AvailableTools!, SalesRoomDialoguePolicy.IsPlayback);
    }

    [Fact]
    public async Task Interrupted_dialogue_flushes_once_preserves_narration_and_hands_off_before_new_output()
    {
        await using var f = await Fixture.Create(true, true);
        var turn = await DialogueTurn(f);
        var fencesBeforeInterruption = 0;
        f.Media.OnSend = async () =>
        {
            fencesBeforeInterruption = f.Media.Cancellations;
            f.Control.CancelResponse(turn.Binding.ResponseGeneration);
            await f.Media.CancelSpeechAsync(default);
        };
        await f.Dialogue(turn);
        f.Db.ChangeTracker.Clear();
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.True(f.Control.AcceptsConfirmedHandoff(turn.Binding.ResponseGeneration, floor));
        Assert.Equal(20, floor.ResumeOffsetMilliseconds);
        Assert.Equal(fencesBeforeInterruption + 1, f.Media.Cancellations);
        Assert.Equal((0, false), Assert.Single(f.Conversation.OutputReceipts));
        Assert.Empty(f.Conversation.Played);
    }

    [Fact]
    public async Task Noncontroller_may_ask_but_cannot_see_or_execute_playback_commands()
    {
        await using var f = await Fixture.Create(true, true);
        await PlaybackTurn(f);
        var guest = new SalesRoomParticipant(f.Source.Company, f.Work.RoomId, "Guest", "test", f.Source.Clock.Now.AddHours(1));
        guest.Admit(); guest.Consent("ai_processing", true); guest.Consent("retained_transcript", true);
        f.Db.SalesRoomParticipants.Add(guest); await f.Db.SaveChangesAsync();
        var authority = await SalesRoomConversationPolicy.LoadAsync(f.Db, f.Options, f.Source.Company, f.Work.RoomId, guest.Id, f.Source.Clock.Now, default);
        var turn = new SalesRoomDialogueTurn(authority!.Binding, Guid.NewGuid(), guest.Version, f.Source.Clock.Now.AddSeconds(15));
        var request = await f.PlaybackRequest(turn);
        Assert.Contains(SalesRoomConversationTools.Question, request.AvailableTools!);
        Assert.DoesNotContain(request.AvailableTools!, SalesRoomDialoguePolicy.IsPlayback);
        Assert.False((await ConversationService(f, f.Db).ExecuteDialoguePlaybackAsync(turn, SalesRoomDialoguePolicy.Start, default)).Accepted);
    }

    [Fact]
    public async Task Output_owner_waits_for_old_cleanup_and_stale_completion_cannot_release_new_owner()
    {
        var control = new SalesRoomAgentRunControl();
        var old = await control.BeginResponseAsync(default);
        var next = control.BeginResponseAsync(default);
        Assert.False(next.IsCompleted);
        control.CancelResponse(); Assert.True(old.IsCancellationRequested);
        control.EndResponse(old);
        var current = await next.WaitAsync(TimeSpan.FromSeconds(2));
        control.EndResponse(old);
        var third = control.BeginResponseAsync(default);
        Assert.False(third.IsCompleted);
        control.EndResponse(current); control.EndResponse(await third);
    }
}
