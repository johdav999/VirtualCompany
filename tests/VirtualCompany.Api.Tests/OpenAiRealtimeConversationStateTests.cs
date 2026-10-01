using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class OpenAiRealtimeConversationStateTests
{
    [Fact]
    public void Routing_with_unanswered_history_explicitly_targets_latest_confirmed_input()
    {
        var state = new OpenAiRealtimeConversationState([ReadTool]);
        state.AddConfirmed(Guid.NewGuid(), "Welcome to the meeting");
        var latest = Guid.NewGuid();
        state.AddConfirmed(latest, "How does onboarding work?");
        var payload = state.CreateResponse(new(latest, false, KeepProviderContext: true,
            AutomaticToolChoice: true, PlaybackContext: "{\"state\":\"paused\"}"));
        var input = payload["response"]!["input"]!.AsArray();
        Assert.Contains("LAST user message only", input[0]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("How does onboarding work?", input.Last()!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Null(payload["response"]!["instructions"]);
    }

    [Fact]
    public void Per_turn_tools_are_advertised_and_enforced_even_if_registered_on_the_session()
    {
        var write = new RealtimeAgentToolDefinition("start_presentation", "Start", "{\"type\":\"object\"}", "execute");
        var state = new OpenAiRealtimeConversationState([ReadTool, write]);
        var turn = Guid.NewGuid(); state.AddConfirmed(turn, "Start the presentation");
        var request = state.CreateResponse(new(turn, false, KeepProviderContext: true, AutomaticToolChoice: true,
            AvailableTools: [ReadTool.Name], PlaybackContext: "{\"state\":\"paused\",\"offset\":120}"));
        Assert.Equal(ReadTool.Name, Assert.Single(request["response"]!["tools"]!.AsArray())!["name"]!.GetValue<string>());
        Assert.Null(request["response"]!["instructions"]); // Never override session policy or output instructions.
        state.ObserveResponse("resp_tools", turn.ToString("N"));
        Assert.Throws<RealtimeAgentEventException>(() => state.AcceptTool("resp_tools", "call_write", write.Name, "{}"));
        Assert.True(state.AcceptTool("resp_tools", "call_read", ReadTool.Name, "{}"));
    }

    [Fact]
    public void Isolated_audio_can_be_truncated_at_delivered_position_after_generation_completed()
    {
        var state = new OpenAiRealtimeConversationState([]);
        var turn = Guid.NewGuid(); state.AddConfirmed(turn, "Hello");
        Assert.Equal("auto", state.CreateResponse(new(turn, true, DefaultAudioConversation: true))["response"]!["conversation"]!.GetValue<string>());
        state.ObserveResponse("resp_audio", turn.ToString("N")); state.AcceptAudio("resp_audio", "item_audio", 48000); state.Complete("resp_audio");
        var receipt = state.Cancel("resp_audio", "item_audio", 125)!.Value;
        var events = OpenAiRealtimeAgentSessionGateway.BuildCancellationEvents("resp_audio", "item_audio", 125,
            receipt.KeepContext, receipt.AudioBytes, receipt.Completed);
        Assert.Equal("conversation.item.truncate", Assert.Single(events)["type"]!.GetValue<string>());
        Assert.Equal(125, events[0]["audio_end_ms"]!.GetValue<int>());
        Assert.Throws<RealtimeAgentEventException>(() => state.Cancel("resp_audio", "item_audio", 1001));
    }
    private static readonly RealtimeAgentToolDefinition ReadTool =
        new("read_approved_source", "Read an approved source", "{\"type\":\"object\"}", "read");

    [Fact]
    public void Multiple_audio_items_belong_to_same_turn_but_truncation_uses_each_items_own_duration()
    {
        var state = new OpenAiRealtimeConversationState([]);
        var turn = Guid.NewGuid(); state.AddConfirmed(turn, "Hello");
        state.CreateResponse(new(turn, true, DefaultAudioConversation: true));
        state.ObserveResponse("resp_audio", turn.ToString("N"));
        Assert.Equal(turn, state.AcceptAudio("resp_audio", "item_first", 48000));
        Assert.Equal(turn, state.AcceptAudio("resp_audio", "item_second", 24000));
        Assert.Equal(turn, state.AcceptAudio("resp_audio", "item_first", 24000));
        state.Complete("resp_audio");
        Assert.Throws<RealtimeAgentEventException>(() => state.Cancel("resp_audio", "item_second", 501));
        Assert.Throws<RealtimeAgentEventException>(() => state.Cancel("resp_audio", "item_unknown", 0));
        var first = state.Cancel("resp_audio", "item_first", 1000)!.Value;
        var second = state.Cancel("resp_audio", "item_second", 0)!.Value;
        Assert.Equal(72000, first.AudioBytes); Assert.Equal(24000, second.AudioBytes);
        var payload = Assert.Single(OpenAiRealtimeAgentSessionGateway.BuildCancellationEvents("resp_audio", "item_second", 0,
            second.KeepContext, second.AudioBytes, second.Completed));
        Assert.Equal("conversation.item.truncate", payload["type"]!.GetValue<string>());
        Assert.Equal("item_second", payload["item_id"]!.GetValue<string>());
        Assert.Equal(0, payload["audio_end_ms"]!.GetValue<int>());
        Assert.Null(state.AcceptAudio("resp_audio", "item_second", 480));
    }

    [Fact]
    public void Multiple_items_cannot_bypass_response_audio_or_item_count_bounds()
    {
        var state = new OpenAiRealtimeConversationState([]);
        var turn = Guid.NewGuid(); state.AddConfirmed(turn, "Hello");
        state.CreateResponse(new(turn, true)); state.ObserveResponse("resp_audio", turn.ToString("N"));
        for (var i = 0; i < 60; i++) state.AcceptAudio("resp_audio", "item_" + i % 2, 96000);
        Assert.Equal("audio_limit", Assert.Throws<RealtimeAgentEventException>(() =>
            state.AcceptAudio("resp_audio", "item_third", 480)).Code);

        var second = Guid.NewGuid(); state.AddConfirmed(second, "Next");
        state.CreateResponse(new(second, true)); state.ObserveResponse("resp_items", second.ToString("N"));
        for (var i = 0; i < 32; i++) state.AcceptAudio("resp_items", "item_" + i, 480);
        Assert.Equal("audio_limit", Assert.Throws<RealtimeAgentEventException>(() =>
            state.AcceptAudio("resp_items", "item_extra", 480)).Code);
    }

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
