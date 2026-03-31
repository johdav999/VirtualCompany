using System.Text.Json.Nodes;
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
            Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
            Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy"), JsonValue.Create("approval_latency")))),
            Payload(("allowed", new JsonArray(JsonValue.Create("erp"), JsonValue.Create("approvals_console")))),
            Payload(("read", new JsonArray(JsonValue.Create("finance"))), ("write", new JsonArray(JsonValue.Create("approval_notes")))),
            Payload(("approval", new JsonObject { ["expenseUsd"] = 2500, ["maxAmount"] = 5000, ["minAmount"] = 0 })),
            Payload(("critical", new JsonArray(JsonValue.Create("failed_payment"))), ("escalateTo", JsonValue.Create("owner"))),
            Payload(
                ("enabled", JsonValue.Create(true)),
                ("conditions", new JsonArray(new JsonObject
                {
                    ["event"] = "invoice_created",
                    ["source"] = "erp"
                }))),
            Payload(
                ("timezone", JsonValue.Create("UTC")),
                ("windows", new JsonArray(new JsonObject
                {
                    ["day"] = "monday",
                    ["start"] = "08:00",
                    ["end"] = "16:00"
                }))));

        UpdateAgentOperatingProfileCommandValidator.ValidateAndThrow(command);
    }

    [Fact]
    public void ValidateAndThrow_returns_nested_field_errors_for_invalid_payloads()
    {
        var command = new UpdateAgentOperatingProfileCommand(
            "sleeping",
            new string('x', 4001),
            Payload(("primary", new JsonArray(JsonValue.Create(string.Empty)))),
            Payload(("targets", new JsonArray(new JsonObject { ["name"] = string.Empty }, JsonValue.Create("cycle_time"), JsonValue.Create("cycle_time")))),
            Payload(("allowed", new JsonArray(JsonValue.Create("erp"), JsonValue.Create(string.Empty), JsonValue.Create("erp"))), ("denied", new JsonArray(JsonValue.Create("erp")))),
            Payload(("read", new JsonArray(JsonValue.Create("finance"), JsonValue.Create(string.Empty)))),
            Payload(("approval", new JsonObject { ["expenseUsd"] = -1, ["minAmount"] = 50, ["maxAmount"] = 10, ["requiresApproval"] = "yes" })),
            Payload(("critical", new JsonArray(JsonValue.Create("failed_payment"))), ("escalateTo", JsonValue.Create(string.Empty))),
            Payload(("enabled", JsonValue.Create(true)), ("conditions", new JsonArray(new JsonObject { ["source"] = "erp" }))),
            Payload(
                ("timezone", JsonValue.Create(string.Empty)),
                ("windows", new JsonArray(new JsonObject
                {
                    ["day"] = "monday",
                    ["start"] = "18:00",
                    ["end"] = "09:00"
                }))));

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
    }

    [Fact]
    public void ValidateAndThrow_rejects_archived_profiles_with_enabled_trigger_logic()
    {
        var command = new UpdateAgentOperatingProfileCommand(
            "archived",
            "Archived operating profile.",
            Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
            Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
            Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            Payload(("read", new JsonArray(JsonValue.Create("finance")))),
            Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
            Payload(("critical", new JsonArray(JsonValue.Create("cash_runway_under_90_days"))), ("escalateTo", JsonValue.Create("owner"))),
            Payload(
                ("enabled", JsonValue.Create(true)),
                ("conditions", new JsonArray(new JsonObject
                {
                    ["event"] = "invoice_created"
                }))),
            Payload(
                ("timezone", JsonValue.Create("UTC")),
                ("windows", new JsonArray(new JsonObject
                {
                    ["day"] = "monday",
                    ["start"] = "08:00",
                    ["end"] = "16:00"
                }))));

        var exception = Assert.Throws<AgentValidationException>(() => UpdateAgentOperatingProfileCommandValidator.ValidateAndThrow(command));

        Assert.Contains("Status", exception.Errors.Keys);
        Assert.Contains("TriggerLogic.enabled", exception.Errors.Keys);
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
}