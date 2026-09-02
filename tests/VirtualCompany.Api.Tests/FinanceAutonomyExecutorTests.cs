using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyExecutorTests
{
    [Fact]
    public async Task Disabled_hosted_executor_starts_and_stops_without_resolving_work()
    {
        using var worker = new FinanceAutonomyExecutorBackgroundService(null!,
            Options.Create(new FinanceAutonomyExecutorOptions { Enabled = false }), TimeProvider.System,
            NullLogger<FinanceAutonomyExecutorBackgroundService>.Instance);
        await worker.StartAsync(default);
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task Duplicate_workers_execute_one_business_effect_with_one_stable_key()
    {
        await using var fixture = Fixture.Create();
        await fixture.CreateRunAsync("read", fixture.Tool.Succeeded());

        var first = await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "worker-a", 10, default);
        var second = await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "worker-b", 10, default);

        Assert.Equal(1, first.Completed);
        Assert.Equal(0, second.Claimed);
        var command = Assert.Single(fixture.Tool.Commands);
        Assert.StartsWith($"finance-autonomy:{fixture.CompanyId:N}:", command.CorrelationId, StringComparison.Ordinal);
        Assert.Equal(fixture.Policy.AuthorityHash, command.ExpectedAuthorityHash);
        Assert.Equal(fixture.ActorId, Assert.Single(fixture.Tool.Actors));
    }

    [Fact]
    public async Task Transient_read_failure_retries_boundedly_without_changing_business_idempotency()
    {
        await using var fixture = Fixture.Create();
        await fixture.CreateRunAsync("read", fixture.Tool.Failed(), maximumAttempts: 2);

        var first = await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "worker-a", 10, default);
        fixture.Tool.Result = fixture.Tool.Succeeded();
        var second = await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "worker-b", 10, default);

        Assert.Equal(1, first.Retried);
        Assert.Equal(1, second.Completed);
        Assert.Equal(2, fixture.Tool.Commands.Count);
        Assert.Equal(fixture.Tool.Commands[0].CorrelationId, fixture.Tool.Commands[1].CorrelationId);
        Assert.Equal("completed", (await fixture.Run()).Status);
    }

    [Fact]
    public async Task Failed_execute_and_expired_execute_lease_require_reconciliation_before_retry()
    {
        await using var failed = Fixture.Create();
        await failed.CreateRunAsync("execute", failed.Tool.Failed());
        var ambiguous = await failed.Executor.ProcessBatchAsync(failed.Clock.UtcNow, "worker-a", 10, default);
        Assert.Equal(1, ambiguous.Reconciling);
        Assert.Equal(0, (await failed.Executor.ProcessBatchAsync(failed.Clock.UtcNow, "worker-b", 10, default)).Claimed);

        await using var expired = Fixture.Create();
        var run = await expired.CreateRunAsync("execute", expired.Tool.Succeeded());
        Assert.NotNull(await expired.Service.ClaimStepAsync(expired.CompanyId,
            new(run.Id, run.Steps[0].Id, "crashed-worker", "expired-lease", 5, run.EvidenceHash), default));
        expired.Clock.UtcNow = expired.Clock.UtcNow.AddSeconds(6);
        var recovered = await expired.Executor.ProcessBatchAsync(expired.Clock.UtcNow, "recovery-worker", 10, default);
        Assert.Equal(0, recovered.Claimed);
        Assert.Equal("reconciling", (await expired.Run()).Status);
        Assert.Empty(expired.Tool.Commands);
    }

    [Fact]
    public async Task Corrupt_object_evidence_blocks_before_tool_dispatch()
    {
        await using var fixture = Fixture.Create();
        fixture.Storage.Content = Encoding.UTF8.GetBytes("tampered");
        await fixture.CreateRunAsync("read", fixture.Tool.Succeeded(), objectArtifact: true);

        var result = await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "worker", 10, default);

        Assert.Equal(1, result.Blocked);
        Assert.Empty(fixture.Tool.Commands);
        var run = await fixture.Run();
        Assert.Equal(FinanceAutonomyRunReasonCodes.ArtifactCorrupt, run.ReasonCode);
    }

    [Fact]
    public async Task Newer_authoritative_event_blocks_stale_target_before_tool_dispatch()
    {
        await using var fixture = Fixture.Create();
        await fixture.CreateRunAsync("read", fixture.Tool.Succeeded(), staleAuthoritative: true);

        var result = await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "worker", 10, default);

        Assert.Equal(0, result.Claimed);
        Assert.Empty(fixture.Tool.Commands);
        Assert.Equal(FinanceAutonomyRunReasonCodes.EvidenceChanged, (await fixture.Run()).ReasonCode);
    }

    [Fact]
    public async Task Confirmed_provider_outcome_completes_reconciling_step_without_reexecution()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.CreateRunAsync("execute", fixture.Tool.Failed());
        await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "worker", 10, default);
        run = await fixture.Run();

        var reconciled = await fixture.Service.ReconcileStepAsync(fixture.CompanyId, run.Id, run.Steps[0].Id,
            new(FinanceAutonomyReconciliationOutcomes.ConfirmedApplied, Hash("provider-effect"),
                "Provider confirmed the effect.", "provider-safe-reference", run.Steps[0].Version), default);

        Assert.Equal("completed", reconciled.Status);
        Assert.True(reconciled.HasCompletedEffects);
        Assert.Equal("provider-safe-reference", reconciled.Steps[0].ReconciliationReference);
        Assert.Equal(1, fixture.Tool.Commands.Count);
    }

    [Fact]
    public async Task Reviewed_workflow_output_is_deduplicated_resolved_and_reopened_with_source_links()
    {
        await using var fixture = Fixture.Create();
        var template = FinanceAutonomyWorkflowTemplateCatalogue.Find(
            FinanceAutonomyWorkflowTemplateCodes.StaleCashEvidence)!;
        var run = await fixture.CreateRunAsync("read", fixture.Tool.Succeeded(), template: template);
        var command = new MaterializeFinanceAutonomyWorkflowOutcomeCommand(run.Id, run.Steps[0].Id,
            template.Code, FinanceAutonomyWorkflowOutcomeStates.Exception, "Cash evidence requires review.");

        var created = await fixture.Outcomes.MaterializeAsync(fixture.CompanyId, command, default);
        var duplicate = await fixture.Outcomes.MaterializeAsync(fixture.CompanyId, command, default);
        var resolved = await fixture.Outcomes.MaterializeAsync(fixture.CompanyId,
            command with { Outcome = FinanceAutonomyWorkflowOutcomeStates.Healthy }, default);
        var reopened = await fixture.Outcomes.MaterializeAsync(fixture.CompanyId, command, default);

        Assert.True(created.Created);
        Assert.True(duplicate.Duplicate);
        Assert.True(resolved.Resolved);
        Assert.True(reopened.Reopened);
        Assert.Equal(created.TaskId, reopened.TaskId);
        var task = Assert.Single(fixture.Db.WorkTasks.IgnoreQueryFilters());
        Assert.Equal(WorkTaskStatus.New, task.Status);
        Assert.Equal(run.Id.ToString(), task.InputPayload["runId"]!.GetValue<Guid>().ToString());
        Assert.Equal(run.PolicyVersion, task.InputPayload["policyVersion"]!.GetValue<string>());
        Assert.Equal(template.OwnerRole, task.InputPayload["ownerRole"]!.GetValue<string>());
        Assert.Equal(template.NextHumanAction.Sv, task.InputPayload["nextHumanActionSv"]!.GetValue<string>());
        Assert.NotNull(task.InputPayload["sources"]);
        Assert.Contains(fixture.Audit.Requests, request =>
            request.Action == AuditEventActions.FinanceAutonomyWorkflowOutcomeMaterialized &&
            request.Metadata?["disposition"] == "reopened");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Outcomes.MaterializeAsync(Guid.NewGuid(), command, default));
    }

    [Fact]
    public async Task Healthy_scheduled_work_records_no_action_without_creating_a_task()
    {
        await using var fixture = Fixture.Create();
        var template = FinanceAutonomyWorkflowTemplateCatalogue.Find(
            FinanceAutonomyWorkflowTemplateCodes.StaleCashEvidence)!;
        var run = await fixture.CreateRunAsync("read", fixture.Tool.Succeeded(), template: template);

        var result = await fixture.Outcomes.MaterializeAsync(fixture.CompanyId,
            new(run.Id, run.Steps[0].Id, template.Code, FinanceAutonomyWorkflowOutcomeStates.Healthy,
                "Cash evidence is current."), default);

        Assert.False(result.Created);
        Assert.Empty(fixture.Db.WorkTasks.IgnoreQueryFilters());
        Assert.Contains(fixture.Audit.Requests, request =>
            request.Action == AuditEventActions.FinanceAutonomyWorkflowOutcomeMaterialized &&
            request.Metadata?["disposition"] == "no_action_required");
    }

    [Fact]
    public async Task Template_aware_executor_materializes_one_review_task_before_completing_the_step()
    {
        await using var fixture = Fixture.Create();
        var template = FinanceAutonomyWorkflowTemplateCatalogue.Find(
            FinanceAutonomyWorkflowTemplateCodes.StaleCashEvidence)!;
        var toolResult = fixture.Tool.Succeeded(new Dictionary<string, JsonNode?>
        {
            ["status"] = ToolExecutionStatus.Executed.ToStorageValue(),
            ["staleEvidenceCount"] = 1
        });
        await fixture.CreateRunAsync("read", toolResult, template: template);

        var result = await fixture.Executor.ProcessBatchAsync(fixture.Clock.UtcNow, "workflow-worker", 10, default);

        Assert.Equal(1, result.Completed);
        var task = Assert.Single(fixture.Db.WorkTasks.IgnoreQueryFilters());
        Assert.Equal(WorkTaskStatus.Blocked, task.Status);
        Assert.Single(fixture.Db.AgentTaskCreationDedupeRecords.IgnoreQueryFilters());
        Assert.Equal("completed", (await fixture.Run()).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Guid? _runId;
        private Fixture(VirtualCompanyDbContext db, FinanceAutonomyRunService service,
            FinanceAutonomyExecutor executor, FinanceAutonomyWorkflowOutcomeService outcomes,
            NullAudit audit, MutablePolicy policy, MutableTimeProvider clock,
            FakeDurableTool tool, MemoryStorage storage, Guid companyId, Guid agentId, Guid actorId)
        {
            Db = db; Service = service; Executor = executor; Outcomes = outcomes; Audit = audit;
            Policy = policy; Clock = clock;
            Tool = tool; Storage = storage; CompanyId = companyId; AgentId = agentId; ActorId = actorId;
        }
        public VirtualCompanyDbContext Db { get; }
        public FinanceAutonomyRunService Service { get; }
        public FinanceAutonomyExecutor Executor { get; }
        public FinanceAutonomyWorkflowOutcomeService Outcomes { get; }
        public NullAudit Audit { get; }
        public MutablePolicy Policy { get; }
        public MutableTimeProvider Clock { get; }
        public FakeDurableTool Tool { get; }
        public MemoryStorage Storage { get; }
        public Guid CompanyId { get; }
        public Guid AgentId { get; }
        public Guid ActorId { get; }

        public static Fixture Create()
        {
            var companyId = Guid.NewGuid(); var agentId = Guid.NewGuid(); var actorId = Guid.NewGuid();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var policy = new MutablePolicy();
            var clock = new MutableTimeProvider(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
            var audit = new NullAudit();
            var service = new FinanceAutonomyRunService(db, policy,
                new ManagerMembership(companyId, actorId), audit, clock);
            var tool = new FakeDurableTool(); var storage = new MemoryStorage();
            var outcomes = new FinanceAutonomyWorkflowOutcomeService(db, audit, clock);
            var executor = new FinanceAutonomyExecutor(db, service, tool, outcomes, storage, null!);
            return new Fixture(db, service, executor, outcomes, audit, policy, clock, tool, storage, companyId, agentId, actorId);
        }

        public async Task<FinanceAutonomyRunDto> CreateRunAsync(string actionClass,
            ExecuteAgentToolResultDto result, int maximumAttempts = 3, bool objectArtifact = false,
            bool staleAuthoritative = false, FinanceAutonomyWorkflowTemplate? template = null)
        {
            Tool.Result = result;
            var selectedAction = template?.ActionClass ?? actionClass;
            var selectedTool = template?.ToolName ?? "get_cash_balance";
            var selectedTrigger = template is null ? FinanceAutonomyTriggers.ManualReview : FinanceAutonomyTriggers.Schedule;
            var selectedCapability = template?.CapabilityId ?? "daily_cash";
            var grantVersion = new FinanceAutonomyGrantVersion(Policy.GrantVersionId, CompanyId,
                Policy.GrantId, 1, FinanceAutonomyLevel.ReadMonitor,
                [selectedTrigger], [selectedAction], [selectedTool],
                10, null, 10, null, "UTC", "00:00", "23:59", 60, "none", "finance_manager",
                Clock.UtcNow, Clock.UtcNow.AddDays(30), "catalogue-v1", Hash("capability"),
                "authority-v1", Policy.AuthorityHash, ActorId, Clock.UtcNow, false);
            Db.FinanceAutonomyGrantVersions.Add(grantVersion);
            await Db.SaveChangesAsync();
            IReadOnlyList<FinanceAutonomyRunSourceDefinition> sources = objectArtifact
                ? new[] { new FinanceAutonomyRunSourceDefinition("object_artifact", "generated_report",
                    $"companies/{CompanyId:N}/finance/report.bin", "v1", Hash("expected"), "Generated report") }
                : staleAuthoritative
                    ? [new FinanceAutonomyRunSourceDefinition("authoritative_event", "cash_snapshot", "snapshot-1", "v1", Hash("source-v1"), "Cash")]
                    : [new FinanceAutonomyRunSourceDefinition("ledger", "cash_snapshot", "snapshot-1", "v1", Hash("source"), "Cash")];
            var step = new FinanceAutonomyRunPlanStepDefinition(
                template is null ? "step" : $"reviewed_template:{template.Code}", selectedAction, selectedTool, [],
                Hash("requested"), "Bounded tool call", maximumAttempts, RequestPayload:
                template?.RequestPayload ?? new Dictionary<string, JsonNode?> { ["idempotencyKey"] = null });
            var run = await Service.CreateOrCoalesceAsync(CompanyId,
                new(AgentId, selectedCapability, selectedTrigger, Guid.NewGuid().ToString("N"),
                    Clock.UtcNow, Clock.UtcNow.AddHours(1), null, null, Guid.NewGuid().ToString("N"),
                    "correlation", Clock.UtcNow, new Dictionary<string, string?> { ["snapshot"] = "fresh" },
                    template?.Version ?? "plan-v1", [step], new Dictionary<string, decimal> { ["maximumRecords"] = 10 }, sources, 1), default);
            if (staleAuthoritative)
            {
                Db.FinanceAutonomyTriggerEvents.Add(new FinanceAutonomyTriggerEvent(Guid.NewGuid(), CompanyId,
                    Guid.NewGuid(), "cash_snapshot_changed", "event-2", "v2", "cash_snapshot", "snapshot-1",
                    Clock.UtcNow.AddMinutes(1), Clock.UtcNow.AddMinutes(1), "snapshot-1", Hash("source-v2"),
                    "New cash snapshot", "event-correlation", Clock.UtcNow.AddMinutes(1)));
                await Db.SaveChangesAsync();
            }
            _runId = run.Id;
            return run;
        }

        public Task<FinanceAutonomyRunDto> Run() => Service.GetAsync(CompanyId, _runId!.Value, default);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeDurableTool : IFinanceDurableToolExecutionService
    {
        public ExecuteAgentToolResultDto Result { get; set; } = ResultFor(ToolExecutionStatus.Executed, true);
        public List<ExecuteAgentToolCommand> Commands { get; } = [];
        public List<Guid> Actors { get; } = [];
        public Task<ExecuteAgentToolResultDto> ExecuteDurableAsync(Guid companyId, Guid agentId,
            Guid persistedActorUserId, ExecuteAgentToolCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command); Actors.Add(persistedActorUserId); return Task.FromResult(Result);
        }
        public ExecuteAgentToolResultDto Succeeded(IReadOnlyDictionary<string, JsonNode?>? result = null) =>
            ResultFor(ToolExecutionStatus.Executed, true, result);
        public ExecuteAgentToolResultDto Failed() => ResultFor(ToolExecutionStatus.Failed, false);
        private static ExecuteAgentToolResultDto ResultFor(ToolExecutionStatus status, bool success,
            IReadOnlyDictionary<string, JsonNode?>? result = null)
        {
            var value = status.ToStorageValue();
            return new(Guid.NewGuid(), value, null,
                new(PolicyDecisionOutcomeValues.Allow, [], "Allowed", "level1", "read", null, false, []),
                result?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase) ??
                new Dictionary<string, JsonNode?> { ["status"] = value, ["success"] = success },
                success ? "Tool completed." : "Temporary tool failure.");
        }
    }

    private sealed class MemoryStorage : ICompanyDocumentStorage
    {
        public byte[] Content { get; set; } = Encoding.UTF8.GetBytes("expected");
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(Content, writable: false));
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MutablePolicy : IFinanceAutonomyPolicyEvaluator
    {
        public Guid GrantId { get; } = Guid.NewGuid();
        public Guid GrantVersionId { get; } = Guid.NewGuid();
        public string AuthorityHash { get; } = Hash("authority");
        public Task<FinanceAutonomyDecisionDto> EvaluateAsync(FinanceAutonomyEvaluationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceAutonomyDecisionDto(true, FinanceAutonomyDecisionReasonCodes.Allowed,
                "Allowed", GrantId, GrantVersionId, 1, FinanceAutonomyLevels.ReadMonitor, false, false,
                100, 100, null, FinanceAutonomyPolicyVersions.V1, "catalogue-v1", "authority-v1",
                AuthorityHash, DateTime.UtcNow));
    }

    private sealed class ManagerMembership(Guid companyId, Guid actorId) : ICompanyMembershipContextResolver
    {
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) => ResolveAsync(companyId, cancellationToken);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid requestedCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedCompanyMembershipContext?>(requestedCompanyId == companyId
                ? new(Guid.NewGuid(), companyId, actorId, "Executor test", CompanyMembershipRole.Owner,
                    CompanyMembershipStatus.Active, "UTC", "SEK") : null);
    }

    private sealed class NullAudit : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Requests { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTime value) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = value;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
