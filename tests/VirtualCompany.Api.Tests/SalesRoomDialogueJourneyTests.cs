using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Fact]
    public async Task Dialogue_journey_preserves_checkpoint_through_greeting_grounded_answer_and_late_resume()
    {
        // Production services/relational state with deterministic provider and media seams.
        // This is not microphone recognition or browser audibility acceptance.
        await using var f = await Fixture.Create(true, true);
        var timings = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == SalesRoomBenchmarkTelemetry.MeterName && instrument.Name == "sales.browser_room.latency")
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "operation" && tag.Value is string operation) timings[operation] = value;
        });
        listener.Start();
        var greeting = await PlaybackTurn(f);
        await f.Dialogue(greeting);
        Assert.Single(f.Conversation.Played);
        Assert.Empty(await f.Db.SalesMeetingQuestions.ToListAsync());
        Assert.True(timings.ContainsKey("conversation_audio_generation"));
        Assert.True(timings.ContainsKey("conversation_audio_buffered_to_release"));
        Assert.True(timings.ContainsKey("conversation_first_media_enqueue"));

        var answering = await IndexedAnswerService(f);
        await RetainQuestion(f, "How is onboarding done?");
        await f.Ask("How is onboarding done?", answering);
        f.Db.ChangeTracker.Clear();
        var answer = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Kind == SalesRoomAgentSpeechKinds.Answer);
        await f.Play(answer.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken, (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == answer.Id)).Status);
        var room = await f.Db.SalesBrowserRooms.SingleAsync();
        Assert.True(room.RenewAgentLease(f.Work.LeaseOwnerId, f.Work.Generation, f.Source.Clock.Now.AddMinutes(3), f.Source.Clock.Now));
        await f.Db.SaveChangesAsync();
        f.Source.Clock.Now = f.Source.Clock.Now.AddSeconds(60);
        Assert.NotNull(await f.FreshInput("Thank you"));
        Assert.False(await f.Db.SalesRoomAgentSpeech.AnyAsync(x => x.Status == SalesRoomAgentSpeechStates.Queued));

        // A fresh playback authority is required; never reuse the greeting's old generation.
        f.Db.ChangeTracker.Clear();
        var authority = await SalesRoomConversationPolicy.LoadAsync(f.Db, f.Options, f.Source.Company,
            f.Work.RoomId, f.Participant.Id, f.Source.Clock.Now, default);
        var resume = new SalesRoomDialogueTurn(authority!.Binding, Guid.NewGuid(),
            (await f.Db.SalesRoomParticipants.SingleAsync()).Version, f.Source.Clock.Now.AddSeconds(15));
        var result = await ConversationService(f, f.Db).ExecuteDialoguePlaybackAsync(resume, SalesRoomDialoguePolicy.Resume, default);
        Assert.True(result.Accepted, result.Message);
        f.Db.ChangeTracker.Clear();
        var queued = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Status == SalesRoomAgentSpeechStates.Queued);
        Assert.Equal(resume.Binding.OffsetMilliseconds, queued.OffsetMilliseconds);
        Assert.True((await ConversationService(f, f.Db).ExecuteDialoguePlaybackAsync(resume, SalesRoomDialoguePolicy.Resume, default)).Accepted);
        Assert.Single(await f.Db.SalesRoomOperations.Where(x => x.Action.StartsWith("dialogue_")).ToListAsync());
        Assert.False(f.Media.Disposed);
        Assert.True((await f.Db.SalesBrowserRooms.SingleAsync()).AgentOutputAudioMilliseconds > 0);
    }
}
