using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ConditionExpressionValidatorTests
{
    [Fact]
    public void Validate_accepts_valid_metric_threshold_condition()
    {
        var condition = MetricCondition(
            ConditionOperator.GreaterThan,
            ConditionValueType.Number,
            JsonValue.Create(10));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_valid_entity_field_equals_condition()
    {
        var condition = EntityFieldCondition(
            ConditionOperator.Equals,
            ConditionValueType.String,
            JsonValue.Create("blocked"));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_changed_since_last_evaluation_without_comparison_value()
    {
        var condition = MetricCondition(
            ConditionOperator.ChangedSinceLastEvaluation,
            null,
            null);

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_datetime_ordering_condition()
    {
        var condition = MetricCondition(
            ConditionOperator.GreaterThan,
            ConditionValueType.DateTime,
            JsonValue.Create("2026-04-13T08:00:00Z"));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_omitted_repeat_mode_as_false_to_true_transition_default()
    {
        var condition = new ConditionExpression(
            new ConditionTargetReference(ConditionOperandSourceType.Metric, "task.backlog.count", null, null),
            ConditionOperator.LessThan,
            ConditionValueType.Number,
            JsonValue.Create(5));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Empty(errors);
        Assert.Equal(RepeatFiringMode.FalseToTrueTransition, condition.RepeatFiringMode);
    }

    [Fact]
    public void Validate_rejects_missing_condition()
    {
        var errors = ConditionExpressionValidator.Validate(null);

        Assert.Contains("condition", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_missing_target()
    {
        var condition = new ConditionExpression(
            null!,
            ConditionOperator.GreaterThan,
            ConditionValueType.Number,
            JsonValue.Create(10));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.target", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_missing_operator()
    {
        var condition = MetricCondition(
            default,
            ConditionValueType.Number,
            JsonValue.Create(10));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.operator", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_unsupported_operator()
    {
        var condition = MetricCondition(
            (ConditionOperator)99,
            ConditionValueType.Number,
            JsonValue.Create(10));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.operator", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_missing_comparison_value_for_threshold_condition()
    {
        var condition = MetricCondition(
            ConditionOperator.Equals,
            ConditionValueType.Number,
            null);

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.comparisonValue", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_comparison_value_for_changed_since_last_evaluation()
    {
        var condition = MetricCondition(
            ConditionOperator.ChangedSinceLastEvaluation,
            null,
            JsonValue.Create("green"));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.comparisonValue", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_non_numeric_value_type_for_greater_than()
    {
        var condition = MetricCondition(
            ConditionOperator.GreaterThan,
            ConditionValueType.String,
            JsonValue.Create("10"));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.valueType", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_blank_metric_name_for_metric_target()
    {
        var condition = new ConditionExpression(
            new ConditionTargetReference(ConditionOperandSourceType.Metric, " ", null, null),
            ConditionOperator.Equals,
            ConditionValueType.Number,
            JsonValue.Create(1));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.target.metricName", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_blank_field_path_for_entity_field_target()
    {
        var condition = new ConditionExpression(
            new ConditionTargetReference(ConditionOperandSourceType.EntityField, null, "workTask", " "),
            ConditionOperator.Equals,
            ConditionValueType.String,
            JsonValue.Create("blocked"));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.target.fieldPath", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_metric_fields_on_entity_field_target()
    {
        var condition = new ConditionExpression(
            new ConditionTargetReference(ConditionOperandSourceType.EntityField, "task.backlog.count", "workTask", "status"),
            ConditionOperator.Equals,
            ConditionValueType.String,
            JsonValue.Create("blocked"));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.target.metricName", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_malformed_literal_for_declared_type()
    {
        var condition = MetricCondition(
            ConditionOperator.GreaterThan,
            ConditionValueType.Number,
            JsonValue.Create("many"));

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.comparisonValue", errors.Keys);
    }

    [Fact]
    public void Validate_rejects_invalid_repeat_firing_mode()
    {
        var condition = new ConditionExpression(
            new ConditionTargetReference(ConditionOperandSourceType.Metric, "task.backlog.count", null, null),
            ConditionOperator.GreaterThan,
            ConditionValueType.Number,
            JsonValue.Create(10),
            (RepeatFiringMode)99);

        var errors = ConditionExpressionValidator.Validate(condition);

        Assert.Contains("condition.repeatFiringMode", errors.Keys);
    }

    [Fact]
    public void ValidateAndThrow_raises_field_level_errors()
    {
        var condition = MetricCondition(
            ConditionOperator.GreaterThan,
            ConditionValueType.String,
            JsonValue.Create("10"));

        var exception = Assert.Throws<AgentValidationException>(() =>
            ConditionExpressionValidator.ValidateAndThrow(condition));

        Assert.Contains("condition.valueType", exception.Errors.Keys);
    }

    private static ConditionExpression MetricCondition(
        ConditionOperator conditionOperator,
        ConditionValueType? valueType,
        JsonNode? comparisonValue) =>
        new(
            new ConditionTargetReference(ConditionOperandSourceType.Metric, "task.backlog.count", null, null),
            conditionOperator,
            valueType,
            comparisonValue);

    private static ConditionExpression EntityFieldCondition(
        ConditionOperator conditionOperator,
        ConditionValueType? valueType,
        JsonNode? comparisonValue) =>
        new(
            new ConditionTargetReference(ConditionOperandSourceType.EntityField, null, "workTask", "status"),
            conditionOperator,
            valueType,
            comparisonValue);
}
