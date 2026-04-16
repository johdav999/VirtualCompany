using System.Globalization;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public sealed record ConditionEvaluationRequest(
    ConditionExpression Condition,
    JsonNode? CurrentValue,
    bool HasPreviousValue,
    JsonNode? PreviousValue,
    bool? PreviousOutcome,
    DateTime EvaluatedUtc);

public sealed record ConditionEvaluationResultDto(
    DateTime EvaluatedUtc,
    Dictionary<string, JsonNode?> InputValues,
    bool Outcome,
    bool? PreviousOutcome,
    bool IsFalseToTrueTransition,
    bool ShouldFire,
    string? Diagnostic);

public sealed record ConditionResolvedValue(bool Found, JsonNode? Value, string? Diagnostic = null)
{
    public static ConditionResolvedValue FoundValue(JsonNode? value) => new(true, value?.DeepClone());

    public static ConditionResolvedValue Missing(string diagnostic) => new(false, null, diagnostic);
}

public sealed record EvaluateConditionTriggerCommand(
    Guid CompanyId,
    string ConditionDefinitionId,
    Guid? WorkflowTriggerId,
    ConditionExpression Condition,
    DateTime EvaluatedUtc,
    string? EntityId = null);

public interface IConditionTriggerEvaluator
{
    ConditionEvaluationResultDto Evaluate(ConditionEvaluationRequest request);
}

public interface IConditionMetricValueResolver
{
    Task<ConditionResolvedValue> ResolveMetricAsync(
        Guid companyId,
        string metricName,
        CancellationToken cancellationToken);
}

public interface IConditionEntityFieldValueResolver
{
    Task<ConditionResolvedValue> ResolveEntityFieldAsync(
        Guid companyId,
        string entityType,
        string fieldPath,
        string? entityId,
        CancellationToken cancellationToken);
}

public interface IConditionTriggerEvaluationRepository
{
    Task<ConditionTriggerEvaluation?> GetLatestAsync(
        Guid companyId,
        string conditionDefinitionId,
        Guid? workflowTriggerId,
        CancellationToken cancellationToken);

