using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Workflows;

public sealed record PredefinedWorkflowCatalogEntry(
    string Code,
    string Name,
    string Description,
    string? Department,
    int Version,
    string TriggerType,
    IReadOnlyList<string> SupportedStepHandlers,
    Dictionary<string, JsonNode?> DefinitionJson);

public static class PredefinedWorkflowCatalog
{
    public const int CurrentSchemaVersion = 1;

    private static readonly IReadOnlyList<PredefinedWorkflowCatalogEntry> Entries =
    [
        Create(
            "DAILY-EXECUTIVE-BRIEFING",
            "Daily executive briefing",
            "Prepare a concise cross-functional briefing from active company signals.",
            "Executive",
            WorkflowTriggerType.Schedule,
            ["collect_signals", "summarize_briefing", "publish_briefing"],
            [
                Step("collect-signals", "Collect company signals", "collect_signals"),
                Step("summarize-briefing", "Summarize briefing", "summarize_briefing"),
                Step("publish-briefing", "Publish briefing", "publish_briefing")
            ],
            json =>
            {
                json["schedule"] = new JsonObject
                {
                    ["scheduleKey"] = "daily-executive-briefing",
                    ["timezone"] = "company-default"
                };
            }),
        Create(
            "INVOICE-APPROVAL-REVIEW",
            "Invoice approval review",
            "Route a new invoice signal into a review-ready approval workflow.",
            "Finance",
            WorkflowTriggerType.Event,
            ["capture_invoice", "prepare_review", "request_approval"],
            [
                Step("capture-invoice", "Capture invoice context", "capture_invoice"),
                Step("prepare-review", "Prepare review packet", "prepare_review"),
                Step("request-approval", "Request approval", "request_approval")
            ],
            json =>
            {
                json["event"] = new JsonObject
                {
                    ["eventName"] = "invoice.received"
                };
            }),
        Create(
            "SUPPORT-ESCALATION-TRIAGE",
            "Support escalation triage",
            "Triage an escalated support case and prepare the next owner handoff.",
            "Support",
            WorkflowTriggerType.Event,
            ["capture_case", "classify_escalation", "assign_owner"],
            [
                Step("capture-case", "Capture case context", "capture_case"),
                Step("classify-escalation", "Classify escalation", "classify_escalation"),
                Step("assign-owner", "Assign owner", "assign_owner")
            ],
            json =>
            {
                json["event"] = new JsonObject
                {
                    ["eventName"] = "support.case.escalated"
                };
            }),
        Create(
            "LEAD-FOLLOW-UP",
            "Lead follow-up",
            "Start a focused lead follow-up sequence for sales teams.",
            "Sales",
            WorkflowTriggerType.Manual,
            ["qualify_lead", "draft_follow_up", "schedule_next_action"],
            [
                Step("qualify-lead", "Qualify lead", "qualify_lead"),
                Step("draft-follow-up", "Draft follow-up", "draft_follow_up"),
                Step("schedule-next-action", "Schedule next action", "schedule_next_action")
            ])
    ];

    public static IReadOnlyList<PredefinedWorkflowCatalogEntry> All => Entries;

    public static bool TryGet(string code, out PredefinedWorkflowCatalogEntry entry)
    {
        entry = Entries.FirstOrDefault(x => string.Equals(x.Code, NormalizeCode(code), StringComparison.OrdinalIgnoreCase))!;
        return entry is not null;
    }

    public static Dictionary<string, JsonNode?> CloneDefinitionJson(string code) =>
        TryGet(code, out var entry)
            ? CloneNodes(entry.DefinitionJson)
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Workflow code is not in the predefined catalog.");

    public static void ValidateDefinition(
        IDictionary<string, List<string>> errors,
        string codeKey,
        string code,
        string triggerTypeKey,
        string? triggerType,
        string definitionJsonKey,
        Dictionary<string, JsonNode?>? definitionJson)
    {
        if (!TryGet(code, out var catalogEntry))
        {
            AddError(errors, codeKey, "Workflow definitions must use one of the predefined catalog codes.");
            return;
        }

        if (!WorkflowTriggerTypeValues.TryParse(triggerType, out var parsedTriggerType))
        {
            return;
        }

        if (!string.Equals(catalogEntry.TriggerType, parsedTriggerType.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, triggerTypeKey, $"Workflow '{catalogEntry.Code}' only supports '{catalogEntry.TriggerType}' triggers.");
        }

        if (definitionJson is null || definitionJson.Count == 0)
        {
            return;
        }

        if (!TryGetString(definitionJson, "templateCode", out var templateCode) ||
            !string.Equals(templateCode, catalogEntry.Code, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, definitionJsonKey, "DefinitionJson must include the matching predefined templateCode.");
        }

        if (!TryGetInt(definitionJson, "schemaVersion", out var schemaVersion) ||
            schemaVersion != CurrentSchemaVersion)
        {
            AddError(errors, definitionJsonKey, $"DefinitionJson schemaVersion must be {CurrentSchemaVersion}.");
        }

        if (!definitionJson.TryGetValue("steps", out var stepsNode) || stepsNode is not JsonArray steps || steps.Count == 0)
        {
            AddError(errors, definitionJsonKey, "DefinitionJson must include at least one predefined step.");
            return;
        }

        var allowedHandlers = catalogEntry.SupportedStepHandlers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps.OfType<JsonObject>())
        {
            if (!TryGetString(step, "handler", out var handler) || !allowedHandlers.Contains(handler))
            {
                AddError(errors, definitionJsonKey, $"DefinitionJson includes a step handler that is not supported by '{catalogEntry.Code}'.");
            }
        }
    }

    private static PredefinedWorkflowCatalogEntry Create(
        string code,
        string name,
        string description,
        string? department,
        WorkflowTriggerType triggerType,
        IReadOnlyList<string> handlers,
        IReadOnlyList<JsonObject> steps,
        Action<JsonObject>? configure = null)
    {
        var definitionJson = new JsonObject
        {
            ["schema"] = "predefined-workflow-v1",
            ["schemaVersion"] = CurrentSchemaVersion,
            ["templateCode"] = code,
            ["description"] = description,
            ["steps"] = new JsonArray(steps.Select(step => step.DeepClone()).ToArray())
        };
        configure?.Invoke(definitionJson);

        return new PredefinedWorkflowCatalogEntry(
            code,
            name,
            description,
            department,
            CurrentSchemaVersion,
            triggerType.ToStorageValue(),
            handlers,
            definitionJson.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase));
    }

    private static JsonObject Step(string id, string name, string handler) =>
        new()
        {
            ["id"] = id,
            ["name"] = name,
            ["handler"] = handler
        };

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> nodes) =>
        nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static bool TryGetString(IReadOnlyDictionary<string, JsonNode?> nodes, string key, out string value)
    {
        value = string.Empty;
        return nodes.TryGetValue(key, out var node) && node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed) &&
            (value = parsed.Trim()).Length > 0;
    }

    private static bool TryGetString(JsonObject node, string key, out string value)
    {
        value = string.Empty;
        return node.TryGetPropertyValue(key, out var child) && child is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed) &&
            (value = parsed.Trim()).Length > 0;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, JsonNode?> nodes, string key, out int value)
    {
        value = default;
        return nodes.TryGetValue(key, out var node) && node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out value);
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}