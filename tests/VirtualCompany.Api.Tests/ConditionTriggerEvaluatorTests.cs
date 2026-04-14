using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class ConditionTriggerEvaluatorTests
{
    private static readonly DateTime EvaluationUtc = new(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ConditionOperator.GreaterThan, 11, 10, true)]
    [InlineData(ConditionOperator.GreaterThan, 9, 10, false)]
    [InlineData(ConditionOperator.LessThan, 9, 10, true)]
    [InlineData(ConditionOperator.LessThan, 11, 10, false)]
    [InlineData(ConditionOperator.Equals, 10, 10, true)]
    [InlineData(ConditionOperator.Equals, 11, 10, false)]
    public void Evaluate_handles_numeric_threshold_operators(
        ConditionOperator conditionOperator,
        int current,
        int target,
        bool expectedOutcome)
    {
        var result = Evaluate(
            MetricCondition(conditionOperator, ConditionValueType.Number, JsonValue.Create(target)),
            JsonValue.Create(current),
            hasPreviousValue: false,
            previousValue: null,
            previousOutcome: false);

        Assert.Equal(expectedOutcome, result.Outcome);
    }

    [Fact]
    public void Evaluate_handles_strict_string_equals()
    {
        var result = Evaluate(
            EntityFieldCondition(ConditionOperator.Equals, ConditionValueType.String, JsonValue.Create("blocked")),
            JsonValue.Create("blocked"),
            hasPreviousValue: true,
            previousValue: JsonValue.Create("open"),
            previousOutcome: false);

        Assert.True(result.Outcome);
        Assert.True(result.ShouldFire);
    }

    [Fact]
    public void Evaluate_changed_since_last_evaluation_is_false_on_first_evaluation()
    {
        var result = Evaluate(
            MetricCondition(ConditionOperator.ChangedSinceLastEvaluation, null, null),
            JsonValue.Create("green"),
            hasPreviousValue: false,
            previousValue: null,
            previousOutcome: null);

        Assert.False(result.Outcome);
        Assert.False(result.ShouldFire);
    }

    [Fact]
    public void Evaluate_changed_since_last_evaluation_compares_current_and_previous_values()
    {
        var result = Evaluate(
            MetricCondition(ConditionOperator.ChangedSinceLastEvaluation, null, null),
            JsonValue.Create("red"),
            hasPreviousValue: true,
            previousValue: JsonValue.Create("green"),
            previousOutcome: false);

        Assert.True(result.Outcome);
        Assert.True(result.ShouldFire);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public void Evaluate_fires_only_on_false_to_true_transition_by_default(
        bool previousOutcome,
        bool currentMatches,
        bool expectedFire)
    {
        var result = Evaluate(
            MetricCondition(ConditionOperator.Equals, ConditionValueType.Number, JsonValue.Create(10)),
            JsonValue.Create(currentMatches ? 10 : 9),
            hasPreviousValue: true,
            previousValue: JsonValue.Create(9),
            previousOutcome: previousOutcome);

        Assert.Equal(expectedFire, result.ShouldFire);
    }

    [Fact]
    public void Evaluate_treats_first_true_as_false_to_true_transition()
    {
        var result = Evaluate(
            MetricCondition(ConditionOperator.GreaterThan, ConditionValueType.Number, JsonValue.Create(10)),
            JsonValue.Create(11),
            hasPreviousValue: false,
            previousValue: null,
            previousOutcome: null);

        Assert.True(result.Outcome);
        Assert.True(result.ShouldFire);
    }

    [Fact]
    public void Evaluate_repeated_firing_fires_while_outcome_remains_true()
    {
        var result = Evaluate(
            MetricCondition(
                ConditionOperator.GreaterThan,
                ConditionValueType.Number,
                JsonValue.Create(10),
                RepeatFiringMode.EveryEvaluationWhileTrue),
            JsonValue.Create(11),
            hasPreviousValue: true,
            previousValue: JsonValue.Create(12),
            previousOutcome: true);

        Assert.True(result.Outcome);
        Assert.True(result.ShouldFire);
    }

    [Fact]
    public void Evaluate_type_mismatch_fails_safely_with_diagnostic()
    {
        var result = Evaluate(
            MetricCondition(ConditionOperator.GreaterThan, ConditionValueType.Number, JsonValue.Create(10)),
            JsonValue.Create("many"),
            hasPreviousValue: false,
            previousValue: null,
            previousOutcome: false);

        Assert.False(result.Outcome);
        Assert.False(result.ShouldFire);
        Assert.NotNull(result.Diagnostic);
    }

    private static ConditionEvaluationResultDto Evaluate(
        ConditionExpression condition,
        JsonNode? currentValue,
        bool hasPreviousValue,
        JsonNode? previousValue,
        bool? previousOutcome) =>
        new ConditionTriggerEvaluator().Evaluate(new ConditionEvaluationRequest(
            condition,
            currentValue,
            hasPreviousValue,
            previousValue,
            previousOutcome,
            EvaluationUtc));

    private static ConditionExpression MetricCondition(
        ConditionOperator conditionOperator,
        ConditionValueType? valueType,
        JsonNode? comparisonValue,
        RepeatFiringMode repeatFiringMode = RepeatFiringMode.FalseToTrueTransition) =>
        new(
            new ConditionTargetReference(ConditionOperandSourceType.Metric, "task.backlog.count", null, null),
            conditionOperator,
            valueType,
            comparisonValue,
            repeatFiringMode);

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
