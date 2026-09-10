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
