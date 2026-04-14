using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed record ConditionExpression(
    ConditionTargetReference Target,
    ConditionOperator Operator,
    ConditionValueType? ValueType,
    JsonNode? ComparisonValue,
    RepeatFiringMode RepeatFiringMode = RepeatFiringMode.FalseToTrueTransition);

public sealed record ConditionTargetReference(
    ConditionOperandSourceType SourceType,
    string? MetricName,
    string? EntityType,
    string? FieldPath);

public sealed record ConditionEvaluationSnapshot(
    DateTime EvaluatedUtc,
    Dictionary<string, JsonNode?> InputValues,
    bool Outcome,
    bool? PreviousOutcome,
    bool IsFalseToTrueTransition)
{
    public bool ShouldFire(RepeatFiringMode repeatFiringMode) =>
        repeatFiringMode is RepeatFiringMode.EveryEvaluationWhileTrue
            ? Outcome
            : IsFalseToTrueTransition;
}

public sealed class ConditionTriggerEvaluation : ICompanyOwnedEntity
{
    private const int ConditionDefinitionIdMaxLength = 200;
    private const int SourceNameMaxLength = 200;
    private const int EntityTypeMaxLength = 100;
    private const int FieldPathMaxLength = 200;
    private const int DiagnosticMaxLength = 2000;

    private ConditionTriggerEvaluation()
    {
        InputValues = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
    }

    public ConditionTriggerEvaluation(
        Guid id,
        Guid companyId,
        string conditionDefinitionId,
        Guid? workflowTriggerId,
        DateTime evaluatedUtc,
        ConditionTargetReference target,
        ConditionOperator conditionOperator,
        ConditionValueType? valueType,
        RepeatFiringMode repeatFiringMode,
        IDictionary<string, JsonNode?> inputValues,
        bool? previousOutcome,
        bool currentOutcome,
        bool fired,
        string? diagnostic = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (workflowTriggerId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowTriggerId cannot be empty.", nameof(workflowTriggerId));
        }

        ArgumentNullException.ThrowIfNull(target);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ConditionDefinitionId = NormalizeRequired(conditionDefinitionId, nameof(conditionDefinitionId), ConditionDefinitionIdMaxLength);
        WorkflowTriggerId = workflowTriggerId;
        EvaluatedUtc = NormalizeUtc(evaluatedUtc, nameof(evaluatedUtc));
        SourceType = target.SourceType;
        SourceName = NormalizeOptional(target.MetricName, nameof(target.MetricName), SourceNameMaxLength);
        EntityType = NormalizeOptional(target.EntityType, nameof(target.EntityType), EntityTypeMaxLength);
        FieldPath = NormalizeOptional(target.FieldPath, nameof(target.FieldPath), FieldPathMaxLength);
        Operator = conditionOperator;
        ValueType = valueType;
        RepeatFiringMode = repeatFiringMode;
        InputValues = CloneNodes(inputValues);
        PreviousOutcome = previousOutcome;
        CurrentOutcome = currentOutcome;
        Fired = fired;
        Diagnostic = NormalizeOptional(diagnostic, nameof(diagnostic), DiagnosticMaxLength);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ConditionDefinitionId { get; private set; } = null!;
    public Guid? WorkflowTriggerId { get; private set; }
    public DateTime EvaluatedUtc { get; private set; }
    public ConditionOperandSourceType SourceType { get; private set; }
    public string? SourceName { get; private set; }
    public string? EntityType { get; private set; }
    public string? FieldPath { get; private set; }
    public ConditionOperator Operator { get; private set; }
    public ConditionValueType? ValueType { get; private set; }
    public RepeatFiringMode RepeatFiringMode { get; private set; }
    public Dictionary<string, JsonNode?> InputValues { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool? PreviousOutcome { get; private set; }
    public bool CurrentOutcome { get; private set; }
    public bool Fired { get; private set; }
    public string? Diagnostic { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public WorkflowTrigger? WorkflowTrigger { get; private set; }

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return NormalizeOptional(value, name, maxLength)!;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
