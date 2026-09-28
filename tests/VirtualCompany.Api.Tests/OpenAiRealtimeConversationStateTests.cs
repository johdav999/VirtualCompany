using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class OpenAiRealtimeConversationStateTests
{
    private static readonly RealtimeAgentToolDefinition ReadTool =
        new("read_approved_source", "Read an approved source", "{\"type\":\"object\"}", "read");

    [Fact]
    public void Two_confirmed_turns_use_only_bounded_played_context_and_correlate_audio()
    {
        var state = new OpenAiRealtimeConversationState([ReadTool]);
        var first = Guid.NewGuid();
        state.AddConfirmed(first, "How does onboarding work?");
        var firstRequest = state.CreateResponse(new(first, true));
        Assert.Equal("none", firstRequest["response"]!["conversation"]!.GetValue<string>());
        Assert.Equal("audio", firstRequest["response"]!["output_modalities"]![0]!.GetValue<string>());
        state.ObserveResponse("resp_first", first.ToString("N"));
        Assert.Null(state.AcceptAudio("resp_other", "item_other", 480));
        Assert.Equal(first, state.AcceptAudio("resp_first", "item_first", 480));
        state.RecordPlayed(first, "The approved setup is documented. The schedule needs follow-up.");

        var second = Guid.NewGuid();
        state.AddConfirmed(second, "And who handles it?");
        var request = state.CreateResponse(new(second, false));
        var input = (JsonArray)request["response"]!["input"]!;
        Assert.Equal(3, input.Count);
        Assert.Equal("user", input[0]!["role"]!.GetValue<string>());
        Assert.Equal("assistant", input[1]!["role"]!.GetValue<string>());
        Assert.Equal("user", input[2]!["role"]!.GetValue<string>());
        Assert.Equal("text", request["response"]!["output_modalities"]![0]!.GetValue<string>());
        state.ObserveResponse("resp_second", second.ToString("N"));
        Assert.Equal(second, state.AcceptAudio("resp_second", "item_second", 480));
    }

    [Fact]
    public void Only_actually_played_follow_up_enters_provider_context_once()
    {
        var state = new OpenAiRealtimeConversationState([]);
        var turn = Guid.NewGuid(); var bridge = Guid.NewGuid();
        state.AddConfirmed(turn, "How does onboarding work?");
        Assert.Throws<RealtimeAgentEventException>(() => state.RecordPlayedFollowUp(turn, bridge, "Would you like more detail?"));
        state.RecordPlayed(turn, "Setup is documented, timing is not.");
        state.RecordPlayedFollowUp(turn, bridge, "Would you like more detail?");
        Assert.Throws<RealtimeAgentEventException>(() => state.RecordPlayedFollowUp(turn, bridge, "Would you like more detail?"));
        Assert.Throws<RealtimeAgentEventException>(() => state.RecordPlayedFollowUp(turn, Guid.NewGuid(), "Another follow-up?"));
        var next = Guid.NewGuid(); state.AddConfirmed(next, "What about pricing?");
        var request = state.CreateResponse(new(next, false));
        var input = (JsonArray)request["response"]!["input"]!;
        Assert.Equal(4, input.Count);
        Assert.Equal("Would you like more detail?", input[2]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Social_reply_after_follow_up_is_correlated_to_its_own_confirmed_turn()
    {
        var state = new OpenAiRealtimeConversationState([]);
        var question = Guid.NewGuid();
        state.AddConfirmed(question, "How does onboarding work?");
        state.RecordPlayed(question, "Setup is documented, timing is not.");
        state.RecordPlayedFollowUp(question, Guid.NewGuid(), "Would you like more detail?");
        var thanks = Guid.NewGuid();
        state.AddConfirmed(thanks, "Thank you");
        state.RecordPlayed(thanks, "Would you like to return to the presentation?");
        var resume = Guid.NewGuid();
        state.AddConfirmed(resume, "Can you continue presenting?");
        var request = state.CreateResponse(new(resume, false));
        var input = (JsonArray)request["response"]!["input"]!;
        Assert.Contains(input, item => item?["role"]?.GetValue<string>() == "assistant" &&
            item["content"]![0]!["text"]!.GetValue<string>() == "Would you like to return to the presentation?");
    }

    [Fact]
    public void Tool_calls_require_registered_completed_correlated_bounded_json_and_are_idempotent()
    {
        var state = new OpenAiRealtimeConversationState([ReadTool]);
        var turn = Guid.NewGuid();
        state.AddConfirmed(turn, "Read it");
        Assert.Throws<RealtimeAgentEventException>(() => state.CreateResponse(new(turn, true, KeepProviderContext: true)));
        var proposal = state.CreateResponse(new(turn, false, KeepProviderContext: true));
        Assert.Equal("none", proposal["response"]!["conversation"]!.GetValue<string>());
        Assert.Equal("required", proposal["response"]!["tool_choice"]!.GetValue<string>());
        state.ObserveResponse("resp_one", turn.ToString("N"));
        Assert.Equal(turn, state.ResponseTurn("resp_one"));
        Assert.Null(state.ResponseTurn("resp_unknown"));

        Assert.Throws<RealtimeAgentEventException>(() => state.AcceptTool("resp_bad", "call_one", ReadTool.Name, "{}"));
        Assert.Throws<RealtimeAgentEventException>(() => state.AcceptTool("resp_one", "call_one", "unregistered", "{}"));
        Assert.Throws<RealtimeAgentEventException>(() => state.AcceptTool("resp_one", "call_one", ReadTool.Name, "{broken"));
        Assert.Throws<RealtimeAgentEventException>(() => state.AcceptTool("resp_one", "call_one", ReadTool.Name,
            "{\"x\":\"" + new string('a', 4096) + "\"}"));
        Assert.True(state.AcceptTool("resp_one", "call_one", ReadTool.Name, "{\"source\":\"approved\"}"));
        Assert.False(state.AcceptTool("resp_one", "call_one", ReadTool.Name, "{\"source\":\"approved\"}"));
        Assert.Throws<RealtimeAgentEventException>(() => state.ValidateToolResult("call_other", "{}"));
        Assert.Throws<RealtimeAgentEventException>(() => state.ValidateToolResult("call_one", "not json"));
        state.ValidateToolResult("call_one", "{\"found\":true}");
        state.ReserveToolResult("call_one", "{\"found\":true}");
        Assert.Throws<RealtimeAgentEventException>(() => state.ReserveToolResult("call_one", "{\"found\":true}"));
        state.CompleteToolResult("call_one");
        var continuation = state.CreateToolContinuation(new(turn, true, KeepProviderContext: true));
        Assert.Equal("none", continuation["response"]!["conversation"]!.GetValue<string>());
        Assert.Equal("none", continuation["response"]!["tool_choice"]!.GetValue<string>());
        Assert.Equal("function_call", continuation["response"]!["input"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("call_one", continuation["response"]!["input"]![0]!["call_id"]!.GetValue<string>());
        Assert.Equal("call_one", continuation["response"]!["input"]![1]!["call_id"]!.GetValue<string>());
        Assert.Equal("{\"found\":true}", continuation["response"]!["input"]![1]!["output"]!.GetValue<string>());
        Assert.Throws<RealtimeAgentEventException>(() => state.CreateToolContinuation(
            new(turn, true, KeepProviderContext: true)));
        Assert.False(state.AcceptTool("resp_one", "call_one", ReadTool.Name, "{}"));
        Assert.Throws<RealtimeAgentEventException>(() => state.ValidateToolResult("call_one", "{}"));
    }

    [Fact]
    public void Classified_control_tool_is_pinned_without_allowing_unregistered_tools()
    {
        var state = new OpenAiRealtimeConversationState([ReadTool]); var turn = Guid.NewGuid();
        state.AddConfirmed(turn, "A confirmed turn");
        Assert.Throws<RealtimeAgentEventException>(() => state.CreateResponse(new(turn, false, KeepProviderContext: true, RequiredToolName: "unregistered")));
        var response = state.CreateResponse(new(turn, false, KeepProviderContext: true, RequiredToolName: ReadTool.Name));
        Assert.Equal("function", response["response"]!["tool_choice"]!["type"]!.GetValue<string>());
        Assert.Equal(ReadTool.Name, response["response"]!["tool_choice"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Explicit_tool_context_never_includes_another_turns_result()
    {
        var state = new OpenAiRealtimeConversationState([ReadTool]);
        foreach (var suffix in new[] { "one", "two" })
        {
            var turn = Guid.NewGuid();
            state.AddConfirmed(turn, "Continue presenting");
            state.CreateResponse(new(turn, false, KeepProviderContext: true));
            state.ObserveResponse("resp_" + suffix, turn.ToString("N"));
            state.AcceptTool("resp_" + suffix, "call_" + suffix, ReadTool.Name, "{}");
            Assert.Throws<RealtimeAgentEventException>(() => state.CreateToolContinuation(new(turn, false, KeepProviderContext: true)));
            state.ReserveToolResult("call_" + suffix, "{\"accepted\":true}");
            state.CompleteToolResult("call_" + suffix);
            var continuation = state.CreateToolContinuation(new(turn, false, KeepProviderContext: true));
            var input = (JsonArray)continuation["response"]!["input"]!;
            Assert.Equal(2, input.Count);
            Assert.All(input, x => Assert.Equal("call_" + suffix, x!["call_id"]!.GetValue<string>()));
        }
    }

    [Fact]
    public void Cancellation_discards_late_audio_and_duplicate_or_ambiguous_responses()
    {
        var state = new OpenAiRealtimeConversationState([]);
        var turn = Guid.NewGuid();
        state.AddConfirmed(turn, "Hello");
        state.CreateResponse(new(turn, true));
        Assert.Throws<RealtimeAgentEventException>(() => state.ObserveResponse("resp_wrong", Guid.NewGuid().ToString("N")));
        state.ObserveResponse("resp_one", turn.ToString("N"));
        Assert.Throws<RealtimeAgentEventException>(() => state.ObserveResponse("resp_one", turn.ToString("N")));
        Assert.Equal(turn, state.AcceptAudio("resp_one", "item_one", 480));
        Assert.Throws<RealtimeAgentEventException>(() => state.Cancel("resp_one", "item_wrong", 10));
        Assert.Equal(turn, state.AcceptAudio("resp_one", "item_one", 480));
        var cancelled = state.Cancel("resp_one");
        Assert.Equal(960, cancelled?.AudioBytes);
        Assert.Null(state.ResponseTurn("resp_one"));
        Assert.Null(state.AcceptAudio("resp_one", "item_one", 480));
        Assert.Throws<RealtimeAgentEventException>(() => state.CreateResponse(new(turn, true)));
        Assert.Throws<RealtimeAgentEventException>(() => state.CreateToolContinuation(new(turn, true, KeepProviderContext: true)));
    }

    [Fact]
    public void Completed_and_rolled_over_sessions_do_not_replay_old_audio_or_context()
    {
        var first = new OpenAiRealtimeConversationState([]);
        var turn = Guid.NewGuid();
        first.AddConfirmed(turn, "Old meeting");
        first.CreateResponse(new(turn, true));
        first.ObserveResponse("resp_old", turn.ToString("N"));
        first.Complete("resp_old");
        Assert.Null(first.AcceptAudio("resp_old", "item_old", 480));

        var next = new OpenAiRealtimeConversationState([]);
        Assert.Null(next.AcceptAudio("resp_old", "item_old", 480));
        Assert.Throws<RealtimeAgentEventException>(() => next.CreateResponse(new(turn, true)));
        var newTurn = Guid.NewGuid();
        next.AddConfirmed(newTurn, "New meeting");
        var request = next.CreateResponse(new(newTurn, false));
        var input = (JsonArray)request["response"]!["input"]!;
        Assert.Single(input);
        Assert.DoesNotContain("Old meeting", request.ToJsonString(), StringComparison.Ordinal);
    }
}
