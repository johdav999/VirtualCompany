using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public static class ConditionExpressionCriteriaMapper
{
    public const string CriteriaConditionKey = "condition";

    public static IReadOnlyDictionary<string, string[]> ValidateCriteriaCondition(
        JsonNode? conditionNode,
        string path = "criteriaJson.condition")
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!TryRead(conditionNode, path, errors, out var condition))
        {
            return ToResult(errors);
        }

        foreach (var error in ConditionExpressionValidator.Validate(condition, path))
        {
            foreach (var message in error.Value)
            {
                AddError(errors, error.Key, message);
            }
        }

        return ToResult(errors);
    }

    private static bool TryRead(
        JsonNode? conditionNode,
        string path,
        IDictionary<string, List<string>> errors,
        out ConditionExpression? condition)
    {
        condition = null;

        if (conditionNode is not JsonObject conditionObject)
        {
            AddError(errors, path, "Condition criteria must be an object.");
            return false;
        }

        if (GetProperty(conditionObject, "target") is not JsonObject targetObject)
        {
            AddError(errors, $"{path}.target", "Condition target is required.");
            return false;
        }

        var sourceType = ReadSourceType(GetProperty(targetObject, "sourceType"));
        var conditionOperator = ReadOperator(GetProperty(conditionObject, "operator"));
        var valueType = ReadValueType(GetProperty(conditionObject, "valueType"), $"{path}.valueType", errors);
        var repeatFiringMode = ReadRepeatFiringMode(GetProperty(conditionObject, "repeatFiringMode"));

        condition = new ConditionExpression(
            new ConditionTargetReference(
                sourceType,
                ReadOptionalString(GetProperty(targetObject, "metricName")),
                ReadOptionalString(GetProperty(targetObject, "entityType")),
                ReadOptionalString(GetProperty(targetObject, "fieldPath"))),
            conditionOperator,
            valueType,
            GetProperty(conditionObject, "comparisonValue")?.DeepClone(),
            repeatFiringMode);

        return true;
    }

    private static ConditionOperandSourceType ReadSourceType(JsonNode? node)
    {
        if (!TryGetString(node, out var sourceType))
        {
            return default;
        }

        return ConditionTriggerStorageValues.TryParseSourceType(sourceType, out var parsed)
            ? parsed
            : (ConditionOperandSourceType)99;
    }

    private static ConditionOperator ReadOperator(JsonNode? node)
    {
        if (!TryGetString(node, out var conditionOperator))
        {
            return default;
        }

        return ConditionTriggerStorageValues.TryParseOperator(conditionOperator, out var parsed)
            ? parsed
            : (ConditionOperator)99;
    }

    private static ConditionValueType? ReadValueType(
        JsonNode? node,
        string path,
        IDictionary<string, List<string>> errors)
    {
        if (node is null)
        {
            return null;
        }

        if (!TryGetString(node, out var valueType) ||
            !ConditionTriggerStorageValues.TryParseValueType(valueType, out var parsed))
        {
            AddError(errors, path, "Condition value type is not supported.");
            return null;
        }

        return parsed;
    }

    private static RepeatFiringMode ReadRepeatFiringMode(JsonNode? node)
    {
        if (node is null)
        {
            return RepeatFiringMode.FalseToTrueTransition;
        }

        return TryGetString(node, out var repeatFiringMode) &&
               ConditionTriggerStorageValues.TryParseRepeatFiringMode(repeatFiringMode, out var parsed)
            ? parsed
            : (RepeatFiringMode)99;
    }

    private static string? ReadOptionalString(JsonNode? node) =>
        TryGetString(node, out var value) ? value : null;

    private static bool TryGetString(JsonNode? node, out string value)
    {
        value = string.Empty;

        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static JsonNode? GetProperty(JsonObject jsonObject, string propertyName) =>
        jsonObject.FirstOrDefault(pair => string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase)).Value;

    private static void AddError(
        IDictionary<string, List<string>> errors,
        string key,
        string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static IReadOnlyDictionary<string, string[]> ToResult(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
}