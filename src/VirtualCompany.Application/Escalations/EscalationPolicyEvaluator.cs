using System.Globalization;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Escalations;

public sealed class EscalationPolicyEvaluationService : IEscalationPolicyEvaluationService
{
    private readonly IEscalationRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly TimeProvider _timeProvider;

    public EscalationPolicyEvaluationService(
        IEscalationRepository repository,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _timeProvider = timeProvider;
    }

    public async Task<EscalationPolicyEvaluationSummary> EvaluateAsync(
        EvaluateEscalationPoliciesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input);

        ValidateInput(command.Input);

        var evaluatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await WriteAuditAsync(
            command.Input,
            AuditEventActions.EscalationPolicyEvaluationStarted,
            AuditEventOutcomes.Requested,
            "Escalation policy evaluation started.",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["policyCount"] = command.Policies.Count.ToString(CultureInfo.InvariantCulture),
                ["eventType"] = command.Input.EventType,
                ["sourceStatus"] = command.Input.CurrentStatus,
                ["lifecycleVersion"] = command.Input.LifecycleVersion.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);

        var results = new List<EscalationPolicyEvaluationResult>();
        foreach (var policy in command.Policies.Where(x => x.Enabled).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var level in policy.Levels.OrderBy(x => x.EscalationLevel))
            {
                var result = await EvaluateLevelAsync(command.Input, policy, level, evaluatedUtc, cancellationToken);
                results.Add(result);
            }
        }

        await WriteAuditAsync(
            command.Input,
            AuditEventActions.EscalationPolicyEvaluationCompleted,
            AuditEventOutcomes.Succeeded,
            "Escalation policy evaluation completed.",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["resultCount"] = results.Count.ToString(CultureInfo.InvariantCulture),
                ["createdCount"] = results.Count(x => x.EscalationCreated).ToString(CultureInfo.InvariantCulture),
                ["skippedDuplicateCount"] = results.Count(x => x.SkippedDueToIdempotency).ToString(CultureInfo.InvariantCulture),
                ["correlationId"] = command.Input.CorrelationId
            },
            cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        return new EscalationPolicyEvaluationSummary(
            command.Input.CompanyId,
            command.Input.SourceEntityId,
            command.Input.SourceEntityType,
            command.Input.CorrelationId,
            evaluatedUtc,
            results);
    }

    private async Task<EscalationPolicyEvaluationResult> EvaluateLevelAsync(
        EscalationEvaluationInput input,
        EscalationPolicyDefinition policy,
        EscalationLevelDefinition level,
        DateTime evaluatedUtc,
        CancellationToken cancellationToken)
    {
        var levelReason = string.IsNullOrWhiteSpace(level.Reason)
            ? $"Policy {policy.PolicyId} level {level.EscalationLevel} conditions matched."
            : level.Reason.Trim();

        var (conditionsMet, diagnostic) = EvaluateConditions(input, level);
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["policyId"] = policy.PolicyId.ToString(),
            ["policyName"] = policy.Name,
            ["escalationLevel"] = level.EscalationLevel.ToString(CultureInfo.InvariantCulture),
            ["conditionsMet"] = conditionsMet.ToString(CultureInfo.InvariantCulture),
            ["diagnostic"] = diagnostic,
            ["sourceEntityType"] = input.SourceEntityType,
            ["sourceEntityId"] = input.SourceEntityId.ToString(),
            ["lifecycleVersion"] = input.LifecycleVersion.ToString(CultureInfo.InvariantCulture)
        };

        await WriteAuditAsync(
            input,
            AuditEventActions.EscalationPolicyEvaluationResult,
            diagnostic is null ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed,
            conditionsMet ? levelReason : diagnostic ?? "Escalation policy conditions were not met.",
            metadata,
            cancellationToken);

        if (!conditionsMet)
        {
            return new EscalationPolicyEvaluationResult(policy.PolicyId, level.EscalationLevel, false, false, false, levelReason, diagnostic, null);
        }

        if (await _repository.HasExecutedAsync(input.CompanyId, policy.PolicyId, input.SourceEntityType, input.SourceEntityId, level.EscalationLevel, input.LifecycleVersion, cancellationToken))
        {
            await WriteAuditAsync(
                input,
                AuditEventActions.EscalationDuplicateSkipped,
                AuditEventOutcomes.Succeeded,
                "Escalation skipped because this policy level already executed for the current source lifecycle.",
                metadata,
                cancellationToken);

            return new EscalationPolicyEvaluationResult(policy.PolicyId, level.EscalationLevel, true, false, true, levelReason, null, null);
        }

        var escalation = new Escalation(
            Guid.NewGuid(),
            input.CompanyId,
            policy.PolicyId,
            input.SourceEntityId,
            input.SourceEntityType,
            level.EscalationLevel,
            levelReason,
            evaluatedUtc,
            input.CorrelationId,
            input.LifecycleVersion);

        var creation = await _repository.TryCreateAsync(escalation, cancellationToken);
        metadata["escalationId"] = creation.Escalation.Id.ToString();

        if (!creation.Created)
        {
            await WriteAuditAsync(
                input,
                AuditEventActions.EscalationDuplicateSkipped,
                AuditEventOutcomes.Succeeded,
                "Escalation skipped because this policy level already executed for the current source lifecycle.",
                metadata,
                cancellationToken);

            return new EscalationPolicyEvaluationResult(policy.PolicyId, level.EscalationLevel, true, false, true, levelReason, null, creation.Escalation.Id);
        }

        await WriteAuditAsync(
            input,
            AuditEventActions.EscalationCreated,
            AuditEventOutcomes.Succeeded,
            levelReason,
            metadata,
            cancellationToken);

        return new EscalationPolicyEvaluationResult(policy.PolicyId, level.EscalationLevel, true, true, false, levelReason, null, creation.Escalation.Id);
    }

    private static (bool Met, string? Diagnostic) EvaluateConditions(EscalationEvaluationInput input, EscalationLevelDefinition level)
    {
        if (level.EscalationLevel <= 0)
        {
            return (false, "Escalation level must be greater than zero.");
        }

        if (level.Conditions.Count == 0)
        {
            return (false, "At least one escalation condition is required.");
        }

        var outcomes = new List<bool>();
        foreach (var condition in level.Conditions)
        {
            var (met, diagnostic) = EvaluateCondition(input, condition);
            if (diagnostic is not null)
            {
                return (false, diagnostic);
            }

            outcomes.Add(met);
        }

        var result = level.Mode is EscalationConditionMode.Any
            ? outcomes.Any(x => x)
            : outcomes.All(x => x);

        return (result, null);
    }

    private static (bool Met, string? Diagnostic) EvaluateCondition(EscalationEvaluationInput input, EscalationConditionDefinition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Field) && condition.Type is not EscalationConditionType.Timer)
        {
            return (false, "Escalation condition field is required.");
        }

        return condition.Type switch
        {
            EscalationConditionType.Threshold or EscalationConditionType.Rule => EvaluateComparison(input, condition),
            EscalationConditionType.Timer => EvaluateTimer(input, condition),
            _ => (false, "Escalation condition type is not supported.")
        };
    }

    private static (bool Met, string? Diagnostic) EvaluateTimer(EscalationEvaluationInput input, EscalationConditionDefinition condition)
    {
        if (condition.DurationSeconds is null or < 0)
        {
            return (false, "Timer condition durationSeconds must be zero or greater.");
        }

        var sinceField = string.IsNullOrWhiteSpace(condition.SinceField) ? condition.Field : condition.SinceField;
        if (string.IsNullOrWhiteSpace(sinceField))
        {
            return (false, "Timer condition requires a field or sinceField.");
        }

        var current = ResolveField(input, sinceField);
        if (!TryGetDateTimeOffset(current, out var sinceUtc))
        {
            return (false, $"Timer condition field '{sinceField}' could not be resolved as a datetime.");
        }

        var elapsed = input.EventUtc.Kind == DateTimeKind.Utc
            ? input.EventUtc - sinceUtc.UtcDateTime
            : input.EventUtc.ToUniversalTime() - sinceUtc.UtcDateTime;

        return (elapsed.TotalSeconds >= condition.DurationSeconds.Value, null);
    }

    private static (bool Met, string? Diagnostic) EvaluateComparison(EscalationEvaluationInput input, EscalationConditionDefinition condition)
    {
        var current = ResolveField(input, condition.Field);
        if (current is null)
        {
            return (false, $"Escalation condition field '{condition.Field}' could not be resolved.");
        }

        if (condition.Value is null)
        {
            return (false, $"Escalation condition '{condition.Field}' comparison value is required.");
        }

        var comparison = ToJsonNode(condition.Value);
        return Compare(current, comparison, condition.Operator);
    }

    private static JsonNode? ResolveField(EscalationEvaluationInput input, string fieldPath)
    {
        if (input.Fields.TryGetValue(fieldPath, out var value))
        {
            return value;
        }

        var segments = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 1)
        {
            return input.Payload is not null && input.Payload.TryGetValue(fieldPath, out var payloadValue)
                ? payloadValue
                : null;
        }

        var rootName = segments[0];
        JsonNode? current = input.Fields.TryGetValue(rootName, out var root)
            ? root
            : input.Payload is not null && input.Payload.TryGetValue(rootName, out var payloadRoot)
                ? payloadRoot
                : null;

        foreach (var segment in segments.Skip(1))
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var child))
            {
                current = child;
                continue;
            }

            return null;
        }

        return current;
    }

    private static (bool Met, string? Diagnostic) Compare(JsonNode current, JsonNode? comparison, string comparisonOperator)
    {
        if (comparison is null)
        {
            return (false, "Escalation comparison value is required.");
        }

        var op = comparisonOperator.Trim().ToLowerInvariant();
        return op switch
        {
            "eq" => (ValuesEqual(current, comparison), null),
            "neq" => (!ValuesEqual(current, comparison), null),
            "gt" => CompareOrdered(current, comparison, (left, right) => left > right),
            "gte" => CompareOrdered(current, comparison, (left, right) => left >= right),
            "lt" => CompareOrdered(current, comparison, (left, right) => left < right),
            "lte" => CompareOrdered(current, comparison, (left, right) => left <= right),
            "in" => comparison is JsonArray values
                ? (values.Any(value => value is not null && ValuesEqual(current, value)), null)
                : (false, "The in operator requires an array comparison value."),
            "contains" => EvaluateContains(current, comparison),
            _ => (false, $"Escalation condition operator '{comparisonOperator}' is not supported.")
        };
    }

    private static (bool Met, string? Diagnostic) CompareOrdered(JsonNode current, JsonNode comparison, Func<decimal, decimal, bool> numericComparison)
    {
        if (TryGetDecimal(current, out var currentNumber) && TryGetDecimal(comparison, out var comparisonNumber))
        {
            return (numericComparison(currentNumber, comparisonNumber), null);
        }

        if (TryGetBusinessOrdinal(current, out var currentOrdinal) && TryGetBusinessOrdinal(comparison, out var comparisonOrdinal))
        {
            return (numericComparison(currentOrdinal, comparisonOrdinal), null);
        }

        if (TryGetDateTimeOffset(current, out var currentDate) && TryGetDateTimeOffset(comparison, out var comparisonDate))
        {
            return (numericComparison(currentDate.UtcDateTime.Ticks, comparisonDate.UtcDateTime.Ticks), null);
        }

        return (false, "Ordered escalation comparison requires numeric or datetime values.");
    }

    private static (bool Met, string? Diagnostic) EvaluateContains(JsonNode current, JsonNode comparison)
    {
        if (current is JsonArray array)
        {
            return (array.Any(value => value is not null && ValuesEqual(value, comparison)), null);
        }

        if (TryGetString(current, out var currentText) && TryGetString(comparison, out var comparisonText))
        {
            return (currentText.Contains(comparisonText, StringComparison.OrdinalIgnoreCase), null);
        }

        return (false, "The contains operator requires a string or array current value.");
    }

    private static bool ValuesEqual(JsonNode current, JsonNode comparison)
    {
        if (TryGetString(current, out var currentText) && TryGetString(comparison, out var comparisonText))
        {
            return string.Equals(currentText, comparisonText, StringComparison.OrdinalIgnoreCase);
        }

        return JsonNode.DeepEquals(current, comparison);
    }

    private static bool TryGetBusinessOrdinal(JsonNode? node, out decimal value)
    {
        value = 0;
        if (!TryGetString(node, out var text))
        {
            return false;
        }

        return KnownBusinessOrdinals.TryGetValue(text.Trim(), out value);
    }

    private static JsonNode? ToJsonNode(object value) =>
        value switch
        {
            JsonNode node => node.DeepClone(),
            string text => JsonValue.Create(text),
            int number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            bool flag => JsonValue.Create(flag),
            DateTime dateTime => JsonValue.Create(dateTime),
            DateTimeOffset dateTimeOffset => JsonValue.Create(dateTimeOffset),
            IEnumerable<string> strings => new JsonArray(strings.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            IEnumerable<int> numbers => new JsonArray(numbers.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            _ => JsonValue.Create(value.ToString())
        };

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

    private static readonly IReadOnlyDictionary<string, decimal> KnownBusinessOrdinals =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["low"] = 1,
            ["normal"] = 2,
            ["medium"] = 2,
            ["high"] = 3,
            ["critical"] = 4
        };

    private static void ValidateInput(EscalationEvaluationInput input)
    {
        if (input.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(input));
        }

        if (input.SourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("SourceEntityId is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.SourceEntityType))
        {
            throw new ArgumentException("SourceEntityType is required.", nameof(input));
        }

        if (input.LifecycleVersion < 0)
        {
            throw new ArgumentException("LifecycleVersion cannot be negative.", nameof(input));
        }
    }

    private Task WriteAuditAsync(
        EscalationEvaluationInput input,
        string action,
        string outcome,
        string rationaleSummary,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) =>
        _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                input.CompanyId,
                AuditActorTypes.System,
                null,
                action,
                AuditTargetTypes.EscalationPolicy,
                input.SourceEntityId.ToString(),
                outcome,
                rationaleSummary,
                DataSources: ["escalation_policy", input.SourceEntityType],
                Metadata: metadata,
                CorrelationId: input.CorrelationId,
                OccurredUtc: _timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
}
