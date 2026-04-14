namespace VirtualCompany.Domain.Enums;

public enum ConditionOperandSourceType
{
    Metric = 1,
    EntityField = 2
}

public enum ConditionOperator
{
    GreaterThan = 1,
    LessThan = 2,
    Equals = 3,
    ChangedSinceLastEvaluation = 4
}

public enum ConditionValueType
{
    Number = 1,
    String = 2,
    Boolean = 3,
    DateTime = 4
}

public enum RepeatFiringMode
{
    FalseToTrueTransition = 1,
    EveryEvaluationWhileTrue = 2
}

public static class ConditionTriggerStorageValues
{
    public const string MetricSourceType = "metric";
    public const string EntityFieldSourceType = "entityField";

    public const string GreaterThanOperator = "greaterThan";
    public const string LessThanOperator = "lessThan";
    public const string EqualsOperator = "equals";
    public const string ChangedSinceLastEvaluationOperator = "changedSinceLastEvaluation";

    public const string NumberValueType = "number";
    public const string StringValueType = "string";
    public const string BooleanValueType = "boolean";
    public const string DateTimeValueType = "datetime";

    public const string FalseToTrueTransitionRepeatMode = "falseToTrueTransition";
    public const string EveryEvaluationWhileTrueRepeatMode = "everyEvaluationWhileTrue";

    public static string ToStorageValue(this ConditionOperandSourceType sourceType) =>
        sourceType switch
        {
            ConditionOperandSourceType.Metric => MetricSourceType,
            ConditionOperandSourceType.EntityField => EntityFieldSourceType,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Unsupported condition source type.")
        };

    public static string ToStorageValue(this ConditionOperator conditionOperator) =>
        conditionOperator switch
        {
            ConditionOperator.GreaterThan => GreaterThanOperator,
            ConditionOperator.LessThan => LessThanOperator,
            ConditionOperator.Equals => EqualsOperator,
            ConditionOperator.ChangedSinceLastEvaluation => ChangedSinceLastEvaluationOperator,
            _ => throw new ArgumentOutOfRangeException(nameof(conditionOperator), conditionOperator, "Unsupported condition operator.")
        };

    public static string ToStorageValue(this ConditionValueType valueType) =>
        valueType switch
        {
            ConditionValueType.Number => NumberValueType,
            ConditionValueType.String => StringValueType,
            ConditionValueType.Boolean => BooleanValueType,
            ConditionValueType.DateTime => DateTimeValueType,
            _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Unsupported condition value type.")
        };

    public static string ToStorageValue(this RepeatFiringMode repeatFiringMode) =>
        repeatFiringMode switch
        {
            RepeatFiringMode.FalseToTrueTransition => FalseToTrueTransitionRepeatMode,
            RepeatFiringMode.EveryEvaluationWhileTrue => EveryEvaluationWhileTrueRepeatMode,
            _ => throw new ArgumentOutOfRangeException(nameof(repeatFiringMode), repeatFiringMode, "Unsupported repeat firing mode.")
        };

    public static bool TryParseSourceType(string? value, out ConditionOperandSourceType sourceType)
    {
        sourceType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        sourceType = value.Trim() switch
        {
            var source when string.Equals(source, MetricSourceType, StringComparison.OrdinalIgnoreCase) => ConditionOperandSourceType.Metric,
            var source when string.Equals(source, EntityFieldSourceType, StringComparison.OrdinalIgnoreCase) => ConditionOperandSourceType.EntityField,
            _ => default
        };

        return sourceType is not default(ConditionOperandSourceType);
    }

    public static bool TryParseOperator(string? value, out ConditionOperator conditionOperator)
    {
        conditionOperator = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        conditionOperator = value.Trim() switch
        {
            var op when string.Equals(op, GreaterThanOperator, StringComparison.OrdinalIgnoreCase) => ConditionOperator.GreaterThan,
            var op when string.Equals(op, LessThanOperator, StringComparison.OrdinalIgnoreCase) => ConditionOperator.LessThan,
            var op when string.Equals(op, EqualsOperator, StringComparison.OrdinalIgnoreCase) => ConditionOperator.Equals,
            var op when string.Equals(op, ChangedSinceLastEvaluationOperator, StringComparison.OrdinalIgnoreCase) => ConditionOperator.ChangedSinceLastEvaluation,
            _ => default
        };

        return conditionOperator is not default(ConditionOperator);
    }

    public static bool TryParseValueType(string? value, out ConditionValueType valueType)
    {
        valueType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        valueType = value.Trim() switch
        {
            var type when string.Equals(type, NumberValueType, StringComparison.OrdinalIgnoreCase) => ConditionValueType.Number,
            var type when string.Equals(type, StringValueType, StringComparison.OrdinalIgnoreCase) => ConditionValueType.String,
            var type when string.Equals(type, BooleanValueType, StringComparison.OrdinalIgnoreCase) => ConditionValueType.Boolean,
            var type when string.Equals(type, DateTimeValueType, StringComparison.OrdinalIgnoreCase) => ConditionValueType.DateTime,
            _ => default
        };

        return valueType is not default(ConditionValueType);
    }

    public static bool TryParseRepeatFiringMode(string? value, out RepeatFiringMode repeatFiringMode)
    {
        repeatFiringMode = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        repeatFiringMode = value.Trim() switch
        {
            var mode when string.Equals(mode, FalseToTrueTransitionRepeatMode, StringComparison.OrdinalIgnoreCase) => RepeatFiringMode.FalseToTrueTransition,
            var mode when string.Equals(mode, EveryEvaluationWhileTrueRepeatMode, StringComparison.OrdinalIgnoreCase) => RepeatFiringMode.EveryEvaluationWhileTrue,
            _ => default
        };

        return repeatFiringMode is not default(RepeatFiringMode);
    }

    public static ConditionOperandSourceType ParseSourceType(string value) =>
        TryParseSourceType(value, out var sourceType) ? sourceType : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported condition source type.");

    public static ConditionOperator ParseOperator(string value) =>
        TryParseOperator(value, out var conditionOperator) ? conditionOperator : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported condition operator.");

    public static ConditionValueType ParseValueType(string value) =>
        TryParseValueType(value, out var valueType) ? valueType : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported condition value type.");

    public static RepeatFiringMode ParseRepeatFiringMode(string value) =>
        TryParseRepeatFiringMode(value, out var repeatFiringMode) ? repeatFiringMode : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported repeat firing mode.");
}
