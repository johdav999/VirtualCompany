using System.Text.Json;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class RealtimeBufferedSpeechTests
{
    [Fact]
    public void Provider_error_has_safe_categorical_diagnostic_without_payload()
    {
        using var collector = new RealtimeBufferedSpeechCollector(turn);
        var error = Assert.Throws<RealtimeAgentEventException>(() => collector.Accept(Event(1,
            new { type = "error", error = new { message = "private provider payload" } })));
        Assert.Equal("conversation_provider_error", error.Code);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public void Buffered_audio_request_has_audio_sized_budget_and_retains_isolated_output_ownership()
    {
        var id = Guid.NewGuid();
        var state = new OpenAiRealtimeConversationState([]);
        state.AddConfirmed(id, "How does onboarding work?");
        var payload = state.CreateResponse(OpenAiRealtimeAgentSessionGateway.BufferedSpeechResponse(id));
        Assert.Equal(1024, payload["response"]!["max_output_tokens"]!.GetValue<int>());
        Assert.Equal("audio", payload["response"]!["output_modalities"]![0]!.GetValue<string>());
        Assert.Equal("none", payload["response"]!["tool_choice"]!.GetValue<string>());
        Assert.Throws<RealtimeAgentEventException>(() =>
            state.CreateResponse(OpenAiRealtimeAgentSessionGateway.BufferedSpeechResponse(id)));
    }

    private readonly Guid turn = Guid.NewGuid();
    private RealtimeAgentPcmOutput Event(long sequence, object payload, Guid? id = null) =>
        new("session", sequence, DateTime.UtcNow, default, ProviderEventJson: JsonSerializer.Serialize(payload), TurnId: id ?? turn);
    private RealtimeAgentPcmOutput Audio(Guid? id = null, string item = "item_1", long sequence = 1,
        int index = 0, byte marker = 0, int bytes = 960, string response = "resp_1", int contentIndex = 0) =>
        new("session", sequence, DateTime.UtcNow, Enumerable.Repeat(marker, bytes).ToArray(),
            ResponseId: response, ItemId: item, TurnId: id ?? turn, OutputIndex: index, ContentIndex: contentIndex);
    private RealtimeAgentPcmOutput Transcript(string text = "Hello!", string item = "item_1", int index = 0, long sequence = 2) => Event(sequence,
        new { type = "response.output_audio_transcript.done", response_id = "resp_1", item_id = item,
            output_index = index, content_index = 0, transcript = text });
    private RealtimeAgentPcmOutput End(string item = "item_1", int index = 0, long sequence = 3) => Event(sequence,
        new { type = "response.output_audio.done", response_id = "resp_1", item_id = item, output_index = index, content_index = 0 });
    private RealtimeAgentPcmOutput Done(string status = "completed", string text = "Hello!") => Event(4,
        new { type = "response.done", response = new { id = "resp_1", status,
            output = new[] { OutputItem("item_1", text) },
            usage = new { input_tokens = 5, output_tokens = 10 } } });
    private static object OutputItem(string id, string transcript) =>
        new { id, type = "message", role = "assistant", content = new[] { new { type = "audio", transcript } } };
    private RealtimeAgentPcmOutput MultiDone(params object[] items) => Event(20,
        new { type = "response.done", response = new { id = "resp_1", status = "completed", output = items,
            usage = new { input_tokens = 5, output_tokens = 10 } } });

    [Fact]
    public void Interleaved_items_are_reconciled_and_assembled_in_completed_response_order()
    {
        using var collector = new RealtimeBufferedSpeechCollector(turn);
        // Arrival order is deliberately different from playback order. Sequence duplicates
        // still cannot duplicate PCM, and one item's completion cannot end the whole reply.
        Assert.Null(collector.Accept(Audio(item: "item_2", index: 1, marker: 2)));
        Assert.Null(collector.Accept(Audio(item: "item_2", index: 1, marker: 2)));
        Assert.Null(collector.Accept(Audio(sequence: 2, marker: 1)));
        Assert.Null(collector.Accept(Transcript("Let me check.", sequence: 3)));
        Assert.Null(collector.Accept(End(sequence: 4)));
        Assert.Null(collector.Accept(Audio(item: "item_2", index: 1, sequence: 5, marker: 3)));
        Assert.Null(collector.Accept(Transcript("One moment.", "item_2", 1, 6)));
        Assert.Null(collector.Accept(End("item_2", 1, 7)));
        var result = Assert.IsType<RealtimeBufferedSpeech>(collector.Accept(MultiDone(
            OutputItem("item_1", "Let me check."), OutputItem("item_2", "One moment."))));
        Assert.Equal("Let me check. One moment.", result.Text);
        Assert.Equal(Enumerable.Repeat((byte)1, 960).Concat(Enumerable.Repeat((byte)2, 960))
            .Concat(Enumerable.Repeat((byte)3, 960)), result.Pcm);
        Assert.Equal(new[] { new RealtimeBufferedSpeechItem("item_1", 0, 960),
            new RealtimeBufferedSpeechItem("item_2", 960, 1920) }, result.Items);
        Assert.Null(collector.Accept(MultiDone()));
    }

    [Fact]
    public void Reproduced_two_item_response_no_longer_fails_on_second_item()
    {
        // Same item identities, lengths and completion shape as the synthetic provider
        // reproduction; substitute zero PCM (no user audio is stored in tests).
        const string first = "item_ETYq1Gi0TU8NJoRnBbUAp", second = "item_ETYq27mhveIUzqGOBtCQG";
        using var collector = new RealtimeBufferedSpeechCollector(turn);
        collector.Accept(Audio(item: first, bytes: 4850 * 48));
        collector.Accept(Transcript("Let me check.", first));
        collector.Accept(End(first));
        collector.Accept(Audio(item: second, index: 1, sequence: 4, bytes: 4050 * 48));
        collector.Accept(Transcript("One moment.", second, 1, 5));
        collector.Accept(End(second, 1, 6));
        var speech = Assert.IsType<RealtimeBufferedSpeech>(collector.Accept(MultiDone(
            OutputItem(first, "Let me check."), OutputItem(second, "One moment."))));
        Assert.Equal(8900, speech.Pcm.Length / 48);
        Assert.Equal(2, speech.Items!.Count);
    }

    [Theory]
    [InlineData("response")]
    [InlineData("same_index")]
    [InlineData("same_item")]
    [InlineData("gap")]
    [InlineData("content_index")]
    [InlineData("missing_end")]
    [InlineData("missing_transcript")]
    [InlineData("mismatched_transcript")]
    [InlineData("missing_output")]
    [InlineData("extra_output")]
    [InlineData("reordered_output")]
    [InlineData("extra_content")]
    [InlineData("wrong_role")]
    [InlineData("wrong_type")]
    [InlineData("late_delta")]
    [InlineData("foreign_session")]
    public void Multi_item_response_still_fails_closed_on_inconsistent_envelopes(string fault)
    {
        using var collector = new RealtimeBufferedSpeechCollector(turn);
        Assert.Throws<RealtimeAgentEventException>(() =>
        {
            collector.Accept(Audio()); collector.Accept(Transcript()); collector.Accept(End());
            collector.Accept(Audio(item: fault == "same_item" ? "item_1" : "item_2", sequence: 5,
                index: fault == "same_index" ? 0 : fault == "gap" ? 2 : 1,
                response: fault == "response" ? "resp_other" : "resp_1",
                contentIndex: fault == "content_index" ? 1 : 0));
            if (fault != "missing_transcript") collector.Accept(Transcript("One moment.", "item_2", 1, 6));
            if (fault != "missing_end") collector.Accept(End("item_2", 1, 7));
            if (fault == "late_delta") collector.Accept(Audio(item: "item_2", index: 1, sequence: 8));
            if (fault == "foreign_session") collector.Accept(End("item_2", 1, 8) with { ProviderSessionId = "other" });
            var first = OutputItem("item_1", "Hello!");
            var second = OutputItem("item_2", fault == "mismatched_transcript" ? "Different claim" : "One moment.");
            if (fault == "extra_content") second = new { id = "item_2", type = "message", role = "assistant",
                content = new[] { new { type = "audio", transcript = "One moment." }, new { type = "audio", transcript = "Hidden claim" } } };
            if (fault == "wrong_role" || fault == "wrong_type") second = new { id = "item_2",
                type = fault == "wrong_type" ? "function_call" : "message", role = fault == "wrong_role" ? "user" : "assistant",
                content = new[] { new { type = "audio", transcript = "One moment." } } };
            collector.Accept(fault switch
            {
                "missing_output" => MultiDone(first),
                "extra_output" => MultiDone(first, second, OutputItem("item_hidden", "Hidden claim")),
                "reordered_output" => MultiDone(second, first),
                _ => MultiDone(first, second)
            });
        });
    }

    [Fact]
    public void Audio_and_text_limits_apply_to_entire_reply_not_each_item()
    {
        using var audio = new RealtimeBufferedSpeechCollector(turn);
        audio.Accept(Audio(bytes: 480000));
        Assert.Throws<RealtimeAgentEventException>(() => audio.Accept(Audio(item: "item_2", index: 1, sequence: 2, bytes: 480002)));
        using var text = new RealtimeBufferedSpeechCollector(turn);
        text.Accept(Transcript(new string('a', 300)));
        Assert.Throws<RealtimeAgentEventException>(() => text.Accept(Transcript(new string('b', 300), "item_2", 1, 3)));
    }

    [Fact]
    public void Malformed_provider_envelope_is_withheld_with_safe_code()
    {
        using var collector = new RealtimeBufferedSpeechCollector(turn);
        var error = Assert.Throws<RealtimeAgentEventException>(() => collector.Accept(Event(1,
            new { type = "response.output_audio.done", response_id = "resp_1", item_id = "item_1" })));
        Assert.Equal("conversation_audio_invalid_envelope", error.Code);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(500, 500, 0)]
    [InlineData(1000, -1, 0)]
    [InlineData(1250, -1, 250)]
    [InlineData(3000, -1, -1)]
    public void Delivery_receipts_are_item_local_and_exclude_fully_played_items(int delivered, int first, int second)
    {
        var speech = new RealtimeBufferedSpeech(new byte[144000], "Let me check.", "resp_1", 0, 0,
            Items: [new("item_1", 0, 48000), new("item_2", 48000, 96000)]);
        var receipts = OpenAiRealtimeAgentSessionGateway.BufferedSpeechDeliveryReceipts(speech, delivered);
        if (first >= 0) Assert.Contains(("item_1", first), receipts);
        if (second >= 0) Assert.Contains(("item_2", second), receipts);
        Assert.Equal((first >= 0 ? 1 : 0) + (second >= 0 ? 1 : 0), receipts.Count);
        Assert.Throws<RealtimeAgentEventException>(() => OpenAiRealtimeAgentSessionGateway.BufferedSpeechDeliveryReceipts(speech, 3001));
        Assert.Throws<RealtimeAgentEventException>(() => OpenAiRealtimeAgentSessionGateway.BufferedSpeechDeliveryReceipts(
            speech with { Items = [new("item_1", 0, 48000), new("item_2", 0, 96000)] }, delivered));
    }

    [Fact]
    public void No_bytes_are_released_until_matching_complete_audio_and_transcript_then_only_once()
    {
        using var collector = new RealtimeBufferedSpeechCollector(turn);
        Assert.Null(collector.Accept(Audio()));
        Assert.Null(collector.Accept(Audio())); // Provider duplicate cannot duplicate PCM.
        Assert.Null(collector.Accept(Transcript()));
        Assert.Null(collector.Accept(End()));
        var result = Assert.IsType<RealtimeBufferedSpeech>(collector.Accept(Done()));
        Assert.Equal(960, result.Pcm.Length);
        Assert.Equal("Hello!", result.Text);
        Assert.Equal(10, result.OutputTokens);
        Assert.Null(collector.Accept(Done()));
    }

    [Theory]
    [InlineData("turn")]
    [InlineData("item")]
    [InlineData("failed")]
    [InlineData("text")]
    [InlineData("incomplete")]
    public void Mismatched_or_incomplete_output_fails_closed(string fault)
    {
        using var collector = new RealtimeBufferedSpeechCollector(turn);
        Assert.Throws<RealtimeAgentEventException>(() =>
        {
            collector.Accept(Audio(fault == "turn" ? Guid.NewGuid() : turn, fault == "item" ? "other" : "item_1"));
            collector.Accept(Transcript());
            if (fault != "incomplete") collector.Accept(End());
            collector.Accept(Done(fault == "failed" ? "failed" : "completed", fault == "text" ? "Different claim" : "Hello!"));
        });
    }

    [Fact]
    public void Automatic_tool_selection_is_not_pinned_and_stale_input_cannot_generate_audio()
    {
        var state = new OpenAiRealtimeConversationState([new("respond_social", "Social reply", "{\"type\":\"object\"}", "recommend")]);
        state.AddConfirmed(turn, "Welcome Alex");
        var request = state.CreateResponse(new(turn, false, KeepProviderContext: true, AutomaticToolChoice: true));
        Assert.Equal("auto", request["response"]!["tool_choice"]!.GetValue<string>());
        Assert.Contains("Welcome Alex", state.ConfirmedContext(turn));
        state.AddConfirmed(Guid.NewGuid(), "Another question");
        Assert.Throws<RealtimeAgentEventException>(() => state.ConfirmedContext(turn));
    }
}
