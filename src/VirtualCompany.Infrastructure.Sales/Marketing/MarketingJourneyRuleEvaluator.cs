using System.Text.Json;
using VirtualCompany.Application.Marketing;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingJourneyRuleEvaluator : IMarketingJourneyRuleEvaluator
{
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    { "status", "has_email", "has_customer_company", "preferred_language", "created_days_ago", "event_type", "segment_version_id" };
    private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
    { "equals", "not_equals", "in", "exists", "greater_than_or_equal", "less_than_or_equal" };
    private static readonly HashSet<string> RuleGroups = new(StringComparer.OrdinalIgnoreCase)
    { "all", "any", "entry", "exit", "goal" };

    public MarketingJourneyValidationDto Validate(string audienceEligibilityJson, string entryExitCriteriaJson, string guardrailsJson, int stepCount)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        ValidateDocument(audienceEligibilityJson, "audience", errors);
        ValidateDocument(entryExitCriteriaJson, "transition", errors);
        try { using var _ = JsonDocument.Parse(guardrailsJson); } catch (JsonException) { errors.Add("Journey guardrails contain invalid JSON."); }
        if (stepCount == 0) errors.Add("At least one journey step is required.");
        warnings.Add("Eligibility, entry, exit, goal, consent, suppression, frequency, approval, and target version are checked deterministically.");
        return new(errors.Count == 0, errors.Distinct().ToArray(), warnings, stepCount);
    }

    public MarketingJourneyRuleDecision Evaluate(string audienceEligibilityJson, string entryExitCriteriaJson,
        MarketingJourneyContactFacts facts, string phase)
    {
        var reasons = new List<string>();
        using var audience = JsonDocument.Parse(audienceEligibilityJson);
        using var transitions = JsonDocument.Parse(entryExitCriteriaJson);
        var audienceAllowed = EvaluateGroup(audience.RootElement, facts, reasons);
        var entryAllowed = phase == "enrollment" ? EvaluateNamed(transitions.RootElement, "entry", facts, reasons, defaultWhenMissing: true) : true;
        var shouldExit = EvaluateNamed(transitions.RootElement, "exit", facts, reasons, defaultWhenMissing: false);
        var goal = EvaluateNamed(transitions.RootElement, "goal", facts, reasons, defaultWhenMissing: false);
        var allowed = audienceAllowed && entryAllowed && !shouldExit && !goal;
        if (!audienceAllowed) reasons.Add("audience_ineligible"); if (!entryAllowed) reasons.Add("entry_criteria_not_met");
        if (shouldExit) reasons.Add("exit_criteria_met"); if (goal) reasons.Add("journey_goal_reached");
        return new(allowed, shouldExit, goal, reasons.Distinct().ToArray(), JsonSerializer.Serialize(new
        { phase, facts.ContactId, audienceAllowed, entryAllowed, shouldExit, goal, reasons }));
    }

    private static void ValidateDocument(string json, string label, List<string> errors)
    {
        try { using var document = JsonDocument.Parse(json); ValidateNode(document.RootElement, errors); }
        catch (JsonException) { errors.Add($"Journey {label} rules contain invalid JSON."); }
    }
    private static void ValidateNode(JsonElement node, List<string> errors)
    {
        if (node.ValueKind == JsonValueKind.Array) { foreach (var item in node.EnumerateArray()) ValidateNode(item, errors); return; }
        if (node.ValueKind != JsonValueKind.Object) { errors.Add("Journey rules must use allowlisted JSON objects and arrays."); return; }
        foreach (var property in node.EnumerateObject())
        {
            if (RuleGroups.Contains(property.Name)) { ValidateNode(property.Value, errors); continue; }
            if (property.Name == "field") { if (!Fields.Contains(property.Value.GetString() ?? "")) errors.Add("Journey rule uses a field that is not allowed."); continue; }
            if (property.Name == "operator") { if (!Operators.Contains(property.Value.GetString() ?? "")) errors.Add("Journey rule uses an operator that is not allowed."); continue; }
            if (property.Name is not ("value" or "values" or "version" or "consentType")) errors.Add("Journey rule contains an unsupported property.");
        }
    }
    private static bool EvaluateNamed(JsonElement root, string name, MarketingJourneyContactFacts facts, List<string> reasons, bool defaultWhenMissing)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var group)) return defaultWhenMissing;
        return EvaluateGroup(group, facts, reasons);
    }
    private static bool EvaluateGroup(JsonElement node, MarketingJourneyContactFacts facts, List<string> reasons)
    {
        if (node.ValueKind == JsonValueKind.Array) return node.EnumerateArray().All(x => EvaluateGroup(x, facts, reasons));
        if (node.ValueKind != JsonValueKind.Object) return false;
        if (node.TryGetProperty("all", out var all)) return all.EnumerateArray().All(x => EvaluateGroup(x, facts, reasons));
        if (node.TryGetProperty("any", out var any)) return any.EnumerateArray().Any(x => EvaluateGroup(x, facts, reasons));
        if (!node.TryGetProperty("field", out var fieldNode) || !node.TryGetProperty("operator", out var operatorNode)) return true;
        var field = fieldNode.GetString() ?? ""; var op = operatorNode.GetString() ?? "";
        if (!Fields.Contains(field) || !Operators.Contains(op)) return false;
        var values = node.TryGetProperty("values", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(x => x.ToString()).ToArray()
            : node.TryGetProperty("value", out var value) ? [value.ToString()] : [];
        object? actual = field switch
        {
            "status" => facts.Status, "has_email" => facts.HasEmail, "has_customer_company" => facts.HasCustomerCompany,
            "preferred_language" => facts.PreferredLanguage, "created_days_ago" => (DateTime.UtcNow - facts.CreatedUtc).TotalDays,
            "event_type" => facts.EventTypes, "segment_version_id" => facts.StrategicSegmentVersionIds, _ => null
        };
        var result = op switch
        {
            "exists" => actual is not null && actual switch { string s => !string.IsNullOrWhiteSpace(s), bool b => b, _ => true },
            "equals" => Equal(actual, values.FirstOrDefault()), "not_equals" => !Equal(actual, values.FirstOrDefault()),
            "in" => values.Any(x => Equal(actual, x)),
            "greater_than_or_equal" => decimal.TryParse(values.FirstOrDefault(), out var min) && Convert.ToDecimal(actual) >= min,
            "less_than_or_equal" => decimal.TryParse(values.FirstOrDefault(), out var max) && Convert.ToDecimal(actual) <= max,
            _ => false
        };
        if (!result) reasons.Add($"rule_failed:{field}:{op}"); return result;
    }
    private static bool Equal(object? actual, string? expected) => actual switch
    {
        IReadOnlySet<string> strings => expected is not null && strings.Contains(expected),
        IReadOnlySet<Guid> ids => Guid.TryParse(expected, out var id) && ids.Contains(id),
        bool value => bool.TryParse(expected, out var parsed) && value == parsed,
        _ => string.Equals(Convert.ToString(actual), expected, StringComparison.OrdinalIgnoreCase)
    };
}
