using System.Text.Json.Nodes;
using System.Text.Json;
using VirtualCompany.Application.Agents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class UpdateAgentOperatingProfileCommandValidatorTests
{
    [Fact]
    public void ValidateAndThrow_allows_valid_operating_profile_payloads()
    {
        var command = new UpdateAgentOperatingProfileCommand(
            "restricted",
            "Finance gatekeeper for exception approvals.",
            Objectives(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
            Kpis(("targets", new JsonArray(JsonValue.Create("forecast_accuracy"), JsonValue.Create("approval_latency")))),
            ToolPermissions(("allowed", new JsonArray(JsonValue.Create("erp"), JsonValue.Create("approvals_console")))),
            DataScopes(("read", new JsonArray(JsonValue.Create("finance"))), ("write", new JsonArray(JsonValue.Create("approval_notes")))),
            Thresholds(("approval", new JsonObject { ["expenseUsd"] = 2500, ["maxAmount"] = 5000, ["minAmount"] = 0 })),
            Escalation(("critical", new JsonArray(JsonValue.Create("failed_payment"))), ("escalateTo", JsonValue.Create("owner"))),
            TriggerLogic(
                true,
                new JsonObject
                {
                    ["event"] = "invoice_created",
                    ["source"] = "erp"
                }),
            WorkingHours(
                "UTC",
                new JsonObject
                {
                    ["day"] = "monday",
                    ["start"] = "08:00",
                    ["end"] = "16:00"
                }),
            "level_2");

        UpdateAgentOperatingProfileCommandValidator.ValidateAndThrow(command);
    }

    [Fact]
    public void ValidateAndThrow_returns_nested_field_errors_for_invalid_payloads()
    {
        var command = new UpdateAgentOperatingProfileCommand(
            "sleeping",
            new string('x', 4001),
            Objectives(("primary", new JsonArray(JsonValue.Create(string.Empty)))),
            Kpis(("targets", new JsonArray(new JsonObject { ["name"] = string.Empty }, JsonValue.Create("cycle_time"), JsonValue.Create("cycle_time")))),
            ToolPermissions(("allowed", new JsonArray(JsonValue.Create("erp"), JsonValue.Create(string.Empty), JsonValue.Create("erp"))), ("denied", new JsonArray(JsonValue.Create("erp")))),
            DataScopes(("read", new JsonArray(JsonValue.Create("finance"), JsonValue.Create(string.Empty)))),
            Thresholds(("approval", new JsonObject { ["expenseUsd"] = -1, ["minAmount"] = 50, ["maxAmount"] = 10, ["requiresApproval"] = "yes" })),
            Escalation(("critical", new JsonArray(JsonValue.Create("failed_payment"))), ("escalateTo", JsonValue.Create(string.Empty))),
            TriggerLogic(
                true,
                new JsonObject { ["source"] = "erp" }),
            WorkingHours(
                string.Empty,
                new JsonObject
                {
                    ["day"] = "monday",
                    ["start"] = "18:00",
                    ["end"] = "09:00"
                }),
            "level_9");

        var exception = Assert.Throws<AgentValidationException>(() => UpdateAgentOperatingProfileCommandValidator.ValidateAndThrow(command));

        Assert.Contains("Status", exception.Errors.Keys);
        Assert.Contains("RoleBrief", exception.Errors.Keys);
        Assert.Contains("Objectives.primary[0]", exception.Errors.Keys);
        Assert.Contains("Kpis.targets[0].name", exception.Errors.Keys);
        Assert.Contains("Kpis.targets[2]", exception.Errors.Keys);
        Assert.Contains("ToolPermissions.allowed[1]", exception.Errors.Keys);
        Assert.Contains("ToolPermissions.denied", exception.Errors.Keys);
        Assert.Contains("DataScopes.read[1]", exception.Errors.Keys);
        Assert.Contains("ApprovalThresholds.approval.expenseUsd", exception.Errors.Keys);
        Assert.Contains("ApprovalThresholds.approval.maxAmount", exception.Errors.Keys);
        Assert.Contains("ApprovalThresholds.approval.requiresApproval", exception.Errors.Keys);
        Assert.Contains("EscalationRules.escalateTo", exception.Errors.Keys);
        Assert.Contains("TriggerLogic.conditions[0].event", exception.Errors.Keys);
        Assert.Contains("WorkingHours.timezone", exception.Errors.Keys);
        Assert.Contains("WorkingHours.windows[0].end", exception.Errors.Keys);
        Assert.Contains("AutonomyLevel", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateAndThrow_allows_archived_profiles_when_other_fields_are_valid()
    {
        var command = new UpdateAgentOperatingProfileCommand(
            "archived",
            "Archived operating profile.",
            Objectives(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
            Kpis(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
            ToolPermissions(("allowed", new JsonArray(JsonValue.Create("erp")))),
            DataScopes(("read", new JsonArray(JsonValue.Create("finance")))),
            Thresholds(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            Escalation(("critical", new JsonArray(JsonValue.Create("cash_runway_under_90_days"))), ("escalateTo", JsonValue.Create("owner"))),
            TriggerLogic(
                true,
                new JsonObject
                {
                    ["event"] = "invoice_created"
                }),
            WorkingHours(
                "UTC",
                new JsonObject
                {
                    ["day"] = "monday",
                    ["start"] = "08:00",
                    ["end"] = "16:00"
                }),
            "level_1");

        UpdateAgentOperatingProfileCommandValidator.ValidateAndThrow(command);
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private static AgentObjectivesInput Objectives(params (string Key, JsonNode? Value)[] properties) =>
        new()
        {
            Sections = ToJsonElements(properties)
        };

    private static AgentKpisInput Kpis(params (string Key, JsonNode? Value)[] properties) =>
        new()
        {
            Sections = ToJsonElements(properties)
        };

    private static AgentToolPermissionsInput ToolPermissions(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = Payload(properties);
        return new AgentToolPermissionsInput
        {
            Allowed = ExtractStringList(payload, "allowed"),
            Denied = ExtractStringList(payload, "denied"),
            AdditionalProperties = ToJsonElements(payload.Where(x => !string.Equals(x.Key, "allowed", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Key, "denied", StringComparison.OrdinalIgnoreCase)).Select(x => (x.Key, x.Value)).ToArray())
        };
    }

    private static AgentDataScopesInput DataScopes(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = Payload(properties);
        return new AgentDataScopesInput
        {
            Read = ExtractStringList(payload, "read"),
            Write = ExtractStringList(payload, "write"),
            AdditionalProperties = ToJsonElements(payload.Where(x => !string.Equals(x.Key, "read", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Key, "write", StringComparison.OrdinalIgnoreCase)).Select(x => (x.Key, x.Value)).ToArray())
        };
    }

    private static AgentApprovalThresholdsInput Thresholds(params (string Key, JsonNode? Value)[] properties) =>
        new()
        {
            Rules = ToJsonElements(properties)
        };

    private static AgentEscalationRulesInput Escalation(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = Payload(properties);
        return new AgentEscalationRulesInput
        {
            Critical = ExtractStringList(payload, "critical"),
            EscalateTo = payload.TryGetValue("escalateTo", out var escalateToNode) ? escalateToNode?.GetValue<string>() : null
        };
    }

    private static AgentTriggerLogicInput TriggerLogic(bool? enabled, params JsonObject[] conditions) =>
        new()
        {
            Enabled = enabled,
            Conditions = conditions.Select(condition => new AgentTriggerConditionInput
            {
                Event = condition.TryGetPropertyValue("event", out var eventNode) ? eventNode?.GetValue<string>() : null,
                Type = condition.TryGetPropertyValue("type", out var typeNode) ? typeNode?.GetValue<string>() : null,
                Source = condition.TryGetPropertyValue("source", out var sourceNode) ? sourceNode?.GetValue<string>() : null
            }).ToList()
        };

    private static Dictionary<string, JsonNode?> WorkingHours(string? timezone, params JsonObject[] windows) =>
        Payload(
            ("timezone", timezone is null ? null : JsonValue.Create(timezone)),
            ("windows", new JsonArray(windows.Select(window => window.DeepClone()).ToArray())));

    private static Dictionary<string, JsonElement> ToJsonElements(params (string Key, JsonNode? Value)[] properties) =>
        properties.ToDictionary(pair => pair.Key, pair => JsonDocument.Parse(pair.Value?.ToJsonString() ?? "null").RootElement.Clone(), StringComparer.OrdinalIgnoreCase);

    private static List<string> ExtractStringList(IReadOnlyDictionary<string, JsonNode?> payload, string key) =>
        payload.TryGetValue(key, out var node) && node is JsonArray array
            ? array.Select(item => item?.GetValue<string>() ?? string.Empty).ToList()
            : [];
}
