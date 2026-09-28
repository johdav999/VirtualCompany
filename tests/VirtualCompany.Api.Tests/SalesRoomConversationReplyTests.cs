using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed partial class SalesRoomPlaybackWorkerTests
{
    [Fact]
    public async Task Incomplete_question_cannot_reach_retrieval_even_if_intent_model_calls_it_a_question()
    {
        await using var f = await Fixture.Create(true);
        f.Reasoner.Intent = AgentConversationIntent.Question;
        f.Reasoner.Complete = false;
        var answering = new GroundedAcceptanceAnswer(f);
        await f.Ask("question how", answering);
        Assert.Equal(0, answering.Calls);
        Assert.Empty(await f.Db.SalesMeetingQuestions.ToListAsync());
    }

    [Theory]
    [InlineData("Of course, yeah", AgentConversationIntent.Acknowledgement)]
    [InlineData("What", AgentConversationIntent.Unknown)]
    public async Task Captured_non_question_does_not_trigger_a_generic_company_answer(string heard, AgentConversationIntent intent)
    {
        await using var f = await Fixture.Create(true);
        f.Reasoner.Intent = intent;
        var answering = new GroundedAcceptanceAnswer(f);
        await f.Ask(heard, answering);
        Assert.Equal(0, answering.Calls);
        Assert.Empty(await f.Db.SalesMeetingQuestions.ToListAsync());
    }

    [Fact]
    public async Task Confirmed_onboarding_question_still_reaches_grounded_answering()
    {
        await using var f = await Fixture.Create(true);
        f.Reasoner.Intent = AgentConversationIntent.Question;
        var answering = new GroundedAcceptanceAnswer(f);
        await f.Ask("A question, how does the onboarding work?", answering);
        Assert.Equal(1, answering.Calls);
        Assert.Single(await f.Db.SalesMeetingQuestions.ToListAsync());
    }

    [Fact]
    public async Task Explicit_continuation_after_answer_does_not_require_a_generated_follow_up()
    {
        await using var f = await Fixture.Create(true);
        var turn = await PrepareContinuation(f, generateBridge: false);
        Assert.Empty(await f.Db.SalesRoomAgentSpeech.Where(x => x.Kind == SalesRoomAgentSpeechKinds.Bridge).ToListAsync());
        Assert.True(SalesRoomAgentWorker.ShouldTreatInterruptedSpeechAsAddressedQuestion(
            "Can you continue presenting?", false, 1, false));
        // Contextual interpretation must take precedence over that legacy question heuristic.
        Assert.True(SalesRoomAgentWorker.ShouldRouteContextualReply(true, false, true, false, 1,
            "Can you continue presenting?"));
        var result = await ConversationService(f, f.Db).ContinueConversationAsync(turn, default);
        Assert.True(result.Accepted, result.Code);
        var queued = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.CommandId == turn.CommandId);
        Assert.Equal(SalesRoomAgentSpeechKinds.Narration, queued.Kind);
        Assert.Equal(20, queued.OffsetMilliseconds);
        Assert.Single(await f.Db.SalesMeetingQuestions.ToListAsync());
    }

    [Fact]
    public async Task Social_acknowledgement_gets_a_generated_reply_without_lookup_or_resume()
    {
        await using var f = await Fixture.Create(true);
        var turn = (await PrepareContinuation(f)) with { Intent = AgentConversationIntent.Acknowledgement };
        f.Reasoner.Bridge = new("Would you like me to carry on with the presentation?", Guid.NewGuid(), Guid.NewGuid());
        var result = await f.Acknowledge(turn, "Thank you");
        Assert.True(result.Accepted, result.Code);
        Assert.Equal("reply_queued", result.Code);
        f.Db.ChangeTracker.Clear();
        var reply = await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.CommandId == turn.CommandId);
        Assert.Equal(SalesRoomAgentSpeechKinds.Bridge, reply.Kind);
        using var evidence = System.Text.Json.JsonDocument.Parse(reply.EvidenceJson!);
        Assert.Equal(turn.HeardTurnId, evidence.RootElement.GetProperty("ReplyTurnId").GetGuid());
        Assert.Equal(turn.Binding.TurnGeneration, (await f.Db.SalesBrowserRooms.SingleAsync()).AgentTurnGeneration);
        Assert.Single(await f.Db.SalesMeetingQuestions.ToListAsync());
        Assert.True((await f.Acknowledge(turn, "Thank you")).Accepted);
        Assert.Equal(1, await f.Db.SalesRoomAgentSpeech.CountAsync(x => x.CommandId == turn.CommandId));
        await f.Play(reply.Id);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRoomAgentSpeechStates.Spoken, (await f.Db.SalesRoomAgentSpeech.SingleAsync(x => x.Id == reply.Id)).Status);
        var floor = await f.Db.SalesRoomFloors.SingleAsync();
        Assert.Equal(SalesRoomFloorStates.Host, floor.State);
        Assert.Equal(20, floor.ResumeOffsetMilliseconds);
        Assert.Equal(3, f.Media.Completions);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("manual")]
    [InlineData("assisted")]
    [InlineData("consent")]
    [InlineData("expired")]
    [InlineData("changed_during_generation")]
    public async Task Social_reply_rechecks_authority_before_queueing(string scenario)
    {
        await using var f = await Fixture.Create(true);
        var turn = (await PrepareContinuation(f)) with { Intent = AgentConversationIntent.Acknowledgement };
        f.Reasoner.Bridge = new("Would you like to continue the presentation?", Guid.NewGuid(), Guid.NewGuid());
        if (scenario == "company") turn = turn with { Binding = turn.Binding with { CompanyId = Guid.NewGuid() } };
        if (scenario is "manual" or "assisted")
        {
            var floor = await f.Db.SalesRoomFloors.SingleAsync();
            floor.SetMode(scenario, floor.PresentationVersion, f.Source.Clock.Now, true);
        }
        if (scenario == "consent") (await f.Db.SalesRoomParticipants.SingleAsync()).Consent("ai_processing", false);
        if (scenario == "expired") turn = turn with { ExpiresUtc = f.Source.Clock.Now };
        if (scenario == "changed_during_generation") f.Reasoner.BeforeReturn = async () =>
        {
            await using var other = f.OtherDb();
            var floor = await other.SalesRoomFloors.SingleAsync();
            floor.SetMode("manual", floor.PresentationVersion, f.Source.Clock.Now, true);
            await other.SaveChangesAsync();
        };
        await f.Db.SaveChangesAsync();
        Assert.False((await f.Acknowledge(turn, "Thank you")).Accepted);
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.SalesRoomAgentSpeech.AnyAsync(x => x.CommandId == turn.CommandId));
    }
}
