using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Escalations;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed class EscalationPolicyEvaluatorTests
{
    private static readonly Guid CompanyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SourceEntityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PolicyId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime EventUtc = new(2026, 4, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Evaluate_creates_escalation_when_threshold_condition_is_met()
    {
        var repository = new InMemoryEscalationRepository();
        var audit = new CapturingAuditEventWriter();
        var service = CreateService(repository, audit);

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?>
                {
                    ["priority"] = JsonValue.Create(4)
                }),
                [
                    Policy(Level(
                        1,
                        "Critical task priority.",
                        new EscalationConditionDefinition(EscalationConditionType.Threshold, "priority", "gte", 4)))
                ]),
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        Assert.True(result.ConditionsMet);
        Assert.True(result.EscalationCreated);
        Assert.False(result.SkippedDueToIdempotency);

        var escalation = Assert.Single(repository.Escalations);
        Assert.Equal(PolicyId, escalation.PolicyId);
        Assert.Equal(SourceEntityId, escalation.SourceEntityId);
        Assert.Equal(1, escalation.EscalationLevel);
        Assert.Equal("Critical task priority.", escalation.Reason);
        Assert.Equal("correlation-1", escalation.CorrelationId);
        Assert.Contains(audit.Events, x => x.Action == AuditEventActions.EscalationCreated && x.CorrelationId == "correlation-1");
    }

    [Fact]
    public async Task Evaluate_writes_evaluation_and_action_audit_events_with_same_correlation_id()
    {
        var repository = new InMemoryEscalationRepository();
        var audit = new CapturingAuditEventWriter();
        var service = CreateService(repository, audit);

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?>
                {
                    ["priority"] = JsonValue.Create(4)
                }),
                [
                    Policy(Level(
                        1,
                        "Critical task priority.",
                        new EscalationConditionDefinition(EscalationConditionType.Threshold, "priority", "gte", 4)))
                ]),
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        var escalation = Assert.Single(repository.Escalations);

        Assert.True(result.EscalationCreated);
        Assert.Equal("correlation-1", escalation.CorrelationId);
        Assert.Contains(
            audit.Events,
            x => x.Action == AuditEventActions.EscalationPolicyEvaluationResult &&
                 x.CorrelationId == "correlation-1" &&
                 x.Metadata["policyId"] == PolicyId.ToString() &&
                 x.Metadata["sourceEntityId"] == SourceEntityId.ToString());
        Assert.Contains(
            audit.Events,
            x => x.Action == AuditEventActions.EscalationCreated &&
                 x.CorrelationId == "correlation-1" &&
                 x.Metadata["escalationId"] == escalation.Id.ToString() &&
                 x.Metadata["escalationLevel"] == "1");
    }

    [Fact]
    public async Task Evaluate_supports_ordinal_thresholds_for_priority_and_severity_labels()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                AlertInput(new Dictionary<string, JsonNode?>
                {
                    ["severity"] = JsonValue.Create("critical"),
                    ["priority"] = JsonValue.Create("high")
                }),
                [
                    Policy(
                        Level(
                            1,
                            "High or higher priority.",
                            new EscalationConditionDefinition(EscalationConditionType.Threshold, "priority", "gte", "high")),
                        Level(
                            2,
                            "Critical severity.",
                            new EscalationConditionDefinition(EscalationConditionType.Threshold, "severity", "gt", "high")))
                ]),
            CancellationToken.None);

        Assert.Equal(2, summary.Results.Count);
        Assert.All(summary.Results, result => Assert.True(result.EscalationCreated));
        Assert.Equal(2, repository.Escalations.Count);
    }

    [Fact]
    public async Task Evaluate_does_not_create_escalation_when_threshold_condition_is_not_met()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?>
                {
                    ["retryCount"] = JsonValue.Create(1)
                }),
                [
                    Policy(Level(
                        1,
                        "Task retried repeatedly.",
                        new EscalationConditionDefinition(EscalationConditionType.Threshold, "retryCount", "gte", 3)))
                ]),
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        Assert.False(result.ConditionsMet);
        Assert.False(result.EscalationCreated);
        Assert.Empty(repository.Escalations);
    }

    [Fact]
    public async Task Evaluate_supports_timer_conditions()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                AlertInput(new Dictionary<string, JsonNode?>
                {
                    ["openedAt"] = JsonValue.Create(EventUtc.AddMinutes(-35))
                }),
                [
                    Policy(Level(
                        2,
                        "Alert has remained open for 30 minutes.",
                        new EscalationConditionDefinition(EscalationConditionType.Timer, "openedAt", "gte", DurationSeconds: 1800)))
                ]),
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        Assert.True(result.ConditionsMet);
        Assert.True(result.EscalationCreated);
        Assert.Equal(EscalationSourceEntityTypes.Alert, repository.Escalations.Single().SourceEntityType);
    }

    [Fact]
    public async Task Evaluate_supports_rule_any_combinations_over_payload()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(
                    new Dictionary<string, JsonNode?>(),
                    new Dictionary<string, JsonNode?>
                    {
                        ["details"] = new JsonObject
                        {
                            ["department"] = "finance",
                            ["tags"] = new JsonArray("sla", "customer")
                        }
                    }),
                [
                    Policy(new EscalationLevelDefinition(
                        1,
                        "Finance or SLA task needs review.",
                        [
                            new EscalationConditionDefinition(EscalationConditionType.Rule, "details.department", "eq", "legal"),
                            new EscalationConditionDefinition(EscalationConditionType.Rule, "details.tags", "contains", "sla")
                        ],
                        EscalationConditionMode.Any))
                ]),
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        Assert.True(result.ConditionsMet);
        Assert.True(result.EscalationCreated);
    }

    [Fact]
    public async Task Evaluate_uses_case_insensitive_string_matching_for_rules()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(
                    new Dictionary<string, JsonNode?>
                    {
                        ["severity"] = JsonValue.Create("Critical")
                    },
                    new Dictionary<string, JsonNode?>
                    {
                        ["tags"] = new JsonArray("Customer", "SLA")
                    }),
                [
                    Policy(new EscalationLevelDefinition(
                        1,
                        "Critical SLA task needs review.",
                        [
                            new EscalationConditionDefinition(EscalationConditionType.Rule, "severity", "eq", "critical"),
                            new EscalationConditionDefinition(EscalationConditionType.Rule, "tags", "contains", "sla")
                        ]))
                ]),
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        Assert.True(result.ConditionsMet);
        Assert.True(result.EscalationCreated);
        Assert.Single(repository.Escalations);
    }

    [Fact]
    public async Task Evaluate_skips_duplicate_escalation_for_same_policy_level_and_lifecycle()
    {
        var repository = new InMemoryEscalationRepository();
        var audit = new CapturingAuditEventWriter();
        var service = CreateService(repository, audit);
        var command = new EvaluateEscalationPoliciesCommand(
            TaskInput(new Dictionary<string, JsonNode?>
            {
                ["severity"] = JsonValue.Create("critical")
            }),
            [
                Policy(Level(
                    1,
                    "Critical severity.",
                    new EscalationConditionDefinition(EscalationConditionType.Rule, "severity", "eq", "critical")))
            ]);

        await service.EvaluateAsync(command, CancellationToken.None);
        var second = await service.EvaluateAsync(command, CancellationToken.None);

        Assert.Single(repository.Escalations);
        var result = Assert.Single(second.Results);
        Assert.True(result.ConditionsMet);
        Assert.False(result.EscalationCreated);
        Assert.True(result.SkippedDueToIdempotency);
        Assert.Contains(audit.Events, x => x.Action == AuditEventActions.EscalationDuplicateSkipped && x.CorrelationId == "correlation-1");
    }

    [Fact]
    public async Task Evaluate_allows_different_escalation_levels_for_same_policy_source_and_lifecycle()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());
        var policy = Policy(
            Level(
                1,
                "High severity.",
                new EscalationConditionDefinition(EscalationConditionType.Rule, "severity", "eq", "critical")),
            Level(
                2,
                "High severity and stuck.",
                [
                    new EscalationConditionDefinition(EscalationConditionType.Rule, "severity", "eq", "critical"),
                    new EscalationConditionDefinition(EscalationConditionType.Threshold, "blockedMinutes", "gte", 30)
                ]));

        var first = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?>
                {
                    ["severity"] = JsonValue.Create("critical"),
                    ["blockedMinutes"] = JsonValue.Create(15)
                }),
                [policy]),
            CancellationToken.None);
        var second = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?>
                {
                    ["severity"] = JsonValue.Create("critical"),
                    ["blockedMinutes"] = JsonValue.Create(45)
                }),
                [policy]),
            CancellationToken.None);

        Assert.Equal(2, repository.Escalations.Count);
        Assert.True(first.Results.Single(x => x.EscalationLevel == 1).EscalationCreated);
        Assert.False(second.Results.Single(x => x.EscalationLevel == 1).EscalationCreated);
        Assert.True(second.Results.Single(x => x.EscalationLevel == 2).EscalationCreated);
    }

    [Fact]
    public async Task Evaluate_allows_escalation_after_resolved_entity_is_reopened_with_new_lifecycle()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());
        var policy = Policy(Level(
            1,
            "Critical severity.",
            new EscalationConditionDefinition(EscalationConditionType.Rule, "severity", "eq", "critical")));

        await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?> { ["severity"] = JsonValue.Create("critical") }, lifecycleVersion: 0),
                [policy]),
            CancellationToken.None);
        var reopened = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?> { ["severity"] = JsonValue.Create("critical") }, lifecycleVersion: 1),
                [policy]),
            CancellationToken.None);

        Assert.Equal(2, repository.Escalations.Count);
        Assert.True(reopened.Results.Single().EscalationCreated);
        Assert.Contains(repository.Escalations, x => x.LifecycleVersion == 0);
        Assert.Contains(repository.Escalations, x => x.LifecycleVersion == 1);
    }

    [Fact]
    public async Task Evaluate_allows_same_source_id_for_different_source_entity_types()
    {
        var repository = new InMemoryEscalationRepository();
        var service = CreateService(repository, new CapturingAuditEventWriter());
        var policy = Policy(Level(
            1,
            "Critical severity.",
            new EscalationConditionDefinition(EscalationConditionType.Rule, "severity", "eq", "critical")));

        await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?> { ["severity"] = JsonValue.Create("critical") }),
                [policy]),
            CancellationToken.None);
        var alert = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                AlertInput(new Dictionary<string, JsonNode?> { ["severity"] = JsonValue.Create("critical") }),
                [policy]),
            CancellationToken.None);

        Assert.Equal(2, repository.Escalations.Count);
        Assert.True(alert.Results.Single().EscalationCreated);
        Assert.Contains(repository.Escalations, x => x.SourceEntityType == EscalationSourceEntityTypes.WorkTask);
        Assert.Contains(repository.Escalations, x => x.SourceEntityType == EscalationSourceEntityTypes.Alert);
    }

    [Fact]
    public void Source_entities_increment_lifecycle_when_reopened_after_resolution()
    {
        var task = new WorkTask(Guid.NewGuid(), CompanyId, "ops", "Investigate incident", null, WorkTaskPriority.High, null, null, AuditActorTypes.System, null);
        task.UpdateStatus(WorkTaskStatus.Completed);
        task.UpdateStatus(WorkTaskStatus.InProgress);

        var alert = new Alert(Guid.NewGuid(), CompanyId, AlertType.Anomaly, AlertSeverity.High, "Anomaly", "Investigate anomaly.", new Dictionary<string, JsonNode?> { ["signal"] = JsonValue.Create("x") }, "correlation-1", "fp-1");
        alert.UpdateStatus(AlertStatus.Resolved);
        alert.UpdateStatus(AlertStatus.Open);

        Assert.Equal(1, task.SourceLifecycleVersion);
        Assert.Equal(1, alert.SourceLifecycleVersion);
    }

    [Fact]
    public async Task Evaluate_fails_closed_and_audits_invalid_condition_config()
    {
        var repository = new InMemoryEscalationRepository();
        var audit = new CapturingAuditEventWriter();
        var service = CreateService(repository, audit);

        var summary = await service.EvaluateAsync(
            new EvaluateEscalationPoliciesCommand(
                TaskInput(new Dictionary<string, JsonNode?>
                {
                    ["retryCount"] = JsonValue.Create(3)
                }),
                [
                    Policy(Level(
                        1,
                        "Invalid rule should not execute.",
                        new EscalationConditionDefinition(EscalationConditionType.Threshold, "retryCount", "approximately", 3)))
                ]),
            CancellationToken.None);

        var result = Assert.Single(summary.Results);
        Assert.False(result.ConditionsMet);
        Assert.False(result.EscalationCreated);
        Assert.NotNull(result.Diagnostic);
        Assert.Empty(repository.Escalations);
        Assert.Contains(audit.Events, x => x.Action == AuditEventActions.EscalationPolicyEvaluationResult && x.Outcome == AuditEventOutcomes.Failed && x.CorrelationId == "correlation-1");
    }

    private static EscalationPolicyEvaluationService CreateService(InMemoryEscalationRepository repository, CapturingAuditEventWriter audit) =>
        new(repository, audit, TimeProvider.System);

    private static EscalationPolicyDefinition Policy(params EscalationLevelDefinition[] levels) =>
        new(PolicyId, "Default escalation policy", true, levels);

    private static EscalationLevelDefinition Level(int level, string reason, params EscalationConditionDefinition[] conditions) =>
        new(level, reason, conditions);

    private static EscalationEvaluationInput TaskInput(
        IReadOnlyDictionary<string, JsonNode?> fields,
        IReadOnlyDictionary<string, JsonNode?>? payload = null,
        int lifecycleVersion = 0) =>
        EscalationEvaluationInput.ForTaskEvent(
            CompanyId,
            SourceEntityId,
            "task.updated",
            EventUtc,
            "blocked",
            lifecycleVersion,
            "correlation-1",
            fields,
            payload);

    private static EscalationEvaluationInput AlertInput(IReadOnlyDictionary<string, JsonNode?> fields) =>
        EscalationEvaluationInput.ForAlertEvent(
            CompanyId,
            SourceEntityId,
            "alert.detected",
            EventUtc,
            "open",
            0,
            "correlation-1",
            fields);

    private sealed class InMemoryEscalationRepository : IEscalationRepository
    {
        public List<Escalation> Escalations { get; } = [];

        public Task<bool> HasExecutedAsync(Guid companyId, Guid policyId, string sourceEntityType, Guid sourceEntityId, int escalationLevel, int lifecycleVersion, CancellationToken cancellationToken) =>
            Task.FromResult(Escalations.Any(x =>
                x.CompanyId == companyId &&
                x.PolicyId == policyId &&
                x.SourceEntityType == sourceEntityType &&
                x.SourceEntityId == sourceEntityId &&
                x.EscalationLevel == escalationLevel &&
                x.LifecycleVersion == lifecycleVersion));

        public Task<EscalationCreationResult> TryCreateAsync(Escalation escalation, CancellationToken cancellationToken)
        {
            var existing = Escalations.SingleOrDefault(x =>
                x.CompanyId == escalation.CompanyId &&
                x.PolicyId == escalation.PolicyId &&
                x.SourceEntityType == escalation.SourceEntityType &&
                x.SourceEntityId == escalation.SourceEntityId &&
                x.EscalationLevel == escalation.EscalationLevel &&
                x.LifecycleVersion == escalation.LifecycleVersion);
            if (existing is not null)
            {
                return Task.FromResult(new EscalationCreationResult(false, existing, true));
            }

            Escalations.Add(escalation);
            return Task.FromResult(new EscalationCreationResult(true, escalation, false));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CapturingAuditEventWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];

        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
