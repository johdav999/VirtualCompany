using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Agents;

namespace VirtualCompany.Infrastructure.Sales;

// Created by the room worker from confirmed input and completed playback, never from tool JSON.
internal sealed record SalesRoomConversationTurn(AgentConversationBinding Binding, Guid HeardTurnId,
    Guid PlayedSpeechId, long FloorVersion, DateTime ExpiresUtc, AgentConversationIntent Intent)
{
    public Guid CommandId => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"conversation:{Binding.CompanyId:N}:{Binding.ConversationId:N}:{Binding.OwnerGeneration}:{HeardTurnId:N}"))[..16]);
}

internal sealed record SalesRoomConversationToolResult(bool Accepted, string Code, string Message);

internal static class SalesRoomConversationTools
{
    internal const string Question = "ask_grounded_question", Continue = "request_presentation_continuation",
        Wait = "wait_for_participant";
    internal static readonly RealtimeAgentToolDefinition[] Definitions =
    [
        new(Question, "Request a grounded answer to the latest confirmed participant question. Never answer facts yourself.", EmptySchema, "read"),
        new(Continue, "Request continuation from the server checkpoint only after an unambiguous current request. Never claim success before the tool result.", EmptySchema, "execute"),
        new(Wait, "Stay in conversation for a social acknowledgement, a request to wait, or an ambiguous reply. Acknowledgements may receive a brief validated follow-up; they never authorize resuming. Do not infer consent from silence.", EmptySchema, "execute")
    ];
    private const string EmptySchema = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false,\"required\":[]}";

    internal static string ForIntent(AgentConversationIntent intent) => intent switch
    {
        AgentConversationIntent.Question => Question,
        AgentConversationIntent.Continue => Continue,
        _ => Wait
    };

    internal static bool Matches(string? name, string? arguments, AgentConversationIntent intent)
    {
        try
        {
            using var json = JsonDocument.Parse(arguments ?? "null");
            if (json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.EnumerateObject().Any()) return false;
        }
        catch (JsonException) { return false; }
        return name switch
        {
            Question => intent == AgentConversationIntent.Question,
            Continue => intent == AgentConversationIntent.Continue,
            Wait => intent is AgentConversationIntent.Wait or AgentConversationIntent.Unknown or AgentConversationIntent.Acknowledgement,
            _ => false
        };
    }
}
