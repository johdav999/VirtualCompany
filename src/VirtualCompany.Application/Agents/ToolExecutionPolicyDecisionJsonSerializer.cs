using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Agents;

public static class ToolExecutionPolicyDecisionJsonSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static Dictionary<string, JsonNode?> Serialize(ToolExecutionDecisionDto decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var node = JsonSerializer.SerializeToNode(decision, SerializerOptions) as JsonObject ?? new JsonObject();
        return ToDictionary(node);
    }

    public static ToolExecutionDecisionDto Deserialize(IReadOnlyDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return new ToolExecutionDecisionDto(
                PolicyDecisionOutcomeValues.Deny,
                [PolicyDecisionReasonCodes.MissingPolicyConfiguration],
                "Policy decision payload is missing.",
                string.Empty,
                string.Empty,
                null,
                false,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                PolicyDecisionSchemaVersions.V1);
        }

        var jsonObject = new JsonObject();
        foreach (var (key, value) in payload)
        {
            jsonObject[key] = value?.DeepClone();
        }

        return jsonObject.Deserialize<ToolExecutionDecisionDto>(SerializerOptions)
               ?? throw new InvalidOperationException("Failed to deserialize the structured policy decision payload.");
    }

    private static Dictionary<string, JsonNode?> ToDictionary(JsonObject jsonObject)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in jsonObject)
        {
            payload[property.Key] = property.Value?.DeepClone();
        }

        return payload;
    }
}
