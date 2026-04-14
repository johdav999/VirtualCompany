using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TriggerExecutionServiceTests
{
    [Fact]
    public async Task Policy_denial_blocks_orchestration_and_persists_denial_audit()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var dispatcher = new CountingDispatcher();
        var service = CreateService(
            dbContext,
            new StubPolicyChecker(TriggerExecutionPolicyDecision.Deny("Agent autonomy policy denied execution.")),
            dispatcher);
        var workItem = CreateWorkItem(companyId, agentId);

        var outcome = await service.EvaluateAndDispatchAsync(workItem, maxRetryAttempts: 3, CancellationToken.None);

        Assert.Equal(TriggerExecutionAttemptStatus.Blocked, outcome);
        Assert.Equal(0, dispatcher.DispatchCount);

        var attempt = await dbContext.TriggerExecutionAttempts
            .IgnoreQueryFilters()
            .SingleAsync(x => x.IdempotencyKey == workItem.IdempotencyKey);
        Assert.Equal(TriggerExecutionAttemptStatus.Blocked, attempt.Status);
        Assert.Equal("Agent autonomy policy denied execution.", attempt.DenialReason);

        var audit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Action == AuditEventActions.TriggerExecutionAttemptBlocked);
        Assert.Equal(companyId, audit.CompanyId);
        Assert.Equal(workItem.CorrelationId, audit.CorrelationId);
        Assert.Equal(AuditEventOutcomes.Denied, audit.Outcome);
        Assert.Equal(workItem.TriggerId.ToString("N"), audit.Metadata["triggerId"]);
        Assert.Equal(companyId.ToString("N"), audit.Metadata["companyId"]);
        Assert.Equal(agentId.ToString("N"), audit.Metadata["agentId"]);
        Assert.Equal(TriggerExecutionAttemptStatus.Blocked.ToStorageValue(), audit.Metadata["executionStatus"]);
        Assert.Equal(workItem.CorrelationId, audit.Metadata["correlationId"]);
        Assert.Equal("Agent autonomy policy denied execution.", audit.Metadata["denialReason"]);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_does_not_dispatch_twice_and_persists_duplicate_audit()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var dispatcher = new CountingDispatcher();
        var service = CreateService(
            dbContext,
            new StubPolicyChecker(TriggerExecutionPolicyDecision.Allow()),
            dispatcher);
        var workItem = CreateWorkItem(companyId, agentId);

        var first = await service.EvaluateAndDispatchAsync(workItem, maxRetryAttempts: 3, CancellationToken.None);
        var second = await service.EvaluateAndDispatchAsync(workItem, maxRetryAttempts: 3, CancellationToken.None);

        Assert.Equal(TriggerExecutionAttemptStatus.Dispatched, first);
        Assert.Equal(TriggerExecutionAttemptStatus.DuplicateSkipped, second);
        Assert.Equal(1, dispatcher.DispatchCount);
        Assert.Equal(1, await dbContext.TriggerExecutionAttempts.IgnoreQueryFilters().CountAsync());

        var duplicateAudit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Action == AuditEventActions.TriggerExecutionAttemptDuplicateSkipped);
        Assert.Equal(companyId, duplicateAudit.CompanyId);
        Assert.Equal(workItem.CorrelationId, duplicateAudit.CorrelationId);
        Assert.Equal(workItem.IdempotencyKey, duplicateAudit.Metadata["idempotencyKey"]);
        Assert.Equal(TriggerExecutionAttemptStatus.Dispatched.ToStorageValue(), duplicateAudit.Metadata["executionStatus"]);

        var auditActions = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Action)
            .ToListAsync();
        Assert.Contains(AuditEventActions.TriggerExecutionAttemptStarted, auditActions);
        Assert.Contains(AuditEventActions.TriggerOrchestrationStartRequested, auditActions);
        Assert.Contains(AuditEventActions.TriggerExecutionAttemptDispatched, auditActions);
        Assert.Contains(AuditEventActions.TriggerExecutionAttemptDuplicateSkipped, auditActions);

        var startRequestedAudit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Action == AuditEventActions.TriggerOrchestrationStartRequested);
        Assert.Equal(workItem.CorrelationId, startRequestedAudit.Metadata["correlationId"]);
    }

    [Fact]
    public async Task Transient_dispatch_failure_records_failed_audit_and_retry_reuses_idempotency_key()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var dispatcher = new CountingDispatcher(failFirstDispatch: true);
        var service = CreateService(
            dbContext,
            new StubPolicyChecker(TriggerExecutionPolicyDecision.Allow()),
            dispatcher);
        var workItem = CreateWorkItem(companyId, agentId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EvaluateAndDispatchAsync(workItem, maxRetryAttempts: 3, CancellationToken.None));

        var failedAttempt = await dbContext.TriggerExecutionAttempts
            .IgnoreQueryFilters()
            .SingleAsync(x => x.IdempotencyKey == workItem.IdempotencyKey);
        Assert.Equal(TriggerExecutionAttemptStatus.RetryScheduled, failedAttempt.Status);
        Assert.Equal("Transient orchestration outage.", failedAttempt.FailureDetails);
        Assert.NotNull(failedAttempt.NextRetryUtc);

        var failedAudit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Action == AuditEventActions.TriggerExecutionAttemptFailed);
        Assert.Equal(companyId, failedAudit.CompanyId);
        Assert.Equal(AuditEventOutcomes.Failed, failedAudit.Outcome);
        Assert.Equal(TriggerExecutionAttemptStatus.RetryScheduled.ToStorageValue(), failedAudit.Metadata["executionStatus"]);
        Assert.Equal("Transient orchestration outage.", failedAudit.Metadata["failureDetails"]);

        var retryDeferred = await service.EvaluateAndDispatchAsync(workItem, maxRetryAttempts: 3, CancellationToken.None);
        Assert.Equal(TriggerExecutionAttemptStatus.RetryScheduled, retryDeferred);
        Assert.Equal(1, dispatcher.DispatchCount);

        failedAttempt.MarkRetried(failedAttempt.RetryAttemptCount + 1);
        await dbContext.SaveChangesAsync();
        var retryOutcome = await service.EvaluateAndDispatchAsync(workItem, maxRetryAttempts: 3, CancellationToken.None);

        Assert.Equal(TriggerExecutionAttemptStatus.Dispatched, retryOutcome);
        Assert.Equal(2, dispatcher.DispatchCount);
        Assert.Equal(1, await dbContext.TriggerExecutionAttempts.IgnoreQueryFilters().CountAsync());

        var retriedAttempt = await dbContext.TriggerExecutionAttempts
            .IgnoreQueryFilters()
            .SingleAsync(x => x.IdempotencyKey == workItem.IdempotencyKey);
        Assert.Equal(TriggerExecutionAttemptStatus.Dispatched, retriedAttempt.Status);
        Assert.True(retriedAttempt.RetryAttemptCount > 1);

        Assert.True(await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Action == AuditEventActions.TriggerExecutionAttemptRetried));
        Assert.True(await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Action == AuditEventActions.TriggerExecutionAttemptDispatched));
    }

    [Fact]
    public async Task Transient_failure_after_retry_exhaustion_dead_letters_attempt_and_records_audit()
    {
        await using var connection = CreateOpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await SeedCompanyAndAgentAsync(dbContext, companyId, agentId);
        var dispatcher = new CountingDispatcher(alwaysFail: true);
        var service = CreateService(
            dbContext,
            new StubPolicyChecker(TriggerExecutionPolicyDecision.Allow()),
            dispatcher);
        var workItem = CreateWorkItem(companyId, agentId);

        var outcome = await service.EvaluateAndDispatchAsync(workItem, maxRetryAttempts: 1, CancellationToken.None);

        Assert.Equal(TriggerExecutionAttemptStatus.DeadLettered, outcome);
        Assert.Equal(1, dispatcher.DispatchCount);

        var attempt = await dbContext.TriggerExecutionAttempts
            .IgnoreQueryFilters()
            .SingleAsync(x => x.IdempotencyKey == workItem.IdempotencyKey);
        Assert.Equal(TriggerExecutionAttemptStatus.DeadLettered, attempt.Status);
        Assert.Equal("Transient orchestration outage.", attempt.FailureDetails);
        Assert.Null(attempt.NextRetryUtc);

        var audit = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Action == AuditEventActions.TriggerExecutionAttemptDeadLettered);
        Assert.Equal(AuditEventOutcomes.Failed, audit.Outcome);
        Assert.Equal(TriggerExecutionAttemptStatus.DeadLettered.ToStorageValue(), audit.Metadata["executionStatus"]);
        Assert.Equal("true", audit.Metadata["deadLettered"]);
    }

    private static TriggerExecutionService CreateService(
        VirtualCompanyDbContext dbContext,
        ITriggerExecutionPolicyChecker policyChecker,
        ITriggerOrchestrationDispatcher dispatcher) =>
        new(
            new EfTriggerExecutionAttemptRepository(dbContext),
            policyChecker,
            dispatcher,
            new TriggerAuditEventWriter(new AuditEventWriter(dbContext)),
            dbContext);

    private static TriggerExecutionWorkItem CreateWorkItem(Guid companyId, Guid agentId)
    {
        var triggerId = Guid.NewGuid();
        var occurrenceUtc = new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc);
        var idempotencyKey = TriggerExecutionIdempotency.ForScheduledAgentTrigger(companyId, triggerId, occurrenceUtc);

        return new TriggerExecutionWorkItem(
            companyId,
            triggerId,
            TriggerExecutionTypes.AgentScheduled,
            agentId,
            occurrenceUtc,
            TriggerExecutionIdempotency.CorrelationFromIdempotencyKey(idempotencyKey),
            idempotencyKey,
            []);
    }

    private static SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static async Task<VirtualCompanyDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new VirtualCompanyDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private static async Task SeedCompanyAndAgentAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        Guid agentId)
    {
        dbContext.Companies.Add(new Company(companyId, "Trigger Execution Test Company"));
        dbContext.Agents.Add(new Agent(
            agentId,
            companyId,
            "trigger-agent",
            "Trigger Agent",
            "Operations Agent",
            "Operations",
            null,
            AgentSeniority.Mid,
            AgentStatus.Active,
            AgentAutonomyLevel.Assisted));
        await dbContext.SaveChangesAsync();
    }

    private sealed class StubPolicyChecker : ITriggerExecutionPolicyChecker
    {
        private readonly TriggerExecutionPolicyDecision _decision;

        public StubPolicyChecker(TriggerExecutionPolicyDecision decision)
        {
            _decision = decision;
        }

        public Task<TriggerExecutionPolicyDecision> CheckAsync(
            TriggerExecutionWorkItem workItem,
            CancellationToken cancellationToken) =>
            Task.FromResult(_decision);
    }

    private sealed class CountingDispatcher : ITriggerOrchestrationDispatcher
    {
        private readonly bool _failFirstDispatch;
        private readonly bool _alwaysFail;

        public CountingDispatcher(bool failFirstDispatch = false, bool alwaysFail = false)
        {
            _failFirstDispatch = failFirstDispatch;
            _alwaysFail = alwaysFail;
        }

        public int DispatchCount { get; private set; }

        public Task<TriggerExecutionDispatchResult> DispatchAsync(
            TriggerExecutionWorkItem workItem,
            CancellationToken cancellationToken)
        {
            DispatchCount++;
            if (_alwaysFail || _failFirstDispatch && DispatchCount == 1)
            {
                throw new InvalidOperationException("Transient orchestration outage.");
            }

            return Task.FromResult(new TriggerExecutionDispatchResult(
                AuditTargetTypes.WorkTask,
                Guid.NewGuid().ToString("N")));
        }
    }
}