    Task AddAsync(ConditionTriggerEvaluation evaluation, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IConditionTriggerEvaluationService
{
    Task<ConditionEvaluationResultDto> EvaluateAndPersistAsync(
        EvaluateConditionTriggerCommand command,
        CancellationToken cancellationToken);
}

public sealed class ConditionTriggerEvaluator : IConditionTriggerEvaluator
{
    public ConditionEvaluationResultDto Evaluate(ConditionEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Condition);

        var condition = request.Condition;
        var (outcome, diagnostic) = EvaluateOutcome(
            condition.Operator,
            condition.ValueType,
            request.CurrentValue,
            request.HasPreviousValue,
            request.PreviousValue,
            condition.ComparisonValue);

        var previousOutcome = request.PreviousOutcome;
        var isFalseToTrueTransition = outcome && previousOutcome != true;
        var snapshot = new ConditionEvaluationSnapshot(
            NormalizeUtc(request.EvaluatedUtc),
            BuildInputValues(condition, request.CurrentValue, request.HasPreviousValue, request.PreviousValue),
            outcome,
            previousOutcome,
            isFalseToTrueTransition);

        return new ConditionEvaluationResultDto(
            snapshot.EvaluatedUtc,
            snapshot.InputValues,
            snapshot.Outcome,
            snapshot.PreviousOutcome,
            snapshot.IsFalseToTrueTransition,
            snapshot.ShouldFire(condition.RepeatFiringMode),
            diagnostic);
    }

    private static (bool Outcome, string? Diagnostic) EvaluateOutcome(
        ConditionOperator conditionOperator,
        ConditionValueType? valueType,
        JsonNode? currentValue,
        bool hasPreviousValue,
        JsonNode? previousValue,
        JsonNode? comparisonValue)
    {
        if (conditionOperator is ConditionOperator.ChangedSinceLastEvaluation)
        {
            if (!hasPreviousValue)
            {
                return (false, "No previous value exists for changed-since-last-evaluation comparison.");
            }

            return (!JsonValuesEqual(currentValue, previousValue), null);
        }

        if (currentValue is null || comparisonValue is null)
        {
            return (false, "Current value and comparison value are required for threshold condition evaluation.");
        }

        if (valueType is null)
        {
            return (false, "Condition value type is required for threshold condition evaluation.");
        }

        return valueType.Value switch
        {
            ConditionValueType.Number => EvaluateNumber(conditionOperator, currentValue, comparisonValue),
            ConditionValueType.String => EvaluateString(conditionOperator, currentValue, comparisonValue),
            ConditionValueType.Boolean => EvaluateBoolean(conditionOperator, currentValue, comparisonValue),
            ConditionValueType.DateTime => EvaluateDateTime(conditionOperator, currentValue, comparisonValue),
            _ => (false, "Condition value type is not supported.")
        };
    }

    private static (bool Outcome, string? Diagnostic) EvaluateNumber(
        ConditionOperator conditionOperator,
        JsonNode currentValue,
        JsonNode comparisonValue)
    {
        if (!TryGetDecimal(currentValue, out var current) || !TryGetDecimal(comparisonValue, out var target))
        {
            return (false, "Condition value type mismatch: expected numeric current and comparison values.");
        }

        return conditionOperator switch
        {
            ConditionOperator.GreaterThan => (current > target, null),
            ConditionOperator.LessThan => (current < target, null),
            ConditionOperator.Equals => (current == target, null),
            _ => (false, "Condition operator is not supported for numeric comparison.")
        };
    }

    private static (bool Outcome, string? Diagnostic) EvaluateString(
        ConditionOperator conditionOperator,
        JsonNode currentValue,
        JsonNode comparisonValue)
    {
        if (!TryGetString(currentValue, out var current) || !TryGetString(comparisonValue, out var target))
        {
            return (false, "Condition value type mismatch: expected string current and comparison values.");
        }

        return conditionOperator is ConditionOperator.Equals
            ? (string.Equals(current, target, StringComparison.Ordinal), null)
            : (false, "Only equals is supported for string comparisons.");
    }

    private static (bool Outcome, string? Diagnostic) EvaluateBoolean(
        ConditionOperator conditionOperator,
        JsonNode currentValue,
        JsonNode comparisonValue)
    {
        if (!TryGetBoolean(currentValue, out var current) || !TryGetBoolean(comparisonValue, out var target))
        {
            return (false, "Condition value type mismatch: expected boolean current and comparison values.");
        }

        return conditionOperator is ConditionOperator.Equals
            ? (current == target, null)
            : (false, "Only equals is supported for boolean comparisons.");
    }

    private static (bool Outcome, string? Diagnostic) EvaluateDateTime(
        ConditionOperator conditionOperator,
        JsonNode currentValue,
        JsonNode comparisonValue)
    {
        if (!TryGetDateTimeOffset(currentValue, out var current) || !TryGetDateTimeOffset(comparisonValue, out var target))
        {
            return (false, "Condition value type mismatch: expected datetime current and comparison values.");
        }

        return conditionOperator switch
        {
            ConditionOperator.GreaterThan => (current > target, null),
            ConditionOperator.LessThan => (current < target, null),
            ConditionOperator.Equals => (current == target, null),
            _ => (false, "Condition operator is not supported for datetime comparison.")
        };
    }

    private static Dictionary<string, JsonNode?> BuildInputValues(
        ConditionExpression condition,
        JsonNode? currentValue,
        bool hasPreviousValue,
        JsonNode? previousValue) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = JsonValue.Create(condition.Target.SourceType.ToStorageValue()),
            ["metricName"] = condition.Target.MetricName is null ? null : JsonValue.Create(condition.Target.MetricName),
            ["entityType"] = condition.Target.EntityType is null ? null : JsonValue.Create(condition.Target.EntityType),
            ["fieldPath"] = condition.Target.FieldPath is null ? null : JsonValue.Create(condition.Target.FieldPath),
            ["operator"] = JsonValue.Create(condition.Operator.ToStorageValue()),
            ["valueType"] = condition.ValueType.HasValue ? JsonValue.Create(condition.ValueType.Value.ToStorageValue()) : null,
            ["comparisonValue"] = condition.ComparisonValue?.DeepClone(),
            ["currentValue"] = currentValue?.DeepClone(),
            ["hasPreviousValue"] = JsonValue.Create(hasPreviousValue),
            ["previousValue"] = previousValue?.DeepClone()
        };

    private static bool JsonValuesEqual(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return JsonNode.DeepEquals(left, right);
    }

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

