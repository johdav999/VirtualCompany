using System.Globalization;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public static class ConditionExpressionValidator
{
    private const int MaxIdentifierLength = 100;

    public static IReadOnlyDictionary<string, string[]> Validate(
        ConditionExpression? condition,
        string path = "condition")
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (condition is null)
        {
            AddError(errors, path, "Condition expression is required.");
            return ToResult(errors);
        }

        ValidateTarget(errors, $"{path}.target", condition.Target);
        ValidateOperator(errors, $"{path}.operator", condition.Operator);
        ValidateRepeatFiringMode(errors, $"{path}.repeatFiringMode", condition.RepeatFiringMode);

        var hasSupportedOperator = IsSupportedOperator(condition.Operator);
        var hasComparisonValue = condition.ComparisonValue is not null;
        var isThresholdOperator = condition.Operator is ConditionOperator.GreaterThan
            or ConditionOperator.LessThan
            or ConditionOperator.Equals;

        if (!hasSupportedOperator)
        {
            return ToResult(errors);
        }

        if (isThresholdOperator)
        {
            if (condition.ValueType is null)
            {
                AddError(errors, $"{path}.valueType", "Condition value type is required for threshold condition operators.");
            }
            else if (!IsSupportedValueType(condition.ValueType.Value))
            {
                AddError(errors, $"{path}.valueType", "Condition value type is not supported.");
            }

            if (!hasComparisonValue)
            {
                AddError(errors, $"{path}.comparisonValue", "Comparison value is required for threshold condition operators.");
            }
        }

        // Ordering comparisons are limited to scalar values with stable ordering semantics.
        if (condition.Operator is ConditionOperator.GreaterThan or ConditionOperator.LessThan &&
            condition.ValueType is not null &&
            condition.ValueType.Value is not (ConditionValueType.Number or ConditionValueType.DateTime))
        {
            AddError(errors, $"{path}.valueType", "Greater-than and less-than conditions require number or datetime value type.");
        }

        if (condition.Operator is ConditionOperator.ChangedSinceLastEvaluation)
        {
            if (hasComparisonValue)
            {
                AddError(errors, $"{path}.comparisonValue", "Comparison value must be omitted for changed-since-last-evaluation conditions.");
            }

            if (condition.ValueType is not null && !IsSupportedValueType(condition.ValueType.Value))
            {
                AddError(errors, $"{path}.valueType", "Condition value type is not supported.");
            }
        }

        if (hasComparisonValue && condition.ValueType is not null && IsSupportedValueType(condition.ValueType.Value))
        {
            ValidateComparisonValue(errors, $"{path}.comparisonValue", condition.ComparisonValue, condition.ValueType.Value);
        }

        return ToResult(errors);
    }

    public static void ValidateAndThrow(
        ConditionExpression? condition,
        string path = "condition")
    {
        var errors = Validate(condition, path);
        if (errors.Count is 0)
        {
            return;
        }

        throw new AgentValidationException(errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase));
    }

    private static void ValidateTarget(
        IDictionary<string, List<string>> errors,
        string path,
        ConditionTargetReference? target)
    {
        if (target is null)
        {
            AddError(errors, path, "Condition target is required.");
            return;
        }

        if (!IsSupportedSourceType(target.SourceType))
        {
            AddError(errors, $"{path}.sourceType", "Condition target source type is required.");
            return;
        }

        var hasMetricName = !string.IsNullOrWhiteSpace(target.MetricName);
        var hasEntityType = !string.IsNullOrWhiteSpace(target.EntityType);
        var hasFieldPath = !string.IsNullOrWhiteSpace(target.FieldPath);

        if (target.SourceType is ConditionOperandSourceType.Metric)
        {
            if (!hasMetricName)
            {
                AddError(errors, $"{path}.metricName", "Metric name is required for metric condition targets.");
            }
            else
            {
                ValidateIdentifier(errors, $"{path}.metricName", target.MetricName!, "Metric name");
            }

            if (hasEntityType)
            {
                AddError(errors, $"{path}.entityType", "Entity type must be omitted for metric condition targets.");
            }

            if (hasFieldPath)
            {
                AddError(errors, $"{path}.fieldPath", "Field path must be omitted for metric condition targets.");
            }

            return;
        }

        if (!hasEntityType)
        {
            AddError(errors, $"{path}.entityType", "Entity type is required for entity field condition targets.");
        }
        else
        {
            ValidateIdentifier(errors, $"{path}.entityType", target.EntityType!, "Entity type");
        }

        if (!hasFieldPath)
        {
            AddError(errors, $"{path}.fieldPath", "Field path is required for entity field condition targets.");
        }
        else
        {
            ValidateIdentifier(errors, $"{path}.fieldPath", target.FieldPath!, "Field path");
        }

        if (hasMetricName)
        {
            AddError(errors, $"{path}.metricName", "Metric name must be omitted for entity field condition targets.");
        }
    }

    private static void ValidateOperator(
        IDictionary<string, List<string>> errors,
        string path,
        ConditionOperator conditionOperator)
    {
        if (conditionOperator is default(ConditionOperator))
        {
            AddError(errors, path, "Condition operator is required.");
            return;
        }

        if (!IsSupportedOperator(conditionOperator))
        {
            AddError(errors, path, "Condition operator is not supported.");
        }
    }

    private static void ValidateRepeatFiringMode(
        IDictionary<string, List<string>> errors,
        string path,
        RepeatFiringMode repeatFiringMode)
    {
        if (repeatFiringMode is default(RepeatFiringMode))
        {
            AddError(errors, path, "Repeat firing mode is required.");
            return;
        }

        if (!Enum.IsDefined(repeatFiringMode))
        {
            AddError(errors, path, "Repeat firing mode is not supported.");
        }
    }

    private static void ValidateComparisonValue(
        IDictionary<string, List<string>> errors,
        string path,
        JsonNode? comparisonValue,
        ConditionValueType valueType)
    {
        var isValid = valueType switch
        {
            ConditionValueType.Number => TryGetDecimal(comparisonValue, out _),
            ConditionValueType.String => TryGetNonEmptyString(comparisonValue, out _),
            ConditionValueType.Boolean => TryGetBoolean(comparisonValue, out _),
            ConditionValueType.DateTime => TryGetDateTimeOffset(comparisonValue, out _),
            _ => false
        };

        if (!isValid)
        {
            AddError(errors, path, valueType switch
            {
                ConditionValueType.Number => "Comparison value must be numeric.",
                ConditionValueType.String => "Comparison value must be a non-empty string.",
                ConditionValueType.Boolean => "Comparison value must be boolean.",
                ConditionValueType.DateTime => "Comparison value must be a valid datetime.",
                _ => "Comparison value type is not supported."
            });
        }
    }

    private static void ValidateIdentifier(
        IDictionary<string, List<string>> errors,
        string path,
        string value,
        string label)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > MaxIdentifierLength || !IsValidIdentifier(trimmed))
        {
            AddError(errors, path, $"{label} must be a valid identifier.");
        }
    }

    private static bool IsSupportedSourceType(ConditionOperandSourceType sourceType) =>
        sourceType is ConditionOperandSourceType.Metric or ConditionOperandSourceType.EntityField;

    private static bool IsSupportedOperator(ConditionOperator conditionOperator) =>
        conditionOperator is ConditionOperator.GreaterThan
            or ConditionOperator.LessThan
            or ConditionOperator.Equals
            or ConditionOperator.ChangedSinceLastEvaluation;

    private static bool IsSupportedValueType(ConditionValueType valueType) =>
        valueType is ConditionValueType.Number
            or ConditionValueType.String
            or ConditionValueType.Boolean
            or ConditionValueType.DateTime;

    private static bool TryGetDecimal(JsonNode? node, out decimal value)
    {
        value = 0;

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        return jsonValue.TryGetValue<decimal>(out value) ||
               jsonValue.TryGetValue<int>(out var intValue) && Assign(intValue, out value) ||
               jsonValue.TryGetValue<long>(out var longValue) && Assign(longValue, out value) ||
               jsonValue.TryGetValue<double>(out var doubleValue) && Assign((decimal)doubleValue, out value) ||
               jsonValue.TryGetValue<string>(out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetNonEmptyString(JsonNode? node, out string value)
    {
        value = string.Empty;
        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }

    private static bool TryGetBoolean(JsonNode? node, out bool value)
    {
        value = false;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        return jsonValue.TryGetValue<bool>(out value) ||
               jsonValue.TryGetValue<string>(out var text) && bool.TryParse(text, out value);
    }

    private static bool TryGetDateTimeOffset(JsonNode? node, out DateTimeOffset value)
    {
        value = default;
        return node is JsonValue jsonValue && jsonValue.TryGetValue<DateTimeOffset>(out value) ||
               TryGetNonEmptyString(node, out var text) &&
               DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    private static bool Assign(decimal input, out decimal output)
    {
        output = input;
        return true;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!char.IsLetterOrDigit(trimmed[0]))
        {
            return false;
        }

        return trimmed.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':' or '/');
    }

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
