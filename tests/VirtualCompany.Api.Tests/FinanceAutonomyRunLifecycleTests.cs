using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyRunLifecycleTests
{
    [Fact]
    public void All_required_run_states_have_stable_round_trip_storage_values()
    {
        var values = Enum.GetValues<FinanceAutonomyRunStatus>();
        Assert.Equal(14, values.Length);
        Assert.All(values, value => Assert.Equal(value,
            FinanceAutonomyRunEnumValues.ParseRunStatus(value.ToStorageValue())));
    }

    [Theory]
    [InlineData("approve", ApprovalRequestStatus.Approved)]
    [InlineData("reject", ApprovalRequestStatus.Rejected)]
    [InlineData("request_changes", ApprovalRequestStatus.ChangesRequested)]
    [InlineData("cancel", ApprovalRequestStatus.Cancelled)]
    [InlineData("expire", ApprovalRequestStatus.Expired)]
    [InlineData("revoke", ApprovalRequestStatus.Revoked)]
    [InlineData("supersede", ApprovalRequestStatus.Superseded)]
    public void Approval_lifecycle_has_an_explicit_terminal_state(string decision, ApprovalRequestStatus expected)
    {
        var approval = new ApprovalRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "get_cash_balance", ToolActionType.Read, "finance_approver",
            new Dictionary<string, JsonNode?> { ["binding"] = "exact-action" });
        var reviewer = Guid.NewGuid();

        switch (decision)
        {
            case "approve": approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, reviewer, "Approved."); break;
            case "reject": approval.RejectCurrentStep(approval.CurrentActionableStep!.Id, reviewer, "Rejected."); break;
            case "request_changes": approval.MarkChangesRequested("Narrow the scope."); break;
            case "cancel": approval.MarkCancelled("Cancelled."); break;
            case "expire": approval.MarkExpired("Expired."); break;
            case "revoke": approval.MarkRevoked("Revoked."); break;
            case "supersede": approval.MarkSuperseded("Superseded."); break;
        }

        Assert.Equal(expected, approval.Status);
        Assert.True(approval.IsTerminal);
        Assert.False(approval.CanExecuteGuardedAction && expected != ApprovalRequestStatus.Approved);
    }

    [Fact]
    public async Task Duplicate_trigger_window_and_event_version_coalesce_to_one_logical_run()
    {
        await using var fixture = Fixture.Create();
        var command = fixture.Command(authoritativeEventId: "transaction-42", authoritativeEventVersion: "v3",
            trigger: FinanceAutonomyTriggers.BusinessEvent);

        var first = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, command, default);
        var duplicate = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId,
            command with { IdempotencyKey = "a-different-request-id", CorrelationId = "another-correlation" }, default);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal("queued", first.Status);
        Assert.Equal(3, first.History.Count);
        Assert.Equal(1, await fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters().CountAsync());
        Assert.Equal(first.EvidenceHash, first.Steps[0].EvidenceHash);
        Assert.Contains(fixture.Audit.Events, x => x.Action == AuditEventActions.FinanceAutonomyRunCreated);
    }

    [Fact]
    public async Task Dependencies_leases_and_restart_recovery_do_not_repeat_completed_effects()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(twoSteps: true), default);
        var first = run.Steps[0];
        var second = run.Steps[1];

        Assert.Null(await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, second.Id, "worker-b", "lease-b", 30, run.EvidenceHash), default));
        var lease = await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, first.Id, "worker-a", "lease-a", 30, run.EvidenceHash), default);
        Assert.NotNull(lease);
        Assert.Null(await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, first.Id, "worker-b", "lease-b", 30, run.EvidenceHash), default));

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        var recovered = await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, first.Id, "worker-b", "lease-recovery", 30, run.EvidenceHash), default);
        Assert.NotNull(recovered);
        Assert.Equal(2, recovered.AttemptNumber);

        var afterFirst = await fixture.Service.CompleteStepAsync(fixture.CompanyId,
            new(run.Id, first.Id, "lease-recovery", null, Hash("actual-1"), "internal_effect", "Draft stored"), default);
        Assert.True(afterFirst.HasCompletedEffects);
        Assert.Equal("queued", afterFirst.Status);
        Assert.Null(await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, first.Id, "worker-c", "lease-c", 30, run.EvidenceHash), default));
        Assert.NotNull(await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, second.Id, "worker-c", "lease-next", 30, run.EvidenceHash), default));
    }

    [Fact]
    public async Task Changed_evidence_or_policy_blocks_pending_steps_before_execution()
    {
        await using var fixture = Fixture.Create();
        var evidenceRun = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(), default);

        Assert.Null(await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(evidenceRun.Id, evidenceRun.Steps[0].Id, "worker", "lease", 30, Hash("new-evidence")), default));
        var evidenceBlocked = await fixture.Service.GetAsync(fixture.CompanyId, evidenceRun.Id, default);
        Assert.Equal("blocked", evidenceBlocked.Status);
        Assert.Equal(FinanceAutonomyRunReasonCodes.EvidenceChanged, evidenceBlocked.ReasonCode);

        var policyRun = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId,
            fixture.Command() with { TriggerKey = "policy-change", IdempotencyKey = "policy-change" }, default);
        fixture.Policy.GrantVersionId = Guid.NewGuid();
        Assert.Null(await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(policyRun.Id, policyRun.Steps[0].Id, "worker", "lease-2", 30, policyRun.EvidenceHash), default));
        var policyBlocked = await fixture.Service.GetAsync(fixture.CompanyId, policyRun.Id, default);
        Assert.Equal(FinanceAutonomyRunReasonCodes.PolicyChanged, policyBlocked.ReasonCode);
    }

    [Fact]
    public async Task Cancellation_preserves_completed_effects_and_retention_preserves_hashes_and_links()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(twoSteps: true), default);
        var lease = await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, run.Steps[0].Id, "worker", "lease", 30, run.EvidenceHash), default);
        Assert.NotNull(lease);
        run = await fixture.Service.CompleteStepAsync(fixture.CompanyId,
            new(run.Id, run.Steps[0].Id, "lease", null, Hash("effect"), "internal_effect", "Created draft"), default);

        var cancelled = await fixture.Service.CancelAsync(fixture.CompanyId, run.Id,
            new("Operator stopped remaining work", run.Version), default);
        Assert.True(cancelled.HasCompletedEffects);
        Assert.Equal("completed", cancelled.Steps[0].Status);
        Assert.Contains("not rolled back", cancelled.SafeSummary, StringComparison.OrdinalIgnoreCase);
        var sourceHash = Assert.Single(cancelled.Sources).ContentHash;
        var redacted = await fixture.Service.RedactAsync(fixture.CompanyId, run.Id,
            new("Retention period elapsed", cancelled.Version), default);
        Assert.NotNull(redacted.SensitiveContentRedactedUtc);
        Assert.Equal(sourceHash, Assert.Single(redacted.Sources).ContentHash);
        Assert.Null(Assert.Single(redacted.Sources).SafeLabel);
        Assert.Equal(cancelled.EvidenceHash, redacted.EvidenceHash);
        Assert.Equal(cancelled.PlanHash, redacted.PlanHash);
    }

    [Fact]
    public async Task Operator_replay_requires_an_explicit_checkpoint_and_links_the_new_run()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(replayPermitted: true), default);
        var lease = await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, run.Steps[0].Id, "worker", "lease", 30, run.EvidenceHash), default);
        Assert.NotNull(lease);
        run = await fixture.Service.CompleteStepAsync(fixture.CompanyId,
            new(run.Id, run.Steps[0].Id, "lease", null, Hash("actual"), "no_effect", "Analysis complete"), default);

        var replay = await fixture.Service.ReplayAsync(fixture.CompanyId, run.Id,
            new(run.Steps[0].Id, "operator-replay-1", "replay-correlation", "Replay reviewed checkpoint"), default);
        Assert.NotEqual(run.Id, replay.Id);
        Assert.Equal(run.Id, replay.ReplayOfRunId);
        Assert.Equal(run.Steps[0].Id, replay.ReplayCheckpointStepId);
        Assert.Equal(run.Steps[0].Id, replay.Steps[0].ReplayOfStepId);

        var prohibited = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId,
            fixture.Command() with { TriggerKey = "not-replayable", IdempotencyKey = "not-replayable" }, default);
        prohibited = await fixture.Service.CancelAsync(fixture.CompanyId, prohibited.Id,
            new("Stop", prohibited.Version), default);
        await Assert.ThrowsAsync<FinanceAutonomyRunValidationException>(() => fixture.Service.ReplayAsync(
            fixture.CompanyId, prohibited.Id,
            new(prohibited.Steps[0].Id, "bad-replay", "bad-correlation", "Not permitted"), default));
    }

    [Fact]
    public async Task Supersession_is_terminal_audited_and_does_not_rewrite_pending_steps_as_completed()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(twoSteps: true), default);

        var superseded = await fixture.Service.SupersedeAsync(fixture.CompanyId, run.Id,
            new("A newer authoritative event version replaced this run", run.Version), default);

        Assert.Equal("superseded", superseded.Status);
        Assert.All(superseded.Steps, step => Assert.Equal("superseded", step.Status));
        Assert.Contains(superseded.History, x => x.ReasonCode == FinanceAutonomyRunReasonCodes.Superseded);
        Assert.Contains(fixture.Audit.Events, x => x.Action == AuditEventActions.FinanceAutonomyRunSuperseded);
    }

    [Fact]
    public async Task Queries_and_link_validation_are_company_scoped()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(), default);
        var otherCompany = Guid.NewGuid();
        Assert.Empty((await fixture.Service.ListAsync(otherCompany, new(), default)).Items);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.GetAsync(otherCompany, run.Id, default));

        var foreignTask = new WorkTask(Guid.NewGuid(), otherCompany, "finance_review", "Foreign", "Foreign task",
            WorkTaskPriority.Normal, null, null, AuditActorTypes.User, Guid.NewGuid(), status: WorkTaskStatus.New);
        fixture.Db.WorkTasks.Add(foreignTask);
        await fixture.Db.SaveChangesAsync();
        var command = fixture.Command() with { OriginatingTaskId = foreignTask.Id, TriggerKey = "foreign-link" };
        await Assert.ThrowsAsync<FinanceAutonomyRunValidationException>(() =>
            fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, command, default));
    }

    [Fact]
    public void Run_API_separates_read_access_from_operator_mutations()
    {
        var controller = typeof(FinanceAutonomyRunsController);
        Assert.Equal(CompanyPolicies.FinanceView,
            Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
        foreach (var method in new[] { "Create", "Transition", "BindApproval", "Reconcile", "Cancel", "Supersede", "Redact", "Replay", "Narrow" })
            Assert.Contains(controller.GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.CompanyManager);
    }

    [Fact]
    public async Task Approval_resolution_keeps_dependents_paused_and_only_successful_continuation_requeues_them()
    {
        await using var rejectedFixture = Fixture.Create();
        var rejected = await rejectedFixture.Service.CreateOrCoalesceAsync(
            rejectedFixture.CompanyId, rejectedFixture.Command(twoSteps: true), default);
        var rejectedApproval = await AwaitApprovalAsync(rejectedFixture, rejected);
        rejectedApproval.Approval.RejectCurrentStep(rejectedApproval.Approval.CurrentActionableStep!.Id,
            Guid.NewGuid(), "The action is not authorized.");
        rejectedApproval.Attempt.MarkRejected(
            new Dictionary<string, JsonNode?>(),
            new Dictionary<string, JsonNode?>(),
            denialReason: "approval_rejected");
        await rejectedFixture.Db.SaveChangesAsync();

        Assert.True(await rejectedFixture.Service.ResolveApprovalAsync(rejectedFixture.CompanyId,
            new(rejectedApproval.Approval.Id, "rejected", "rejected",
                new Dictionary<string, JsonNode?>(), "approval_rejected",
                "The action is not authorized."), default));
        var blocked = await rejectedFixture.Service.GetAsync(rejectedFixture.CompanyId, rejected.Id, default);
        Assert.Equal("blocked", blocked.Status);
        Assert.Equal("blocked", blocked.Steps[0].Status);
        Assert.Equal("queued", blocked.Steps[1].Status);
        Assert.Null(await rejectedFixture.Service.ClaimStepAsync(rejectedFixture.CompanyId,
            new(blocked.Id, blocked.Steps[1].Id, "worker", "blocked-dependent", 30, blocked.EvidenceHash), default));

        await using var approvedFixture = Fixture.Create();
        var approved = await approvedFixture.Service.CreateOrCoalesceAsync(
            approvedFixture.CompanyId, approvedFixture.Command(twoSteps: true), default);
        var approvedApproval = await AwaitApprovalAsync(approvedFixture, approved);
        approvedApproval.Approval.ApproveCurrentStep(approvedApproval.Approval.CurrentActionableStep!.Id,
            Guid.NewGuid(), "Approved independently.");
        approvedApproval.Attempt.MarkExecuted(
            new Dictionary<string, JsonNode?>(),
            new Dictionary<string, JsonNode?> { ["status"] = "executed" });
        await approvedFixture.Db.SaveChangesAsync();

        Assert.True(await approvedFixture.Service.ResolveApprovalAsync(approvedFixture.CompanyId,
            new(approvedApproval.Approval.Id, "approved", "executed", approvedApproval.Attempt.ResultPayload,
                null, "Approved independently."), default));
        var continued = await approvedFixture.Service.GetAsync(approvedFixture.CompanyId, approved.Id, default);
        Assert.Equal("queued", continued.Status);
        Assert.Equal("completed", continued.Steps[0].Status);
        Assert.NotNull(await approvedFixture.Service.ClaimStepAsync(approvedFixture.CompanyId,
            new(continued.Id, continued.Steps[1].Id, "worker", "continued-dependent", 30, continued.EvidenceHash), default));
    }

    [Fact]
    public async Task Narrowing_creates_a_validated_remove_only_revision_and_rejects_expansion()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(twoSteps: true), default);
        var pendingApproval = await AwaitApprovalAsync(fixture, run);
        run = await fixture.Service.GetAsync(fixture.CompanyId, run.Id, default);

        var revision = await fixture.Service.NarrowAsync(fixture.CompanyId, run.Id,
            new(["inspect"], "narrow-revision-1", "narrow-correlation", "Remove the draft step.", run.Version), default);

        Assert.Equal(run.Id, revision.RevisionOfRunId);
        Assert.Equal(2, revision.RevisionNumber);
        Assert.Single(revision.Steps);
        Assert.Equal("inspect", revision.Steps[0].StepKey);
        Assert.Equal("superseded", (await fixture.Service.GetAsync(fixture.CompanyId, run.Id, default)).Status);
        Assert.Equal(ApprovalRequestStatus.Superseded, pendingApproval.Approval.Status);
        Assert.Equal(ToolExecutionStatus.Denied, pendingApproval.Attempt.Status);

        var expansionSource = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId,
            fixture.Command(twoSteps: true) with { TriggerKey = "expansion", IdempotencyKey = "expansion" }, default);
        _ = await AwaitApprovalAsync(fixture, expansionSource);
        expansionSource = await fixture.Service.GetAsync(fixture.CompanyId, expansionSource.Id, default);
        await Assert.ThrowsAsync<FinanceAutonomyRunValidationException>(() => fixture.Service.NarrowAsync(
            fixture.CompanyId, expansionSource.Id,
            new(["inspect", "new-scope"], "bad-expansion", "bad-expansion", "Expand scope", expansionSource.Version), default));
    }

    [Fact]
    public async Task Manager_cancellation_closes_pending_approval_and_waiting_attempt()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(twoSteps: true), default);
        var pending = await AwaitApprovalAsync(fixture, run);
        run = await fixture.Service.GetAsync(fixture.CompanyId, run.Id, default);

        var cancelled = await fixture.Service.CancelAsync(fixture.CompanyId, run.Id,
            new("Cancel the remaining reviewed work.", run.Version), default);

        Assert.Equal("cancelled", cancelled.Status);
        Assert.Equal(ApprovalRequestStatus.Cancelled, pending.Approval.Status);
        Assert.Equal(ToolExecutionStatus.Denied, pending.Attempt.Status);
        Assert.All(cancelled.Steps, step => Assert.Equal("cancelled", step.Status));
    }

    [Fact]
    public async Task Pending_approval_escalation_is_restart_idempotent_and_notifications_do_not_decide()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(twoSteps: true), default);
        var pending = await AwaitApprovalAsync(fixture, run);
        var coordinator = new FinanceAutonomyApprovalCoordinator(
            fixture.Db, fixture.Service, fixture.Audit, fixture.Clock);

        var first = await coordinator.ProcessBatchAsync(fixture.Clock.UtcNow, 25, default);
        var afterRestart = await coordinator.ProcessBatchAsync(fixture.Clock.UtcNow.AddMinutes(1), 25, default);

        Assert.Equal(1, first.Pending);
        Assert.Equal(1, first.Escalated);
        Assert.Equal(1, afterRestart.Pending);
        Assert.Equal(0, afterRestart.Escalated);
        Assert.Single(await fixture.Db.WorkTasks.IgnoreQueryFilters().ToListAsync());
        var notification = Assert.Single(await fixture.Db.CompanyNotifications.IgnoreQueryFilters().ToListAsync());
        Assert.Contains("\"notificationIsApproval\":false", notification.MetadataJson, StringComparison.Ordinal);
        Assert.Equal(ApprovalRequestStatus.Pending, pending.Approval.Status);
        Assert.Equal("awaiting_approval", (await fixture.Service.GetAsync(fixture.CompanyId, run.Id, default)).Status);
    }

    [Fact]
    public async Task Expired_pending_approval_blocks_run_without_creating_a_replacement()
    {
        await using var fixture = Fixture.Create();
        var run = await fixture.Service.CreateOrCoalesceAsync(fixture.CompanyId, fixture.Command(twoSteps: true), default);
        var pending = await AwaitApprovalAsync(fixture, run);
        ((JsonObject)pending.Approval.ThresholdContext["approvalBinding"]!)["expiresUtc"] =
            fixture.Clock.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        var coordinator = new FinanceAutonomyApprovalCoordinator(
            fixture.Db, fixture.Service, fixture.Audit, fixture.Clock);

        var result = await coordinator.ProcessBatchAsync(fixture.Clock.UtcNow, 25, default);

        Assert.Equal(1, result.Blocked);
        Assert.Equal(ApprovalRequestStatus.Expired, pending.Approval.Status);
        Assert.Equal(ToolExecutionStatus.Denied, pending.Attempt.Status);
        Assert.Equal("blocked", (await fixture.Service.GetAsync(fixture.CompanyId, run.Id, default)).Status);
        Assert.Single(await fixture.Db.ApprovalRequests.IgnoreQueryFilters().ToListAsync());
    }

    private static async Task<(ApprovalRequest Approval, ToolExecutionAttempt Attempt)> AwaitApprovalAsync(
        Fixture fixture, FinanceAutonomyRunDto run)
    {
        var first = run.Steps[0];
        var lease = await fixture.Service.ClaimStepAsync(fixture.CompanyId,
            new(run.Id, first.Id, "approval-worker", "approval-lease", 30, run.EvidenceHash), default);
        Assert.NotNull(lease);
        var attempt = new ToolExecutionAttempt(Guid.NewGuid(), fixture.CompanyId, fixture.AgentId,
            first.ToolName, ToolActionType.Read, "finance", correlationId: run.CorrelationId);
        var approval = new ApprovalRequest(Guid.NewGuid(), fixture.CompanyId, fixture.AgentId, attempt.Id,
            Guid.NewGuid(), first.ToolName, ToolActionType.Read, "owner",
            new Dictionary<string, JsonNode?>
            {
                ["binding"] = "test",
                ["approvalBinding"] = new JsonObject
                {
                    ["expiresUtc"] = fixture.Clock.UtcNow.AddHours(1)
                }
            });
        fixture.Db.ToolExecutionAttempts.Add(attempt);
        fixture.Db.ApprovalRequests.Add(approval);
        attempt.MarkAwaitingApproval(approval.Id, new Dictionary<string, JsonNode?>());
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.AwaitApprovalStepAsync(fixture.CompanyId,
            new(run.Id, first.Id, "approval-lease", approval.Id, attempt.Id, "Awaiting independent review."), default);
        return (approval, attempt);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, FinanceAutonomyRunService service, MutableRunPolicy policy,
            MutableTimeProvider clock, CollectingAuditWriter audit, Guid companyId, Guid agentId, Guid approverId)
        { Db = db; Service = service; Policy = policy; Clock = clock; Audit = audit; CompanyId = companyId; AgentId = agentId; ApproverId = approverId; }
        public VirtualCompanyDbContext Db { get; }
        public FinanceAutonomyRunService Service { get; }
        public MutableRunPolicy Policy { get; }
        public MutableTimeProvider Clock { get; }
        public CollectingAuditWriter Audit { get; }
        public Guid CompanyId { get; }
        public Guid AgentId { get; }
        public Guid ApproverId { get; }

        public static Fixture Create()
        {
            var companyId = Guid.NewGuid(); var agentId = Guid.NewGuid(); var actorId = Guid.NewGuid();
            var approverId = Guid.NewGuid();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var policy = new MutableRunPolicy();
            var clock = new MutableTimeProvider(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
            var audit = new CollectingAuditWriter();
            var service = new FinanceAutonomyRunService(db, policy,
                new ManagerMembership(companyId, actorId), audit, clock);
            var grant = new FinanceAutonomyGrant(policy.GrantId, companyId, agentId, "daily_cash", clock.UtcNow);
            var versionNumber = grant.ReserveNextVersion(clock.UtcNow);
            var grantVersion = new FinanceAutonomyGrantVersion(policy.GrantVersionId, companyId, grant.Id,
                versionNumber, FinanceAutonomyLevel.ReadMonitor,
                [FinanceAutonomyTriggers.ManualReview], ["read"], ["get_cash_balance"],
                100, null, 10, null, "UTC", "00:00", "23:59", 60, "approval_required",
                CompanyMembershipRole.FinanceApprover.ToStorageValue(), clock.UtcNow.AddDays(-1),
                clock.UtcNow.AddDays(30), "catalogue-v1", "policy-v1", "authority-v1",
                policy.AuthorityHash, actorId, clock.UtcNow, false);
            grantVersion.Activate(actorId, "Test grant.", clock.UtcNow);
            grant.Versions.Add(grantVersion);
            grant.Activate(grantVersion.Id, grant.Version, clock.UtcNow);
            db.FinanceAutonomyGrants.Add(grant);
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, approverId,
                CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
            db.SaveChanges();
            return new Fixture(db, service, policy, clock, audit, companyId, agentId, approverId);
        }

        public CreateOrCoalesceFinanceAutonomyRunCommand Command(
            bool twoSteps = false, bool replayPermitted = false, string? authoritativeEventId = null,
            string? authoritativeEventVersion = null, string trigger = FinanceAutonomyTriggers.ManualReview)
        {
            var steps = new List<FinanceAutonomyRunPlanStepDefinition>
            {
                new("inspect", "read", "get_cash_balance", [], Hash("requested-1"), "Inspect cash", 3, replayPermitted)
            };
            if (twoSteps) steps.Add(new("draft", "read", "get_cash_balance", ["inspect"], Hash("requested-2"), "Prepare bounded draft", 3, true));
            return new(AgentId, "daily_cash", trigger, "window-2026-09-01",
                Clock.UtcNow, Clock.UtcNow.AddHours(1), authoritativeEventId, authoritativeEventVersion,
                "run-idempotency", "run-correlation", Clock.UtcNow,
                new Dictionary<string, string?> { ["cashSnapshot"] = "fresh" }, "plan-v1", steps,
                new Dictionary<string, decimal> { ["maximumRecords"] = 10 },
                [new("ledger", "cash_snapshot", "snapshot-1", "v1", Hash("source"), "Cash snapshot")], 1);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MutableRunPolicy : IFinanceAutonomyPolicyEvaluator
    {
        public Guid GrantId { get; } = Guid.NewGuid();
        public Guid GrantVersionId { get; set; } = Guid.NewGuid();
        public string AuthorityHash { get; set; } = Hash("authority");
        public Task<FinanceAutonomyDecisionDto> EvaluateAsync(FinanceAutonomyEvaluationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceAutonomyDecisionDto(true, FinanceAutonomyDecisionReasonCodes.Allowed,
                "Allowed", GrantId, GrantVersionId, 1, FinanceAutonomyLevels.ReadMonitor, false, false,
                100, 100, null, FinanceAutonomyPolicyVersions.V1, "catalogue-v1", "authority-v1",
                AuthorityHash, DateTime.UtcNow));
    }

    private sealed class ManagerMembership(Guid companyId, Guid userId) : ICompanyMembershipContextResolver
    {
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) => ResolveAsync(companyId, cancellationToken);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid requestedCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedCompanyMembershipContext?>(requestedCompanyId == companyId
                ? new(Guid.NewGuid(), companyId, userId, "Run test company", CompanyMembershipRole.Owner,
                    CompanyMembershipStatus.Active, "UTC", "SEK") : null);
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    private sealed class CollectingAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        { Events.Add(auditEvent); return Task.CompletedTask; }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