    private static bool TryGetString(JsonNode? node, out string value)
    {
        value = string.Empty;
        return node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out value!);
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

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<DateTimeOffset>(out value))
        {
            return true;
        }

        return TryGetString(node, out var text) &&
               DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    private static bool Assign(decimal input, out decimal output)
    {
        output = input;
        return true;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class ConditionTriggerEvaluationService : IConditionTriggerEvaluationService
{
    private readonly IConditionTriggerEvaluator _evaluator;
    private readonly IConditionMetricValueResolver _metricValueResolver;
    private readonly IConditionEntityFieldValueResolver _entityFieldValueResolver;
    private readonly IConditionTriggerEvaluationRepository _repository;

    public ConditionTriggerEvaluationService(
        IConditionTriggerEvaluator evaluator,
        IConditionMetricValueResolver metricValueResolver,
        IConditionEntityFieldValueResolver entityFieldValueResolver,
        IConditionTriggerEvaluationRepository repository)
    {
        _evaluator = evaluator;
        _metricValueResolver = metricValueResolver;
        _entityFieldValueResolver = entityFieldValueResolver;
        _repository = repository;
    }

    public async Task<ConditionEvaluationResultDto> EvaluateAndPersistAsync(
        EvaluateConditionTriggerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Condition);
        ConditionExpressionValidator.ValidateAndThrow(command.Condition);

        var currentValue = await ResolveCurrentValueAsync(command, cancellationToken);
        var latest = await _repository.GetLatestAsync(command.CompanyId, command.ConditionDefinitionId, command.WorkflowTriggerId, cancellationToken);
        JsonNode? previousValue = null;
        var hasPreviousValue = latest?.InputValues.TryGetValue("currentValue", out previousValue) == true;

        var result = currentValue.Found
            ? _evaluator.Evaluate(new ConditionEvaluationRequest(
                command.Condition,
                currentValue.Value,
                hasPreviousValue,
                previousValue,
                latest?.CurrentOutcome,
                command.EvaluatedUtc))
            : BuildMissingValueResult(command, latest?.CurrentOutcome, currentValue.Diagnostic);

        var evaluation = new ConditionTriggerEvaluation(
            Guid.NewGuid(),
            command.CompanyId,
            command.ConditionDefinitionId,
            command.WorkflowTriggerId,
            result.EvaluatedUtc,
            command.Condition.Target,
            command.Condition.Operator,
            command.Condition.ValueType,
            command.Condition.RepeatFiringMode,
            result.InputValues,
            result.PreviousOutcome,
            result.Outcome,
            result.ShouldFire,
            result.Diagnostic);

        await _repository.AddAsync(evaluation, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<ConditionResolvedValue> ResolveCurrentValueAsync(
        EvaluateConditionTriggerCommand command,
        CancellationToken cancellationToken)
    {
        var target = command.Condition.Target;
        return target.SourceType switch
        {
            ConditionOperandSourceType.Metric => await _metricValueResolver.ResolveMetricAsync(
                command.CompanyId,
                target.MetricName ?? string.Empty,
                cancellationToken),
            ConditionOperandSourceType.EntityField => await _entityFieldValueResolver.ResolveEntityFieldAsync(
                command.CompanyId,
                target.EntityType ?? string.Empty,
                target.FieldPath ?? string.Empty,
                command.EntityId,
                cancellationToken),
            _ => ConditionResolvedValue.Missing("Condition target source type is not supported.")
        };
    }

    private static ConditionEvaluationResultDto BuildMissingValueResult(
        EvaluateConditionTriggerCommand command,
        bool? previousOutcome,
        string? diagnostic)
    {
        var inputValues = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = JsonValue.Create(command.Condition.Target.SourceType.ToStorageValue()),
            ["metricName"] = command.Condition.Target.MetricName is null ? null : JsonValue.Create(command.Condition.Target.MetricName),
            ["entityType"] = command.Condition.Target.EntityType is null ? null : JsonValue.Create(command.Condition.Target.EntityType),
            ["fieldPath"] = command.Condition.Target.FieldPath is null ? null : JsonValue.Create(command.Condition.Target.FieldPath),
            ["operator"] = JsonValue.Create(command.Condition.Operator.ToStorageValue()),
            ["valueType"] = command.Condition.ValueType.HasValue ? JsonValue.Create(command.Condition.ValueType.Value.ToStorageValue()) : null,
            ["comparisonValue"] = command.Condition.ComparisonValue?.DeepClone(),
            ["currentValue"] = null,
            ["hasPreviousValue"] = JsonValue.Create(false),
            ["previousValue"] = null
        };

        return new ConditionEvaluationResultDto(
            command.EvaluatedUtc.Kind == DateTimeKind.Utc ? command.EvaluatedUtc : command.EvaluatedUtc.ToUniversalTime(),
            inputValues,
            false,
            previousOutcome,
            false,
            false,
            diagnostic ?? "Condition value could not be resolved.");
    }
}
