using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceConversationRunPersistenceTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Migration_adds_only_tenant_scoped_durable_run_tables_and_recovery_indexes()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddDurableFinanceConversationRuns();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Equal(new[]
        {
            "finance_conversation_run_attempts", "finance_conversation_run_revisions",
            "finance_conversation_run_steps", "finance_conversation_runs"
        }, tables.Keys.OrderBy(x => x).ToArray());
        Assert.All(tables.Values, table => Assert.Contains(table.Columns,
            column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_conversation_runs" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "agent_id", "idempotency_key"]));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_conversation_run_steps" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "business_idempotency_key"]));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_conversation_runs" &&
            index.Columns.SequenceEqual(["status", "next_attempt_at", "lease_expires_at"]));
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }

    [Fact]
    public void Lifecycle_enforces_leases_bounded_attempts_cancellation_and_retention_redaction()
    {
        var companyId = Guid.NewGuid();
        var run = CreateRun(companyId);
        var step = CreateStep(companyId, run.Id, "read", FinanceConversationRunStepStatuses.Ready);
        run.Steps.Add(step);

        Assert.True(run.TryClaim("worker-a", Now, TimeSpan.FromMinutes(1)));
        Assert.False(run.TryClaim("worker-b", Now.AddSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.True(run.TryClaim("worker-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1)));
        run.ScheduleRetry("worker-b", Now.AddMinutes(2), Now.AddMinutes(3));
        Assert.Equal(1, run.AttemptCount);
        Assert.Equal(Now.AddMinutes(3), run.NextAttemptUtc);

        for (var attempt = 0; attempt < step.MaxAttempts; attempt++)
        {
            Assert.True(step.TryClaim("step-worker", Now.AddMinutes(4 + attempt), TimeSpan.FromSeconds(30)));
            step.ScheduleRetry("transient", "The bounded attempt can be retried safely.",
                Now.AddMinutes(5 + attempt), Now.AddMinutes(4 + attempt));
        }
        Assert.False(step.TryClaim("step-worker", Now.AddMinutes(20), TimeSpan.FromSeconds(30)));

        run.Cancel(Guid.NewGuid(), "Operator cancelled before another effect.", Now.AddMinutes(21));
        step.Cancel(Now.AddMinutes(21));
        Assert.Equal(FinanceConversationRunStatuses.Cancelled, run.Status);
        Assert.Equal(FinanceConversationRunStepStatuses.Cancelled, step.Status);
        Assert.Contains("not undone", run.SafeSummary, StringComparison.OrdinalIgnoreCase);

        var identity = step.Id;
        var businessKey = step.BusinessIdempotencyKey;
        var argumentHash = step.NormalizedArgumentsHash;
        step.Redact(Now.AddDays(91));
        run.MarkRedacted(Now.AddDays(91));
        Assert.Equal(identity, step.Id);
        Assert.Equal(businessKey, step.BusinessIdempotencyKey);
        Assert.Equal(argumentHash, step.NormalizedArgumentsHash);
        Assert.Equal("{}", step.NormalizedArgumentsJson);
        Assert.Equal("[]", step.EvidenceReferencesJson);
        Assert.NotNull(run.RedactedUtc);
    }

    [Fact]
    public void Approval_restart_and_failed_branch_remain_traceable_without_reinvocation_state()
    {
        var companyId = Guid.NewGuid();
        var run = CreateRun(companyId);
        var approvalId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var approved = CreateStep(companyId, run.Id, "approved", FinanceConversationRunStepStatuses.Ready);
        var failed = CreateStep(companyId, run.Id, "failed", FinanceConversationRunStepStatuses.Ready, "[\"approved\"]");
        run.Steps.Add(approved);
        run.Steps.Add(failed);

        approved.AwaitApproval(executionId, approvalId, "{\"outcome\":\"approval_required\"}", Now);
        Assert.Equal(executionId, approved.ToolExecutionAttemptId);
        Assert.Equal(approvalId, approved.ApprovalRequestId);
        approved.Complete(executionId, "{\"journalId\":\"safe-reference\"}", "{\"outcome\":\"allowed\"}", Now.AddMinutes(1));
        failed.Fail("provider_rejected", "The provider rejected this independent branch.", Now.AddMinutes(2));
        run.SetState(FinanceConversationRunStatuses.PartiallyCompleted,
            "One durable branch completed and one failed with retained references.", Now.AddMinutes(2),
            "finance_run_partially_completed");

        Assert.Equal(executionId, approved.ToolExecutionAttemptId);
        Assert.Equal(FinanceConversationRunStatuses.PartiallyCompleted, run.Status);
        Assert.Equal("provider_rejected", failed.FailureCode);
        Assert.NotNull(failed.CompletedUtc);
    }

    [Fact]
    public async Task Sqlite_persistence_enforces_tenant_filters_uniqueness_concurrency_and_rollback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var runA = CreateRun(companyA);
        var runB = CreateRun(companyB);

        await using (var setup = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(null)))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Companies.AddRange(new Company(companyA, "Tenant A"), new Company(companyB, "Tenant B"));
            setup.FinanceConversationRuns.AddRange(runA, runB);
            await setup.SaveChangesAsync();
        }

        await using (var tenantA = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(companyA)))
        {
            var visible = await tenantA.FinanceConversationRuns.SingleAsync();
            Assert.Equal(runA.Id, visible.Id);
            Assert.NotNull(tenantA.Model.FindEntityType(typeof(FinanceConversationRun))!.GetQueryFilter());
            Assert.True(tenantA.Model.FindEntityType(typeof(FinanceConversationRun))!
                .FindProperty(nameof(FinanceConversationRun.Version))!.IsConcurrencyToken);
        }

        await using var first = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(companyA));
        await using var second = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(companyA));
        var firstCopy = await first.FinanceConversationRuns.SingleAsync(x => x.Id == runA.Id);
        var secondCopy = await second.FinanceConversationRuns.SingleAsync(x => x.Id == runA.Id);
        firstCopy.SetState(FinanceConversationRunStatuses.AwaitingApproval, "Waiting for approval.", Now.AddHours(1));
        await first.SaveChangesAsync();
        secondCopy.SetState(FinanceConversationRunStatuses.Failed, "A stale worker cannot overwrite the run.", Now.AddHours(1));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var rollback = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(companyA));
        await using (var transaction = await rollback.Database.BeginTransactionAsync())
        {
            rollback.FinanceConversationRuns.Add(CreateRun(companyA));
            await rollback.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        rollback.ChangeTracker.Clear();
        Assert.Equal(1, await rollback.FinanceConversationRuns.CountAsync());
    }

    [Fact]
    public async Task Restart_recovers_the_persisted_tool_attempt_without_invoking_the_effect_again()
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase($"finance-run-restart-{Guid.NewGuid():N}").Options;
        await using var db = new VirtualCompanyDbContext(options, new TestCompanyContextAccessor(null));
        var companyId = Guid.NewGuid();
        var run = CreateRun(companyId);
        var step = CreateStep(companyId, run.Id, "read", FinanceConversationRunStepStatuses.Ready);
        run.Steps.Add(step);
        run.SetState(FinanceConversationRunStatuses.Ready, "Ready for durable continuation.", Now);
        var stableCorrelation = "fcr:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(step.BusinessIdempotencyKey))).ToLowerInvariant();
        var prior = new ToolExecutionAttempt(Guid.NewGuid(), companyId, run.AgentId, step.ToolName,
            ToolActionType.Read, step.Scope, new Dictionary<string, JsonNode?>(), correlationId: stableCorrelation,
            startedAtUtc: Now, toolVersion: step.ToolVersion);
        prior.MarkExecuted(new Dictionary<string, JsonNode?> { ["outcome"] = "allowed" },
            new Dictionary<string, JsonNode?> { ["recordId"] = "retained-reference" }, Now.AddMinutes(1));
        db.FinanceConversationRuns.Add(run);
        db.ToolExecutionAttempts.Add(prior);
        await db.SaveChangesAsync();

        var executor = new NeverInvokeExecutor();
        var processor = new FinanceConversationRunProcessor(db, executor, new MatchingAuthorityResolver(),
            new EmptyToolRegistry(), new RecordingAuditWriter(), new FixedClock(Now.AddMinutes(2)),
            Options.Create(new FinanceConversationRunOptions { LeaseSeconds = 30 }),
            NullLogger<FinanceConversationRunProcessor>.Instance);

        var result = await processor.RunOnceAsync(1, CancellationToken.None);

        Assert.Equal(1, result.CompletedRuns);
        Assert.Equal(0, executor.CallCount);
        db.ChangeTracker.Clear();
        var stored = await db.FinanceConversationRuns.IgnoreQueryFilters().Include(x => x.Steps).SingleAsync();
        Assert.Equal(FinanceConversationRunStatuses.Completed, stored.Status);
        Assert.Equal(prior.Id, Assert.Single(stored.Steps).ToolExecutionAttemptId);
    }

    private static FinanceConversationRun CreateRun(Guid companyId) => new(
        Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"),
        new string('a', 64), $"run-{Guid.NewGuid():N}", "authority-v1", new string('b', 64),
        "planning-v1", new string('c', 64), Now, Now.AddDays(90));

    private static FinanceConversationRunStep CreateStep(Guid companyId, Guid runId, string key, string status,
        string dependencies = "[]") => new(Guid.NewGuid(), companyId, runId, key,
        key == "read" || key == "approved" ? 1 : 2, dependencies, "finance.test", "1.0.0", "read",
        "finance:test", "{\"accountId\":\"safe\",\"secret\":\"[redacted]\"}", new string('d', 64),
        "Read an authoritative Finance record.", "[{\"sourceId\":\"audit-ref\"}]",
        $"business:{companyId:N}:{runId:N}:{key}", status, Now);

    private sealed class TestCompanyContextAccessor(Guid? companyId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => null;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class MatchingAuthorityResolver : IAgentEffectiveAuthorityResolver
    {
        public Task<AgentEffectiveAuthorityDto> ResolveAsync(Guid companyId, Guid agentId,
            CancellationToken cancellationToken) => Task.FromResult(new AgentEffectiveAuthorityDto(
            companyId, agentId, "Finance agent", "Finance", "active", true, "supervised", "authority-v1",
            new string('b', 64), [], [], [], Now));
    }

    private sealed class NeverInvokeExecutor : IFinanceDurableToolExecutionService
    {
        public int CallCount { get; private set; }
        public Task<ExecuteAgentToolResultDto> ExecuteDurableAsync(Guid companyId, Guid agentId,
            Guid persistedActorUserId, ExecuteAgentToolCommand command, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("A persisted attempt must be recovered instead of invoked again.");
        }
    }

    private sealed class EmptyToolRegistry : ICompanyToolRegistry
    {
        public bool TryGetTool(string toolName, out TrustedToolRegistration registration)
        { registration = null!; return false; }
        public IReadOnlyList<TrustedToolRegistration> ListTools() => [];
        public bool TryGetToolDefinition(string toolName, out ToolDefinitionManifest definition)
        { definition = null!; return false; }
        public IReadOnlyList<ToolDefinitionManifest> ListToolDefinitions() => [];
    }

    private sealed class RecordingAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        { Events.Add(auditEvent); return Task.CompletedTask; }
    }
}
