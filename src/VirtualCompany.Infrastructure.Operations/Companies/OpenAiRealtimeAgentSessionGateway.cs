using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class SharedRealtimeAgentOptions
{
    public const string SectionName = "SharedRealtimeAgent";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-realtime-2.1-mini";
    public string Voice { get; set; } = "marin";
    public string TranscriptionModel { get; set; } = "gpt-realtime-whisper";
    public int TimeoutSeconds { get; set; } = 20;
    public int ClientSecretTtlSeconds { get; set; } = 60;
}

public sealed class OpenAiRealtimeAgentSessionGateway(
    IHttpClientFactory clients,
    IOptions<SharedRealtimeAgentOptions> configured,
    ILogger<OpenAiRealtimeAgentSessionGateway> logger) : IRealtimeAgentSessionGateway, IRealtimeAgentPcmSessionGateway, IDisposable
{
    public const string ClientName = "shared-realtime-agent";
    private const int MaximumSocketMessageBytes = 128_000;
    private readonly ConcurrentDictionary<string, PcmSocketSession> pcmSessions = new(StringComparer.Ordinal);

    public Task<RealtimeAgentHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        var hasKey = !string.IsNullOrWhiteSpace(ApiKey(options));
        var available = options.Enabled && hasKey;
        return Task.FromResult(new RealtimeAgentHealth(options.Enabled, hasKey, available, "openai", options.Model,
            available ? "available" : "degraded", !options.Enabled ? "feature_disabled" : hasKey ? null : "credentials_missing",
            available ? null : "Realtime voice is unavailable; typed meeting controls remain available."));
    }

    public async Task<RealtimeAgentSessionConnection> CreateSessionAsync(RealtimeAgentSessionCreateRequest request, CancellationToken ct)
    {
        Validate(request);
        var options = configured.Value;
        var apiKey = ApiKey(options);
        if (!options.Enabled) throw new RealtimeAgentUnavailableException("feature_disabled", "Realtime voice is disabled.");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new RealtimeAgentUnavailableException("credentials_missing", "Realtime provider credentials are not configured.");
        var maximum = TimeSpan.FromMinutes(Math.Clamp(request.MaximumDuration.TotalMinutes, 1, 120));
        var expires = DateTime.UtcNow.Add(maximum);
        var session = BuildSession(options, request);
        var http = clients.CreateClient(ClientName);
        http.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 60));
        HttpResponseMessage? response = null;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(request.OfferSdp, Encoding.UTF8), "sdp");
                form.Add(new StringContent(session.ToJsonString(), Encoding.UTF8, "application/json"), "session");
                using var message = new HttpRequestMessage(HttpMethod.Post, "realtime/calls") { Content = form };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                message.Headers.TryAddWithoutValidation("OpenAI-Safety-Identifier", Hash(request.UserId.ToString("N")));
                response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct);
                if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == 1) break;
                var retry = RetryAfter(response);
                if (retry is not (>= 1 and <= 3)) break;
                response.Dispose(); response = null;
                await Task.Delay(TimeSpan.FromSeconds(retry.Value), ct);
            }
            if (response is null) throw new RealtimeAgentUnavailableException("provider_unavailable", "Realtime provider initialization failed.");
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode == HttpStatusCode.TooManyRequests ? "quota_exceeded" : "provider_unavailable";
                logger.LogWarning("OpenAI Realtime initialization failed. StatusCode: {StatusCode}; ProviderRequestId: {RequestId}; ErrorCode: {ErrorCode}.",
                    (int)response.StatusCode, Header(response, "x-request-id"), ProviderErrorCode(body));
                throw new RealtimeAgentUnavailableException(code,
                    code == "quota_exceeded" ? "Realtime voice is currently limited; use typed meeting controls." : "Realtime voice could not start; use typed meeting controls.", RetryAfter(response));
            }
            var location = response.Headers.Location?.ToString() ?? Header(response, "location");
            var callId = location?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(callId) || callId.Length > 200 || callId.Any(x => !char.IsLetterOrDigit(x) && x is not '_' and not '-'))
                throw new RealtimeAgentUnavailableException("invalid_provider_session", "Realtime voice returned an invalid session identifier.");
            return new("openai", callId, options.Model, "webrtc", body, expires);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { throw new RealtimeAgentUnavailableException("provider_timeout", "Realtime voice timed out; use typed meeting controls."); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "OpenAI Realtime connection failed safely.");
            throw new RealtimeAgentUnavailableException("provider_unavailable", "Realtime voice is unreachable; use typed meeting controls.");
        }
        finally { response?.Dispose(); }
    }

    public async Task<RealtimeAgentPcmSessionConnection> CreatePcmSessionAsync(
        RealtimeAgentPcmSessionCreateRequest request,
        CancellationToken ct)
    {
        Validate(request);
        var options = configured.Value;
        var apiKey = ApiKey(options);
        if (!options.Enabled) throw new RealtimeAgentUnavailableException("feature_disabled", "Realtime voice is disabled.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new RealtimeAgentUnavailableException("credentials_missing", "Realtime provider credentials are not configured.");

        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        socket.Options.SetRequestHeader("OpenAI-Safety-Identifier", Hash(request.UserId.ToString("N")));
        var providerSessionId = $"ws_{Guid.NewGuid():N}";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 60)));
            await socket.ConnectAsync(RealtimeSocketUri(options), timeout.Token);
            var session = BuildSession(options, request);
            await SendSocketJsonAsync(socket, new JsonObject
            {
                ["event_id"] = $"evt_{Guid.NewGuid():N}",
                ["type"] = "session.update",
                ["session"] = session
            }.ToJsonString(), ct);
            var maximum = TimeSpan.FromMinutes(Math.Clamp(request.MaximumDuration.TotalMinutes, 1, 120));
            var state = new PcmSocketSession(socket, DateTime.UtcNow.Add(maximum));
            if (!pcmSessions.TryAdd(providerSessionId, state))
                throw new RealtimeAgentUnavailableException("provider_unavailable", "Realtime PCM session allocation failed.");
            return new("openai", providerSessionId, options.Model, 24_000, state.ExpiresUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { socket.Dispose(); throw; }
        catch (OperationCanceledException) { socket.Dispose(); throw new RealtimeAgentUnavailableException("provider_timeout", "Realtime voice timed out; use typed meeting controls."); }
        catch (WebSocketException ex)
        {
            socket.Dispose();
            logger.LogWarning(ex, "OpenAI Realtime server-side PCM connection failed safely.");
            throw new RealtimeAgentUnavailableException("provider_unavailable", "Realtime voice is unreachable; use typed meeting controls.");
        }
    }

    public async Task SendInputAudioAsync(string providerSessionId, ReadOnlyMemory<byte> pcm24KhzMono, CancellationToken ct)
    {
        var state = Pcm(providerSessionId);
        if (pcm24KhzMono.IsEmpty || pcm24KhzMono.Length > 96_000 || pcm24KhzMono.Length % 2 != 0)
            throw new ArgumentException("A bounded PCM16 audio block is required.", nameof(pcm24KhzMono));
        await SendSocketJsonAsync(state, new JsonObject
        {
            ["event_id"] = $"evt_{Guid.NewGuid():N}",
            ["type"] = "input_audio_buffer.append",
            ["audio"] = Convert.ToBase64String(pcm24KhzMono.Span)
        }.ToJsonString(), ct);
    }

    public async IAsyncEnumerable<RealtimeAgentPcmOutput> ReceiveOutputAsync(string providerSessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var state = Pcm(providerSessionId);
        var buffer = new byte[16_384];
        using var message = new MemoryStream();
        while (!ct.IsCancellationRequested && state.Socket.State == WebSocketState.Open && DateTime.UtcNow < state.ExpiresUtc)
        {
            var result = await state.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) yield break;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            message.Write(buffer, 0, result.Count);
            if (message.Length > MaximumSocketMessageBytes)
                throw new RealtimeAgentEventException("event_too_large", "The realtime provider event exceeded the safe size limit.");
            if (!result.EndOfMessage) continue;
            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
            message.SetLength(0);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = String(root, "type") ?? string.Empty;
            var sequence = Interlocked.Increment(ref state.ReceiveSequence);
            var eventId = String(root, "event_id") ?? $"server_{sequence}";
            if (type is "response.output_audio.delta" or "response.audio.delta")
            {
                var delta = String(root, "delta");
                if (string.IsNullOrWhiteSpace(delta)) continue;
                byte[] audio;
                try { audio = Convert.FromBase64String(delta); }
                catch (FormatException) { throw new RealtimeAgentEventException("invalid_audio", "The realtime provider returned invalid audio."); }
                if (audio.Length == 0 || audio.Length > 96_000 || audio.Length % 2 != 0)
                    throw new RealtimeAgentEventException("invalid_audio", "The realtime provider returned an invalid PCM block.");
                yield return new(providerSessionId, sequence, DateTime.UtcNow, audio);
            }
            else
            {
                yield return new(providerSessionId, sequence, DateTime.UtcNow, ReadOnlyMemory<byte>.Empty, eventId, json);
            }
        }
    }

    public Task SendClientEventAsync(string providerSessionId, string clientEventJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientEventJson) || clientEventJson.Length > 64_000)
            throw new ArgumentException("A bounded client event is required.", nameof(clientEventJson));
        try { _ = JsonNode.Parse(clientEventJson) ?? throw new JsonException(); }
        catch (JsonException) { throw new ArgumentException("The client event must be valid JSON.", nameof(clientEventJson)); }
        return SendSocketJsonAsync(Pcm(providerSessionId), clientEventJson, ct);
    }

    public async Task<RealtimeAgentControlResult> CancelPcmResponseAsync(string providerSessionId, string? responseId, CancellationToken ct)
    {
        var payload = new JsonObject { ["event_id"] = $"evt_{Guid.NewGuid():N}", ["type"] = "response.cancel" };
        if (!string.IsNullOrWhiteSpace(responseId)) payload["response_id"] = responseId.Trim();
        await SendSocketJsonAsync(Pcm(providerSessionId), payload.ToJsonString(), ct);
        return new(true);
    }

    public async Task TerminatePcmSessionAsync(string providerSessionId, CancellationToken ct)
    {
        EnsureProviderId(providerSessionId);
        if (!pcmSessions.TryRemove(providerSessionId, out var state)) return;
        await state.DisposeAsync(ct);
    }

    public Task<RealtimeAgentEvent> NormalizeEventAsync(RealtimeAgentProviderEvent providerEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerEvent.ProviderSessionId) || string.IsNullOrWhiteSpace(providerEvent.EventId) ||
            providerEvent.EventId.Length > 200 || providerEvent.Sequence < 1 || string.IsNullOrWhiteSpace(providerEvent.PayloadJson) || providerEvent.PayloadJson.Length > 128_000)
            throw new RealtimeAgentEventException("invalid_event", "The realtime event envelope is invalid.");
        try
        {
            using var document = JsonDocument.Parse(providerEvent.PayloadJson);
            var root = document.RootElement;
            var providerType = String(root, "type") ?? throw new JsonException();
            var result = providerType switch
            {
                "session.created" or "session.updated" => Event(RealtimeAgentEventTypes.Connected),
                "input_audio_buffer.speech_started" => Event(RealtimeAgentEventTypes.ParticipantSpeechStarted,
                    responseId: String(root, "item_id")),
                "conversation.item.input_audio_transcription.completed" => Event(RealtimeAgentEventTypes.ParticipantTranscriptCompleted,
                    text: String(root, "transcript"), speakerId: String(root, "item_id")),
                "response.output_audio_transcript.done" or "response.audio_transcript.done" => Event(RealtimeAgentEventTypes.AgentTranscriptCompleted,
                    text: String(root, "transcript"), responseId: String(root, "response_id")),
                "response.function_call_arguments.done" => Event(RealtimeAgentEventTypes.ToolInvocation,
                    toolCallId: String(root, "call_id") ?? String(root, "item_id"), toolName: String(root, "name"),
                    toolArguments: String(root, "arguments")),
                "response.done" => NormalizeResponseDone(),
                "error" => Event(RealtimeAgentEventTypes.ProviderError,
                    errorCode: NestedString(root, "error", "code") ?? "provider_error",
                    errorSummary: Bounded(NestedString(root, "error", "message"), 1000)),
                _ => throw new RealtimeAgentEventException("unsupported_event", "This realtime provider event is not supported by the meeting pilot.")
            };
            return Task.FromResult(result);

            RealtimeAgentEvent Event(string type, string? text = null, string? speakerId = null, string? toolCallId = null,
                string? toolName = null, string? toolArguments = null, string? responseId = null,
                int inputTokens = 0, int outputTokens = 0, int audioDurationMilliseconds = 0,
                string? errorCode = null, string? errorSummary = null) =>
                new(providerEvent.EventId, providerEvent.Sequence, type, Bounded(text, 8000), speakerId, null,
                    toolCallId, toolName, Bounded(toolArguments, 64_000), responseId, audioDurationMilliseconds,
                    inputTokens, outputTokens, errorCode, errorSummary);

            RealtimeAgentEvent NormalizeResponseDone()
            {
                var status = NestedString(root, "response", "status");
                if (status == "cancelled") return Event(RealtimeAgentEventTypes.ResponseCancelled, responseId: NestedString(root, "response", "id"));
                var input = NestedInt(root, "response", "usage", "input_tokens");
                var output = NestedInt(root, "response", "usage", "output_tokens");
                var billedAudio = NestedInt(root, "response", "usage", "input_audio_duration_ms");
                return Event(RealtimeAgentEventTypes.UsageUpdated, responseId: NestedString(root, "response", "id"),
                    inputTokens: input, outputTokens: output, audioDurationMilliseconds: billedAudio);
            }
        }
        catch (RealtimeAgentEventException) { throw; }
        catch (JsonException) { throw new RealtimeAgentEventException("invalid_provider_event", "The realtime provider event payload is invalid."); }
    }

    public Task<RealtimeAgentControlResult> CancelResponseAsync(string providerSessionId, string? responseId, CancellationToken cancellationToken)
    {
        EnsureProviderId(providerSessionId);
        if (pcmSessions.ContainsKey(providerSessionId))
            return CancelPcmResponseAsync(providerSessionId, responseId, cancellationToken);
        var payload = new JsonObject { ["type"] = "response.cancel" };
        if (!string.IsNullOrWhiteSpace(responseId)) payload["response_id"] = responseId.Trim();
        return Task.FromResult(new RealtimeAgentControlResult(true, payload.ToJsonString()));
    }

    public async Task TerminateSessionAsync(string providerSessionId, CancellationToken ct)
    {
        EnsureProviderId(providerSessionId);
        if (pcmSessions.ContainsKey(providerSessionId))
        {
            await TerminatePcmSessionAsync(providerSessionId, ct);
            return;
        }
        var options = configured.Value;
        var apiKey = ApiKey(options);
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        try
        {
            var http = clients.CreateClient(ClientName);
            http.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 60));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"realtime/calls/{Uri.EscapeDataString(providerSessionId)}/hangup");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.NotFound or HttpStatusCode.Conflict) return;
            logger.LogWarning("OpenAI Realtime hangup returned {StatusCode}; the local session will still stop. ProviderRequestId: {RequestId}.",
                (int)response.StatusCode, Header(response, "x-request-id"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "OpenAI Realtime hangup failed; the local session will still stop."); }
    }

    private static JsonObject BuildSession(SharedRealtimeAgentOptions options, RealtimeAgentSessionCreateRequest request)
    {
        var tools = request.Tools.Select(x => new
        {
            type = "function", name = x.Name, description = x.Description,
            parameters = JsonNode.Parse(x.ParametersJsonSchema)
        }).ToArray();
        return new JsonObject
        {
            ["type"] = "realtime",
            ["model"] = options.Model,
            ["instructions"] = request.Instructions,
            ["audio"] = new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["transcription"] = new JsonObject { ["model"] = options.TranscriptionModel },
                    ["turn_detection"] = new JsonObject { ["type"] = "server_vad", ["create_response"] = false, ["interrupt_response"] = true }
                },
                ["output"] = new JsonObject { ["voice"] = options.Voice }
            },
            ["tools"] = JsonSerializer.SerializeToNode(tools),
            ["tool_choice"] = "auto",
            ["max_output_tokens"] = 1200
        };
    }

    private static JsonObject BuildSession(SharedRealtimeAgentOptions options, RealtimeAgentPcmSessionCreateRequest request)
    {
        var tools = request.Tools.Select(x => new
        {
            type = "function", name = x.Name, description = x.Description,
            parameters = JsonNode.Parse(x.ParametersJsonSchema)
        }).ToArray();
        var input = new JsonObject
        {
            ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = 24_000 },
            ["transcription"] = new JsonObject { ["model"] = options.TranscriptionModel }
        };
        if (request.ManualInputCommit)
            input["turn_detection"] = null;
        else
            input["turn_detection"] = new JsonObject { ["type"] = "server_vad", ["create_response"] = true, ["interrupt_response"] = true };
        return new JsonObject
        {
            ["type"] = "realtime",
            ["model"] = options.Model,
            ["instructions"] = request.Instructions,
            ["audio"] = new JsonObject
            {
                ["input"] = input,
                ["output"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = 24_000 },
                    ["voice"] = options.Voice
                }
            },
            ["tools"] = JsonSerializer.SerializeToNode(tools),
            ["tool_choice"] = "auto",
            ["max_output_tokens"] = 1200
        };
    }

    private static void Validate(RealtimeAgentSessionCreateRequest request)
    {
        if (request.CompanyId == Guid.Empty || request.UserId == Guid.Empty || request.AgentId == Guid.Empty) throw new ArgumentException("Company, user, and agent are required.");
        if (string.IsNullOrWhiteSpace(request.OfferSdp) || request.OfferSdp.Length > 100_000) throw new ArgumentException("A bounded WebRTC offer is required.");
        if (string.IsNullOrWhiteSpace(request.Instructions) || request.Instructions.Length > 16_000) throw new ArgumentException("Bounded realtime instructions are required.");
        if (request.Tools.Count > 10 || request.Tools.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.ParametersJsonSchema.Length > 16_000)) throw new ArgumentException("Realtime tools exceed the bounded contract.");
        foreach (var tool in request.Tools) { try { _ = JsonNode.Parse(tool.ParametersJsonSchema) ?? throw new JsonException(); } catch (JsonException) { throw new ArgumentException("Realtime tool schemas must be valid JSON."); } }
    }

    private static void Validate(RealtimeAgentPcmSessionCreateRequest request)
    {
        if (request.CompanyId == Guid.Empty || request.UserId == Guid.Empty || request.AgentId == Guid.Empty)
            throw new ArgumentException("Company, user, and agent are required.");
        if (string.IsNullOrWhiteSpace(request.Instructions) || request.Instructions.Length > 16_000)
            throw new ArgumentException("Bounded realtime instructions are required.");
        if (request.MaximumDuration <= TimeSpan.Zero || request.Tools.Count > 10 ||
            request.Tools.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.ParametersJsonSchema.Length > 16_000))
            throw new ArgumentException("Realtime PCM session limits are invalid.");
        foreach (var tool in request.Tools)
        {
            try { _ = JsonNode.Parse(tool.ParametersJsonSchema) ?? throw new JsonException(); }
            catch (JsonException) { throw new ArgumentException("Realtime tool schemas must be valid JSON."); }
        }
    }

    private PcmSocketSession Pcm(string providerSessionId)
    {
        EnsureProviderId(providerSessionId);
        return pcmSessions.TryGetValue(providerSessionId, out var state) && state.Socket.State == WebSocketState.Open &&
               DateTime.UtcNow < state.ExpiresUtc
            ? state
            : throw new RealtimeAgentUnavailableException("session_unavailable", "The realtime PCM session is not active.");
    }

    private static Uri RealtimeSocketUri(SharedRealtimeAgentOptions options)
    {
        var builder = new UriBuilder(new Uri(options.BaseUrl, UriKind.Absolute))
        {
            Scheme = options.BaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss",
            Port = -1,
            Path = new Uri(new Uri(options.BaseUrl, UriKind.Absolute), "realtime").AbsolutePath,
            Query = $"model={Uri.EscapeDataString(options.Model)}"
        };
        return builder.Uri;
    }

    private static Task SendSocketJsonAsync(ClientWebSocket socket, string json, CancellationToken ct) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);

    private static async Task SendSocketJsonAsync(PcmSocketSession state, string json, CancellationToken ct)
    {
        await state.SendLock.WaitAsync(ct);
        try { await SendSocketJsonAsync(state.Socket, json, ct); }
        finally { state.SendLock.Release(); }
    }

    private static string ApiKey(SharedRealtimeAgentOptions options) => string.IsNullOrWhiteSpace(options.ApiKey)
        ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty : options.ApiKey;
    private static void EnsureProviderId(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(x => !char.IsLetterOrDigit(x) && x is not '_' and not '-')) throw new ArgumentException("Invalid provider session identifier."); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    private static int? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        var raw = Header(response, "x-ratelimit-reset-requests");
        return double.TryParse(raw?.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? Math.Max(1, (int)Math.Ceiling(seconds)) : null;
    }
    private static string? ProviderErrorCode(string body) { try { using var d = JsonDocument.Parse(body); return NestedString(d.RootElement, "error", "code"); } catch (JsonException) { return null; } }
    private static string? String(JsonElement value, string property) => value.TryGetProperty(property, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static string? NestedString(JsonElement value, params string[] path) { foreach (var part in path) { if (!value.TryGetProperty(part, out value)) return null; } return value.ValueKind == JsonValueKind.String ? value.GetString() : null; }
    private static int NestedInt(JsonElement value, params string[] path) { foreach (var part in path) { if (!value.TryGetProperty(part, out value)) return 0; } return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? Math.Max(0, result) : 0; }
    private static string? Bounded(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    public void Dispose()
    {
        foreach (var session in pcmSessions.Values) session.Dispose();
        pcmSessions.Clear();
    }

    private sealed class PcmSocketSession(ClientWebSocket socket, DateTime expiresUtc) : IDisposable
    {
        public ClientWebSocket Socket { get; } = socket;
        public DateTime ExpiresUtc { get; } = expiresUtc;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public long ReceiveSequence;
        public async Task DisposeAsync(CancellationToken ct)
        {
            try
            {
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "session ended", ct);
            }
            catch (WebSocketException) { }
            Dispose();
        }
        public void Dispose() { Socket.Dispose(); SendLock.Dispose(); }
    }
}
