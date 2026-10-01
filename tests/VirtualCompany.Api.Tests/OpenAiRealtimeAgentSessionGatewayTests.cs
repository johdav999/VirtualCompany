using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class OpenAiRealtimeAgentSessionGatewayTests
{
    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("auto")]
    public void Semantic_profile_leaves_completion_to_provider_and_responses_to_application(string eagerness)
    {
        var method = typeof(OpenAiRealtimeAgentSessionGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name == "BuildSession" && x.GetParameters()[1].ParameterType == typeof(RealtimeAgentPcmSessionCreateRequest));
        var request = new RealtimeAgentPcmSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "conversation", "Transcribe full turns", [], TimeSpan.FromMinutes(5),
            ConversationProfile: true, SemanticVadEagerness: eagerness);
        var options = new SharedRealtimeAgentOptions();
        var session = Assert.IsType<JsonObject>(method.Invoke(null, [options, request]));
        var vad = session["audio"]!["input"]!["turn_detection"]!;
        Assert.Equal("semantic_vad", vad["type"]!.GetValue<string>());
        Assert.Equal(eagerness, vad["eagerness"]!.GetValue<string>());
        Assert.False(vad["create_response"]!.GetValue<bool>());
        Assert.False(vad["interrupt_response"]!.GetValue<bool>());
        Assert.Equal(options.Model, session["model"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true, true, "low")]
    [InlineData(false, false, "low")]
    [InlineData(true, false, "unknown")]
    public void Invalid_semantic_profile_is_rejected_before_connecting(bool conversation, bool manual, string eagerness)
    {
        var validate = typeof(OpenAiRealtimeAgentSessionGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name == "Validate" && x.GetParameters()[0].ParameterType == typeof(RealtimeAgentPcmSessionCreateRequest));
        var request = new RealtimeAgentPcmSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "conversation", "Transcribe", [], TimeSpan.FromMinutes(5), ManualInputCommit: manual,
            ConversationProfile: conversation, SemanticVadEagerness: eagerness);
        Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [request])).InnerException);
    }

    [Theory]
    [InlineData("input_audio_buffer.speech_started", RealtimeAgentEventTypes.ParticipantSpeechStarted)]
    [InlineData("input_audio_buffer.speech_stopped", RealtimeAgentEventTypes.ParticipantSpeechStopped)]
    [InlineData("input_audio_buffer.committed", RealtimeAgentEventTypes.InputCommitted)]
    [InlineData("conversation.item.input_audio_transcription.completed", RealtimeAgentEventTypes.ParticipantTranscriptCompleted)]
    [InlineData("conversation.item.input_audio_transcription.failed", RealtimeAgentEventTypes.ParticipantTranscriptFailed)]
    public async Task Semantic_events_keep_item_identity_and_session_audio_offsets(string providerType, string type)
    {
        var gateway = Create(new RecordingHandler());
        var result = await gateway.NormalizeEventAsync(new("call_test_123", "evt", 1,
            $$"""{"type":"{{providerType}}","item_id":"item-1","audio_start_ms":25,"audio_end_ms":1400,"transcript":"How do you onboard?"}"""), default);
        Assert.Equal(type, result.Type); Assert.Equal("item-1", result.ItemId);
        if (type == RealtimeAgentEventTypes.ParticipantSpeechStarted) Assert.Equal(25, result.AudioStartMilliseconds);
        if (type == RealtimeAgentEventTypes.ParticipantSpeechStopped) Assert.Equal(1400, result.AudioEndMilliseconds);
    }

    [Fact]
    public async Task Session_creation_keeps_server_secret_out_of_the_client_contract()
    {
        var handler = new RecordingHandler();
        var gateway = Create(handler);
        var result = await gateway.CreateSessionAsync(new RealtimeAgentSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "test", "offer-sdp", "Safe instructions",
            [new("read_state", "Read state", "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}", "read")],
            TimeSpan.FromMinutes(5)), CancellationToken.None);

        Assert.Equal("answer-sdp", result.AnswerSdp);
        Assert.Equal("call_test_123", result.ProviderSessionId);
        Assert.Null(result.EphemeralClientSecret);
        Assert.Equal("Bearer server-secret", handler.Authorization);
        Assert.DoesNotContain("server-secret", result.AnswerSdp, StringComparison.Ordinal);
        Assert.Contains("read_state", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_events_are_normalized_without_leaking_provider_shapes()
    {
        var gateway = Create(new RecordingHandler());
        var transcript = await gateway.NormalizeEventAsync(new("call_test_123", "evt-1", 1,
            "{\"type\":\"conversation.item.input_audio_transcription.completed\",\"transcript\":\"What is the price?\",\"item_id\":\"speaker-1\"}"), CancellationToken.None);
        var tool = await gateway.NormalizeEventAsync(new("call_test_123", "evt-2", 2,
            "{\"type\":\"response.function_call_arguments.done\",\"call_id\":\"call-1\",\"name\":\"ask_grounded_question\",\"arguments\":\"{\\\"question\\\":\\\"What is the price?\\\"}\"}"), CancellationToken.None);

        Assert.Equal(RealtimeAgentEventTypes.ParticipantTranscriptCompleted, transcript.Type);
        Assert.Equal("What is the price?", transcript.Text);
        Assert.Equal(RealtimeAgentEventTypes.ToolInvocation, tool.Type);
        Assert.Equal("ask_grounded_question", tool.ToolName);
    }

    [Fact]
    public async Task Provider_usage_keeps_reported_billed_audio_separate_from_local_forwarded_audio()
    {
        var gateway = Create(new RecordingHandler());
        var usage = await gateway.NormalizeEventAsync(new("call_test_123", "evt-3", 3,
            "{\"type\":\"response.done\",\"response\":{\"id\":\"response-1\",\"status\":\"completed\",\"usage\":{\"input_tokens\":42,\"output_tokens\":7,\"input_audio_duration_ms\":1560}}}"), CancellationToken.None);

        Assert.Equal(RealtimeAgentEventTypes.UsageUpdated, usage.Type);
        Assert.Equal(1560, usage.AudioDurationMilliseconds);
        Assert.Equal(42, usage.InputTokens);
        Assert.Equal(7, usage.OutputTokens);
    }

    [Fact]
    public void Browser_pcm_session_disables_provider_turn_detection_for_explicit_local_VAD_commits()
    {
        var method = typeof(OpenAiRealtimeAgentSessionGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name == "BuildSession" && x.GetParameters()[1].ParameterType == typeof(RealtimeAgentPcmSessionCreateRequest));
        var request = new RealtimeAgentPcmSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "segmented_transcription", "Transcribe committed utterances only.", [], TimeSpan.FromMinutes(5), ManualInputCommit: true);
        var session = Assert.IsType<JsonObject>(method.Invoke(null, [new SharedRealtimeAgentOptions(), request]));
        var input = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(session["audio"])["input"]);

        Assert.True(input.ContainsKey("turn_detection"));
        Assert.Null(input["turn_detection"]);
    }

    [Fact]
    public void Automatic_pcm_turn_completion_does_not_bypass_local_speech_aware_interruption()
    {
        var method = typeof(OpenAiRealtimeAgentSessionGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name == "BuildSession" && x.GetParameters()[1].ParameterType == typeof(RealtimeAgentPcmSessionCreateRequest));
        var request = new RealtimeAgentPcmSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "teams", "Handle turns.", [], TimeSpan.FromMinutes(5));
        var session = Assert.IsType<JsonObject>(method.Invoke(null, [new SharedRealtimeAgentOptions(), request]));
        var detection = session["audio"]!["input"]!["turn_detection"]!;
        Assert.Equal("server_vad", detection["type"]!.GetValue<string>());
        Assert.True(detection["create_response"]!.GetValue<bool>());
        Assert.False(detection["interrupt_response"]!.GetValue<bool>());
    }

    [Fact]
    public void Conversational_pcm_profile_keeps_application_turn_ownership_and_far_field_noise_processing()
    {
        var method = typeof(OpenAiRealtimeAgentSessionGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name == "BuildSession" && x.GetParameters()[1].ParameterType == typeof(RealtimeAgentPcmSessionCreateRequest));
        var request = new RealtimeAgentPcmSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "sales_conversation", "Listen only to committed turns.", [], TimeSpan.FromMinutes(5),
            ConversationProfile: true);
        var session = Assert.IsType<JsonObject>(method.Invoke(null, [new SharedRealtimeAgentOptions(), request]));
        var input = session["audio"]!["input"]!;

        Assert.Null(input["turn_detection"]);
        Assert.Equal("far_field", input["noise_reduction"]!["type"]!.GetValue<string>());
        Assert.Equal(256, session["max_output_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Completed_function_call_item_is_normalized_without_using_streamed_fragments()
    {
        var gateway = Create(new RecordingHandler());
        var completed = await gateway.NormalizeEventAsync(new("call_test_123", "evt-tool", 1,
            "{\"type\":\"response.output_item.done\",\"response_id\":\"resp_one\",\"item\":{\"type\":\"function_call\",\"status\":\"completed\",\"call_id\":\"call_one\",\"name\":\"read_state\",\"arguments\":\"{\\\"id\\\":1}\"}}"), CancellationToken.None);

        Assert.Equal(RealtimeAgentEventTypes.ToolInvocation, completed.Type);
        Assert.Equal("resp_one", completed.ResponseId);
        Assert.Equal("call_one", completed.ToolCallId);
        Assert.Equal("{\"id\":1}", completed.ToolArgumentsJson);
        await Assert.ThrowsAsync<RealtimeAgentEventException>(() => gateway.NormalizeEventAsync(new("call_test_123",
            "evt-delta", 2, "{\"type\":\"response.function_call_arguments.delta\",\"delta\":\"{\\\"id\\\"\"}"), CancellationToken.None));
    }

    [Fact]
    public void Cancellation_truncates_only_identified_default_conversation_audio_actually_received()
    {
        var outOfBand = OpenAiRealtimeAgentSessionGateway.BuildCancellationEvents("resp_one", null, 100,
            false, 4_800);
        Assert.Single(outOfBand);
        Assert.Equal("response.cancel", outOfBand[0]["type"]!.GetValue<string>());

        var retained = OpenAiRealtimeAgentSessionGateway.BuildCancellationEvents("resp_one", "item_one", 100,
            true, 4_800);
        Assert.Equal(2, retained.Count);
        Assert.Equal("conversation.item.truncate", retained[1]["type"]!.GetValue<string>());
        Assert.Equal(100, retained[1]["audio_end_ms"]!.GetValue<int>());
        Assert.Throws<RealtimeAgentEventException>(() => OpenAiRealtimeAgentSessionGateway.BuildCancellationEvents(
            "resp_one", "item_one", 101, true, 4_800));
        Assert.Throws<RealtimeAgentEventException>(() => OpenAiRealtimeAgentSessionGateway.BuildCancellationEvents(
            "resp_one", null, 100, true, 4_800));
    }

    [Fact]
    public void Direct_webrtc_session_does_not_let_provider_acoustic_onset_cancel_output()
    {
        var method = typeof(OpenAiRealtimeAgentSessionGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name == "BuildSession" && x.GetParameters()[1].ParameterType == typeof(RealtimeAgentSessionCreateRequest));
        var request = new RealtimeAgentSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "webrtc", "Handle turns.", "offer-sdp", [], TimeSpan.FromMinutes(5));
        var session = Assert.IsType<JsonObject>(method.Invoke(null, [new SharedRealtimeAgentOptions(), request]));
        var detection = session["audio"]!["input"]!["turn_detection"]!;

        Assert.Equal("server_vad", detection["type"]!.GetValue<string>());
        Assert.False(detection["create_response"]!.GetValue<bool>());
        Assert.False(detection["interrupt_response"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(null, "marin")]
    [InlineData("cedar", "cedar")]
    public void Pcm_session_uses_selected_voice_without_changing_shared_default(string? voice, string expected)
    {
        var method = typeof(OpenAiRealtimeAgentSessionGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name == "BuildSession" && x.GetParameters()[1].ParameterType == typeof(RealtimeAgentPcmSessionCreateRequest));
        var options = new SharedRealtimeAgentOptions { Voice = "marin" };
        var request = new RealtimeAgentPcmSessionCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "approved_sales_narration", "Read the approved script.", [], TimeSpan.FromMinutes(2), Voice: voice);
        var session = Assert.IsType<JsonObject>(method.Invoke(null, [options, request]));
        Assert.Equal(expected, session["audio"]!["output"]!["voice"]!.GetValue<string>());
        Assert.Equal("marin", options.Voice);
    }

    private static OpenAiRealtimeAgentSessionGateway Create(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        return new OpenAiRealtimeAgentSessionGateway(new Factory(http), Options.Create(new SharedRealtimeAgentOptions
        {
            Enabled = true, ApiKey = "server-secret", BaseUrl = "https://api.openai.com/v1/"
        }), NullLogger<OpenAiRealtimeAgentSessionGateway>.Instance);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string Body { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("answer-sdp", Encoding.UTF8, "application/sdp")
            };
            response.Headers.Location = new Uri("https://api.openai.com/v1/realtime/calls/call_test_123");
            return response;
        }
    }
}
