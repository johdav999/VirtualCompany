using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Agents;

public static class AgentOperatingProfileJsonMapper
{
    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentObjectivesInput? input) =>
        input is null ? null : ToJsonDictionary(input.Sections);

    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentKpisInput? input) =>
        input is null ? null : ToJsonDictionary(input.Sections);

    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentApprovalThresholdsInput? input) =>
        input is null ? null : ToJsonDictionary(input.Rules);

    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentToolPermissionsInput? input)
    {
        if (input is null)
        {
            return null;
        }

        var payload = CreateNodeMap();
        AddArrayIfPresent(payload, "allowed", input.Allowed);
        AddArrayIfPresent(payload, "denied", input.Denied);
        AddArrayIfPresent(payload, "actions", input.Actions);
        AddArrayIfPresent(payload, "deniedActions", input.DeniedActions);
        AddExtensionData(payload, input.AdditionalProperties, "allowed", "denied", "actions", "deniedActions");
        return payload;
    }

    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentDataScopesInput? input)
    {
        if (input is null)
        {
            return null;
        }

        var payload = CreateNodeMap();
        AddArrayIfPresent(payload, "read", input.Read);
        AddArrayIfPresent(payload, "recommend", input.Recommend);
        AddArrayIfPresent(payload, "execute", input.Execute);
        AddArrayIfPresent(payload, "write", input.Write);
        AddExtensionData(payload, input.AdditionalProperties, "read", "recommend", "execute", "write");
        return payload;
    }

    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentEscalationRulesInput? input)
    {
        if (input is null)
        {
            return null;
        }

        var payload = CreateNodeMap();
        AddArrayIfPresent(payload, "critical", input.Critical);

        if (input.EscalateTo is not null)
        {
            payload["escalateTo"] = JsonValue.Create(input.EscalateTo);
        }

        if (input.NotifyAfterMinutes.HasValue)
        {
            payload["notifyAfterMinutes"] = JsonValue.Create(input.NotifyAfterMinutes.Value);
        }

        if (input.RequireApproval is not null)
        {
            payload["requireApproval"] = BuildApprovalRequirementNode(input.RequireApproval);
        }

        AddExtensionData(payload, input.AdditionalProperties, "critical", "escalateTo", "notifyAfterMinutes", "requireApproval");
        return payload;
    }

    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentTriggerLogicInput? input)
    {
        if (input is null)
        {
            return null;
        }

        var payload = CreateNodeMap();

        if (input.Enabled.HasValue)
        {
            payload["enabled"] = JsonValue.Create(input.Enabled.Value);
        }

        if (input.Conditions.Count > 0)
        {
            payload["conditions"] = JsonSerializer.SerializeToNode(input.Conditions);
        }

        AddExtensionData(payload, input.AdditionalProperties, "enabled", "conditions");
        return payload;
    }

    public static Dictionary<string, JsonNode?>? ToJsonDictionary(AgentWorkingHoursInput? input)
    {
        if (input is null)
        {
            return null;
        }

        var payload = CreateNodeMap();

        if (input.Timezone is not null)
        {
            payload["timezone"] = JsonValue.Create(input.Timezone);
        }

        if (input.Windows.Count > 0)
        {
            payload["windows"] = JsonSerializer.SerializeToNode(input.Windows);
        }

        AddExtensionData(payload, input.AdditionalProperties, "timezone", "windows");
        return payload;
    }

    private static Dictionary<string, JsonNode?>? ToJsonDictionary(Dictionary<string, JsonElement>? source)
    {
        if (source is null)
        {
            return null;
        }

        var payload = CreateNodeMap();
        foreach (var (key, value) in source)
        {
            payload[key] = ToNode(value);
        }

        return payload;
    }

    private static Dictionary<string, JsonNode?> CreateNodeMap() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static JsonObject BuildApprovalRequirementNode(AgentApprovalRequirementInput input)
    {
        var payload = CreateNodeMap();
        AddArrayIfPresent(payload, "actions", input.Actions);
        AddArrayIfPresent(payload, "tools", input.Tools);
        AddArrayIfPresent(payload, "scopes", input.Scopes);
        AddExtensionData(payload, input.AdditionalProperties, "actions", "tools", "scopes");

        var jsonObject = new JsonObject();
        foreach (var (key, value) in payload)
        {
            jsonObject[key] = value;
        }

        return jsonObject;
    }

    private static void AddArrayIfPresent(
        IDictionary<string, JsonNode?> payload,
        string propertyName,
        IReadOnlyCollection<string>? values)
    {
        if (values is null)
        {
            return;
        }

        payload[propertyName] = JsonSerializer.SerializeToNode(values);
    }

    private static void AddExtensionData(
        IDictionary<string, JsonNode?> payload,
        Dictionary<string, JsonElement>? additionalProperties,
        params string[] knownProperties)
    {
        if (additionalProperties is null || additionalProperties.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(knownProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in additionalProperties)
        {
            if (known.Contains(key))
            {
                continue;
            }

            payload[key] = ToNode(value);
        }
    }

    private static JsonNode? ToNode(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(value.GetRawText());
    }
}