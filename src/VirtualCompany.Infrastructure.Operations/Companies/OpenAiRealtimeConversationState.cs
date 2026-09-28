using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Companies;

// All state is owned by one provider socket and discarded on termination. The room/floor remains
// authoritative; this class only correlates provider messages and bounds permitted context.
internal sealed class OpenAiRealtimeConversationState(IReadOnlyList<RealtimeAgentToolDefinition> tools)
{
    private readonly object gate = new();
    private readonly List<(Guid Id, string Role, string Text)> context = [];
    private readonly HashSet<Guid> turnIds = [];
    private readonly HashSet<Guid> playedFollowUps = [];
    private readonly HashSet<Guid> followedUpTurns = [];
    private readonly Dictionary<Guid, bool> requested = [];
    private readonly Dictionary<string, (Guid TurnId, bool KeepContext, int AudioBytes, string? ItemId,
        bool Cancelled, bool Completed)> responses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> pendingCalls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Guid Turn, string Name, string Arguments, string? Output)> toolExchanges = new(StringComparer.Ordinal);
    private readonly HashSet<string> inflightCalls = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedCalls = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> turnsWithCompletedTool = [];
    private readonly HashSet<Guid> continuedToolTurns = [];
    private readonly HashSet<string> toolNames = tools.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
    private int contextCharacters;

    public void AddConfirmed(Guid id, string text)
    {
        ValidateText(id, text);
        lock (gate)
        {
            if (turnIds.Count >= 512)
                throw new RealtimeAgentEventException("context_limit", "The conversation must roll over before accepting another turn.");
            if (!turnIds.Add(id)) throw new RealtimeAgentEventException("duplicate_turn", "This conversation turn was already submitted.");
            context.Add((id, "user", text.Trim()));
            contextCharacters += text.Trim().Length;
            TrimContext();
        }
    }

    public void RecordPlayed(Guid id, string text)
    {
        ValidateText(id, text);
        lock (gate)
        {
            if (!turnIds.Contains(id)) throw new RealtimeAgentEventException("turn_unknown", "The played response has no confirmed turn.");
            if (context.Any(x => x.Id == id && x.Role == "assistant"))
                throw new RealtimeAgentEventException("duplicate_playback", "This response was already recorded.");
            context.Add((id, "assistant", text.Trim()));
            contextCharacters += text.Trim().Length;
            TrimContext();
        }
    }

    public void RecordPlayedFollowUp(Guid originatingTurnId, Guid followUpId, string text)
    {
        ValidateText(followUpId, text);
        if (originatingTurnId == Guid.Empty || originatingTurnId == followUpId)
            throw new RealtimeAgentEventException("followup_uncorrelated", "The follow-up must belong to a distinct confirmed turn.");
        lock (gate)
        {
            if (!turnIds.Contains(originatingTurnId) ||
                !context.Any(x => x.Id == originatingTurnId && x.Role == "assistant") ||
                playedFollowUps.Contains(followUpId) || followedUpTurns.Contains(originatingTurnId))
                throw new RealtimeAgentEventException("followup_uncorrelated", "Only a distinct played follow-up can enter context.");
            playedFollowUps.Add(followUpId); followedUpTurns.Add(originatingTurnId);
            context.Add((followUpId, "assistant", text.Trim()));
            contextCharacters += text.Trim().Length;
            TrimContext();
        }
    }

    public JsonObject CreateResponse(RealtimeConversationResponseRequest request)
    {
        if (request.TurnId == Guid.Empty || request.MaximumOutputTokens is < 1 or > 1024)
            throw new ArgumentException("A bounded confirmed turn is required.", nameof(request));
        if (request.KeepProviderContext && request.Audio)
            throw new RealtimeAgentEventException("unheard_context", "A tool proposal must be text-only before entering provider history.");
        if (request.RequiredToolName is not null && (!request.KeepProviderContext || !toolNames.Contains(request.RequiredToolName)))
            throw new RealtimeAgentEventException("tool_invalid", "The requested tool is not registered for this session.");
        lock (gate)
        {
            if (context.Count == 0 || context[^1].Id != request.TurnId || context[^1].Role != "user" ||
                !requested.TryAdd(request.TurnId, request.KeepProviderContext))
                throw new RealtimeAgentEventException("turn_not_current", "Only the latest confirmed turn can request a response once.");
            // An out-of-band response cannot place unheard generated audio into provider history.
            var input = new JsonArray();
            foreach (var item in context)
                input.Add(new JsonObject { ["type"] = "message", ["role"] = item.Role,
                    ["content"] = new JsonArray(new JsonObject { ["type"] = item.Role == "user" ? "input_text" : "output_text",
                        ["text"] = item.Text }) });
            return new JsonObject { ["event_id"] = $"evt_{Guid.NewGuid():N}", ["type"] = "response.create",
                ["response"] = new JsonObject { ["conversation"] = "none",
                    ["metadata"] = new JsonObject { ["turn_id"] = request.TurnId.ToString("N") },
                    ["output_modalities"] = new JsonArray(request.Audio ? "audio" : "text"),
                    ["tool_choice"] = request.RequiredToolName is { } required
                        ? new JsonObject { ["type"] = "function", ["name"] = required }
                        : JsonValue.Create(request.KeepProviderContext && toolNames.Count > 0 ? "required" : "none"),
                    ["max_output_tokens"] = request.MaximumOutputTokens, ["input"] = input } };
        }
    }

    public JsonObject CreateToolContinuation(RealtimeConversationResponseRequest request)
    {
        if (request.TurnId == Guid.Empty || !request.KeepProviderContext ||
            request.MaximumOutputTokens is < 1 or > 1024)
            throw new ArgumentException("A bounded tool continuation is required.", nameof(request));
        lock (gate)
        {
            if (!turnsWithCompletedTool.Contains(request.TurnId) ||
                !continuedToolTurns.Add(request.TurnId))
                throw new RealtimeAgentEventException("tool_uncorrelated", "No completed tool result is awaiting this continuation.");
            // Custom-input responses do not place tool calls in the default conversation.
            // Carry the exact accepted call/result pair explicitly, rather than submitting
            // an orphan function_call_output to that unrelated conversation.
            var input = new JsonArray();
            foreach (var exchange in toolExchanges.Where(x => x.Value.Turn == request.TurnId &&
                         x.Value.Output is not null && completedCalls.Contains(x.Key)))
            {
                input.Add(new JsonObject { ["type"] = "function_call", ["call_id"] = exchange.Key,
                    ["name"] = exchange.Value.Name, ["arguments"] = exchange.Value.Arguments });
                input.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = exchange.Key,
                    ["output"] = exchange.Value.Output });
            }
            if (input.Count == 0) throw new RealtimeAgentEventException("tool_uncorrelated", "No completed result is available.");
            return new JsonObject { ["event_id"] = $"evt_{Guid.NewGuid():N}", ["type"] = "response.create",
                ["response"] = new JsonObject { ["conversation"] = "none", ["input"] = input,
                    ["instructions"] = "Acknowledge receipt of the tool result in at most five words. Do not call tools or generate an answer or speech.",
                    ["metadata"] = new JsonObject { ["turn_id"] = request.TurnId.ToString("N") },
                    ["output_modalities"] = new JsonArray(request.Audio ? "audio" : "text"),
                    ["tool_choice"] = "none",
                    ["max_output_tokens"] = request.MaximumOutputTokens } };
        }
    }

    public void ObserveResponse(string id, string? turnId)
    {
        if (!ValidId(id) || !Guid.TryParseExact(turnId, "N", out var turn))
            throw new RealtimeAgentEventException("response_uncorrelated", "The provider response has no known turn.");
        lock (gate)
        {
            if (!requested.TryGetValue(turn, out var keepContext) || responses.Count >= 512 ||
                !responses.TryAdd(id, (turn, keepContext, 0, null, false, false)))
                throw new RealtimeAgentEventException("response_uncorrelated", "The provider response has no known turn.");
        }
    }

    public Guid? ResponseTurn(string responseId)
    {
        lock (gate)
            return responses.TryGetValue(responseId, out var response) && !response.Cancelled
                ? response.TurnId : null;
    }

    public Guid? AcceptAudio(string responseId, string itemId, int bytes)
    {
        if (!ValidId(responseId) || !ValidId(itemId) || bytes is < 2 or > 96_000 || bytes % 2 != 0)
            throw new RealtimeAgentEventException("invalid_audio", "The provider returned an invalid audio block.");
        lock (gate)
        {
            if (!responses.TryGetValue(responseId, out var response) || response.Cancelled || response.Completed) return null;
            if (response.ItemId is not null && response.ItemId != itemId)
                throw new RealtimeAgentEventException("audio_uncorrelated", "The response audio changed output items.");
            if (response.AudioBytes + bytes > 5_760_000)
                throw new RealtimeAgentEventException("audio_limit", "The provider response exceeded the two-minute audio bound.");
            responses[responseId] = response with { AudioBytes = response.AudioBytes + bytes, ItemId = itemId };
            return response.TurnId;
        }
    }

    public (Guid TurnId, bool KeepContext, int AudioBytes, string? ItemId)? Cancel(string responseId,
        string? itemId = null, int? playedMilliseconds = null)
    {
        lock (gate)
        {
            if (!responses.TryGetValue(responseId, out var response)) return null;
            if (playedMilliseconds.HasValue &&
                (itemId is not null && response.ItemId != itemId ||
                 response.KeepContext && (response.ItemId != itemId || playedMilliseconds.Value < 0 ||
                                          playedMilliseconds.Value > response.AudioBytes / 48)))
                throw new RealtimeAgentEventException("playback_uncorrelated", "The output item or played position is not verified.");
            responses[responseId] = response with { Cancelled = true };
            return (response.TurnId, response.KeepContext, response.AudioBytes, response.ItemId);
        }
    }

    public void Complete(string responseId)
    {
        lock (gate)
        {
            if (responses.TryGetValue(responseId, out var response))
                responses[responseId] = response with { Completed = true };
        }
    }

    public Guid? TurnForResponse(string? responseId)
    {
        lock (gate) return responseId is not null && responses.TryGetValue(responseId, out var r) ? r.TurnId : null;
    }

    public bool AcceptTool(string responseId, string callId, string name, string arguments)
    {
        if (!ValidId(responseId) || !ValidId(callId) || !toolNames.Contains(name) ||
            arguments.Length is < 2 or > 4096)
            throw new RealtimeAgentEventException("tool_invalid", "The provider returned an unregistered or oversized tool call.");
        try { using var json = JsonDocument.Parse(arguments); if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
        catch (JsonException) { throw new RealtimeAgentEventException("tool_invalid", "The provider returned malformed tool arguments."); }
        lock (gate)
        {
            if (!responses.TryGetValue(responseId, out var response) || response.Cancelled || !response.KeepContext)
                throw new RealtimeAgentEventException("tool_uncorrelated", "The tool call does not belong to an active response.");
            if (completedCalls.Contains(callId) || pendingCalls.ContainsKey(callId))
            {
                RealtimeConversationTelemetry.RecordDuplicateTool(); // No provider call IDs, text, or tenant labels.
                return false;
            }
            if (completedCalls.Count + pendingCalls.Count >= 64)
                throw new RealtimeAgentEventException("tool_limit", "The conversation tool-call limit was reached.");
            pendingCalls.Add(callId, responseId);
            toolExchanges.Add(callId, (response.TurnId, name, arguments, null));
            return true;
        }
    }

    public void ValidateToolResult(string callId, string output)
    {
        if (!ValidId(callId) || output.Length is < 1 or > 8192)
            throw new RealtimeAgentEventException("tool_result_invalid", "The tool result is invalid or too large.");
        try { using var json = JsonDocument.Parse(output); }
        catch (JsonException) { throw new RealtimeAgentEventException("tool_result_invalid", "The tool result must be JSON."); }
        lock (gate)
        {
            if (!pendingCalls.TryGetValue(callId, out var responseId) ||
                !responses.TryGetValue(responseId, out var response) || response.Cancelled)
                throw new RealtimeAgentEventException("tool_uncorrelated", "The tool result has no pending authorized call.");
        }
    }

    public void CompleteToolResult(string callId)
    {
        lock (gate)
        {
            if (pendingCalls.Remove(callId, out var responseId) && responses.TryGetValue(responseId, out var response))
                turnsWithCompletedTool.Add(response.TurnId);
            inflightCalls.Remove(callId); completedCalls.Add(callId);
        }
    }

    public void ReserveToolResult(string callId, string output)
    {
        ValidateToolResult(callId, output);
        lock (gate)
        {
            if (!pendingCalls.ContainsKey(callId) || !inflightCalls.Add(callId))
                throw new RealtimeAgentEventException("tool_duplicate", "The tool result has already been submitted.");
            toolExchanges[callId] = toolExchanges[callId] with { Output = output };
        }
    }

    private void TrimContext()
    {
        while (context.Count > 8 || contextCharacters > 8000)
        {
            contextCharacters -= context[0].Text.Length;
            context.RemoveAt(0);
        }
    }

    private static void ValidateText(Guid id, string text)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(text) || text.Length > 2000)
            throw new ArgumentException("A bounded, identified conversation text is required.");
    }
    internal static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
        value.All(x => char.IsLetterOrDigit(x) || x is '_' or '-');
}
