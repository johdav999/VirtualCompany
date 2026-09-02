using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyTriggerTests
{
    [Fact]
    public async Task Daily_schedule_across_fall_DST_creates_one_run_and_advances_cursor_once()
    {
        await using var fixture = await Fixture.CreateAsync(schedule: true, timezone: "Europe/Stockholm",
            cron: "30 2 * * *", utcNow: new DateTime(2026, 10, 25, 3, 30, 0, DateTimeKind.Utc));

        var first = await fixture.Triggers.ProcessDueSchedulesAsync(fixture.Clock.UtcNow, "host-a", 20, default);
        var second = await fixture.Triggers.ProcessDueSchedulesAsync(fixture.Clock.UtcNow, "host-b", 20, default);

        Assert.Equal(1, first.Started);
        Assert.Equal(0, second.Started);
        Assert.Single(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters());
        var cursor = Assert.Single(fixture.Db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters());
        Assert.NotNull(cursor.CursorUtc);
        Assert.Equal(1, cursor.RunsInWindow);
    }

    [Fact]
    public async Task Missed_windows_after_restart_use_latest_bounded_catch_up()
    {
        await using var fixture = await Fixture.CreateAsync(schedule: true, timezone: "UTC",
            cron: "0 6 * * *", utcNow: new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc));

        var result = await fixture.Triggers.ProcessDueSchedulesAsync(fixture.Clock.UtcNow, "restarted-host", 20, default);

        Assert.Equal(1, result.Started);
        var run = Assert.Single(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters());
        Assert.Contains("2026-09-10T06:00:00", run.TriggerKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Equivalent_event_burst_coalesces_and_retains_every_authoritative_source()
    {
        await using var fixture = await Fixture.CreateAsync(eventTypes: [FinanceAutonomyEventTypes.NewUncategorizedTransaction]);
        var first = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.NewUncategorizedTransaction, "event-1", "transaction-1"), "host-a", default);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        var second = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.NewUncategorizedTransaction, "event-2", "transaction-2"), "host-b", default);
        var duplicate = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.NewUncategorizedTransaction, "event-2", "transaction-2"), "host-c", default);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.True(second.Coalesced);
        Assert.True(duplicate.Duplicate);
        var run = Assert.Single(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters().Include(x => x.Sources));
        Assert.Equal(2, run.Sources.Count);
        Assert.Equal(2, fixture.Db.FinanceAutonomyTriggerEvents.IgnoreQueryFilters().Count());
    }

    [Theory]
    [MemberData(nameof(InitialEventTypes))]
    public async Task Every_initial_authoritative_event_source_can_start_reviewed_work(string eventType)
    {
        await using var fixture = await Fixture.CreateAsync(eventTypes: FinanceAutonomyEventTypes.All.ToArray(),
            maximumRunsPerWindow: 100, minimumIntervalMinutes: 1);

        var result = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(eventType, $"event-{eventType}", $"source-{eventType}"), "event-adapter", default);

        Assert.True(result.Accepted);
        Assert.Equal(FinanceAutonomyTriggerReasonCodes.Processed, result.ReasonCode);
    }

    [Fact]
    public async Task Matching_reviewed_template_is_stamped_into_the_immutable_event_run()
    {
        await using var fixture = await Fixture.CreateAsync(
            eventTypes: [FinanceAutonomyEventTypes.StaleCashEvidence]);

        await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.StaleCashEvidence, "cash-event", "cash-snapshot"),
            "template-adapter", default);

        var run = Assert.Single(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters());
        Assert.Equal(FinanceAutonomyWorkflowTemplateVersions.V1, run.PlanVersion);
        Assert.Contains($"reviewed_template:{FinanceAutonomyWorkflowTemplateCodes.StaleCashEvidence}",
            run.PlanJson, StringComparison.Ordinal);
        using var evidence = JsonDocument.Parse(run.EvidenceSnapshotJson);
        Assert.Equal(FinanceAutonomyWorkflowTemplateCodes.StaleCashEvidence,
            evidence.RootElement.GetProperty("workflowTemplateCode").GetString());
        Assert.Equal("finance_manager", evidence.RootElement.GetProperty("workflowOwnerRole").GetString());
    }

    [Fact]
    public async Task Paused_policy_and_revoked_grant_start_no_schedule_or_event_run()
    {
        await using var fixture = await Fixture.CreateAsync(schedule: true,
            eventTypes: [FinanceAutonomyEventTypes.StaleCashEvidence], cron: "0 10 * * *");
        fixture.Policy.Allowed = false;
        var paused = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.StaleCashEvidence, "paused-event", "cash-snapshot"), "host", default);
        await fixture.Triggers.ProcessDueSchedulesAsync(fixture.Clock.UtcNow, "schedule-host", 10, default);

        Assert.False(paused.Accepted);
        Assert.Equal(FinanceAutonomyTriggerReasonCodes.GrantUnavailable, paused.ReasonCode);
        Assert.Empty(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters());

        fixture.Version.Revoke(Guid.NewGuid(), "Grant revoked", fixture.Clock.UtcNow);
        fixture.Grant.ClearActiveVersion(fixture.Grant.Version, fixture.Clock.UtcNow);
        await fixture.Db.SaveChangesAsync();
        var revoked = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.StaleCashEvidence, "revoked-event", "cash-snapshot-2"), "host", default);
        Assert.Equal(FinanceAutonomyTriggerReasonCodes.GrantUnavailable, revoked.ReasonCode);
        Assert.Empty(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Expired_grant_and_other_company_signal_create_no_trigger_state_or_runs()
    {
        await using var fixture = await Fixture.CreateAsync(
            eventTypes: [FinanceAutonomyEventTypes.OverdueReceivable],
            expiresUtc: new DateTime(2026, 9, 1, 9, 59, 0, DateTimeKind.Utc));

        var expired = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.OverdueReceivable, "expired-event", "invoice-1"), "host", default);
        var crossCompany = await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.OverdueReceivable, "foreign-event", "invoice-2") with
            { CompanyId = Guid.NewGuid() }, "host", default);

        Assert.Equal(FinanceAutonomyTriggerReasonCodes.GrantUnavailable, expired.ReasonCode);
        Assert.Equal(FinanceAutonomyTriggerReasonCodes.GrantUnavailable, crossCompany.ReasonCode);
        Assert.Empty(fixture.Db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters());
        Assert.Empty(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Out_of_order_burst_retains_sources_without_regressing_authoritative_cursor()
    {
        await using var fixture = await Fixture.CreateAsync(
            eventTypes: [FinanceAutonomyEventTypes.NewUncategorizedTransaction]);
        var newer = fixture.Signal(FinanceAutonomyEventTypes.NewUncategorizedTransaction,
            "event-new", "transaction-new") with
        {
            SourceEventVersion = "version-2",
            OccurredUtc = fixture.Clock.UtcNow,
            EvidenceObservedUtc = fixture.Clock.UtcNow
        };
        var older = fixture.Signal(FinanceAutonomyEventTypes.NewUncategorizedTransaction,
            "event-old", "transaction-old") with
        {
            SourceEventVersion = "version-1",
            OccurredUtc = fixture.Clock.UtcNow.AddMinutes(-1),
            EvidenceObservedUtc = fixture.Clock.UtcNow.AddMinutes(-1)
        };

        await fixture.Triggers.ProcessEventAsync(newer, "host", default);
        await fixture.Triggers.ProcessEventAsync(older, "host", default);

        var cursor = Assert.Single(fixture.Db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters());
        Assert.Equal(newer.OccurredUtc, cursor.CursorUtc);
        Assert.Equal("version-2", cursor.LastEventVersion);
        Assert.Equal(2, fixture.Db.FinanceAutonomyRunSources.IgnoreQueryFilters().Count());
    }

    [Fact]
    public async Task Late_event_is_dead_lettered_without_treating_signal_as_current_eligibility()
    {
        await using var fixture = await Fixture.CreateAsync(eventTypes: [FinanceAutonomyEventTypes.ImportFailed],
            lateEventToleranceMinutes: 10);
        var old = fixture.Signal(FinanceAutonomyEventTypes.ImportFailed, "late-event", "import-job") with
        {
            OccurredUtc = fixture.Clock.UtcNow.AddHours(-1),
            EvidenceObservedUtc = fixture.Clock.UtcNow.AddHours(-1)
        };

        var result = await fixture.Triggers.ProcessEventAsync(old, "host", default);

        Assert.Equal(FinanceAutonomyTriggerReasonCodes.LateEvent, result.ReasonCode);
        Assert.Empty(fixture.Db.FinanceAutonomyRuns.IgnoreQueryFilters());
        Assert.Equal("dead_lettered", Assert.Single(fixture.Db.FinanceAutonomyTriggerEvents.IgnoreQueryFilters()).Status.ToStorageValue());
    }

    [Fact]
    public async Task Multi_host_lease_excludes_second_owner_until_expiry()
    {
        var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var cursor = new FinanceAutonomyTriggerCursor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "daily_cash", "schedule", "daily", now);

        Assert.True(cursor.TryClaim("host-a", "lease-a", now, TimeSpan.FromMinutes(2)));
        Assert.False(cursor.TryClaim("host-b", "lease-b", now.AddMinutes(1), TimeSpan.FromMinutes(2)));
        Assert.True(cursor.TryClaim("host-b", "lease-b", now.AddMinutes(3), TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task Repeated_safe_processing_failures_dead_letter_and_manager_can_retry()
    {
        await using var fixture = await Fixture.CreateAsync(eventTypes: [FinanceAutonomyEventTypes.ReconciliationFailed],
            coverage: new EmptyCoverage());
        var signal = fixture.Signal(FinanceAutonomyEventTypes.ReconciliationFailed, "failed-event", "reconciliation-job");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(6);
            await fixture.Triggers.ProcessEventAsync(signal, $"host-{attempt}", default);
        }

        var cursor = Assert.Single(fixture.Db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters());
        Assert.Equal("dead_lettered", cursor.Status.ToStorageValue());
        var retried = await fixture.Triggers.RetryDeadLetterAsync(fixture.CompanyId, cursor.Id, cursor.Version, default);
        Assert.Equal("idle", retried.Status);
        Assert.Equal(0, retried.AttemptCount);
        Assert.Equal("received", Assert.Single(fixture.Db.FinanceAutonomyTriggerEvents.IgnoreQueryFilters()).Status.ToStorageValue());
        Assert.Contains(fixture.Audit.Events, x => x.Action == AuditEventActions.FinanceAutonomyTriggerRetried);
    }

    [Fact]
    public async Task Operator_queries_are_tenant_scoped_and_retry_requires_manager()
    {
        await using var fixture = await Fixture.CreateAsync(eventTypes: [FinanceAutonomyEventTypes.BackgroundWorkCompleted]);
        await fixture.Triggers.ProcessEventAsync(
            fixture.Signal(FinanceAutonomyEventTypes.BackgroundWorkCompleted, "work-event", "work-1"), "host", default);

        Assert.Single((await fixture.Triggers.GetOperationalStateAsync(fixture.CompanyId, 20, default)).Cursors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Triggers.GetOperationalStateAsync(Guid.NewGuid(), 20, default));
        fixture.Membership.Role = CompanyMembershipRole.Employee;
        var cursor = Assert.Single(fixture.Db.FinanceAutonomyTriggerCursors.IgnoreQueryFilters());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Triggers.RetryDeadLetterAsync(fixture.CompanyId, cursor.Id, cursor.Version, default));
    }

    [Fact]
    public void Api_exposes_safe_queries_and_manager_only_recovery()
    {
        var controller = typeof(FinanceAutonomyTriggersController);
        Assert.Equal(CompanyPolicies.FinanceView,
            Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
        Assert.Equal(CompanyPolicies.CompanyManager,
            Assert.Single(controller.GetMethod(nameof(FinanceAutonomyTriggersController.Retry))!
                .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
    }

    public static TheoryData<string> InitialEventTypes => new()
    {
        FinanceAutonomyEventTypes.NewUncategorizedTransaction,
        FinanceAutonomyEventTypes.OverdueReceivable,
        FinanceAutonomyEventTypes.StaleCashEvidence,
        FinanceAutonomyEventTypes.CloseTaskBlockerChanged,
        FinanceAutonomyEventTypes.ReconciliationFailed,
        FinanceAutonomyEventTypes.ImportFailed,
        FinanceAutonomyEventTypes.ComplianceObligationExpiring,
        FinanceAutonomyEventTypes.BackgroundWorkCompleted
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, FinanceAutonomyTriggerService triggers,
            MutableTriggerPolicy policy, MutableTimeProvider clock, MutableMembership membership,
            CollectingAuditWriter audit, FinanceAutonomyGrant grant, FinanceAutonomyGrantVersion version,
            Guid companyId, Guid agentId)
        {
            Db = db; Triggers = triggers; Policy = policy; Clock = clock; Membership = membership;
            Audit = audit; Grant = grant; Version = version; CompanyId = companyId; AgentId = agentId;
        }
        public VirtualCompanyDbContext Db { get; }
        public FinanceAutonomyTriggerService Triggers { get; }
        public MutableTriggerPolicy Policy { get; }
        public MutableTimeProvider Clock { get; }
        public MutableMembership Membership { get; }
        public CollectingAuditWriter Audit { get; }
        public FinanceAutonomyGrant Grant { get; }
        public FinanceAutonomyGrantVersion Version { get; }
        public Guid CompanyId { get; }
        public Guid AgentId { get; }

        public static async Task<Fixture> CreateAsync(bool schedule = false,
            IReadOnlyList<string>? eventTypes = null, string timezone = "UTC", string cron = "0 6 * * *",
            DateTime? utcNow = null, int maximumRunsPerWindow = 10, int minimumIntervalMinutes = 60,
            int lateEventToleranceMinutes = 1440, IFinanceAgentCoverageCatalogue? coverage = null,
            DateTime? expiresUtc = null)
        {
            var now = utcNow ?? new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
            var companyId = Guid.NewGuid(); var agentId = Guid.NewGuid(); var grantId = Guid.NewGuid();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var company = new Company(companyId, "Finance trigger test company");
            var agent = new Agent(agentId, companyId, "finance-trigger-agent", "Laura", "Finance Manager",
                "Finance", null, AgentSeniority.Senior, AgentStatus.Active, AgentAutonomyLevel.Guided);
            var grant = new FinanceAutonomyGrant(grantId, companyId, agentId,
                FinanceAgentCoverageCapabilityIds.DailyCash, now.AddDays(-40));
            var versionNumber = grant.ReserveNextVersion(now.AddDays(-40));
            var triggers = new List<string>();
            if (schedule) triggers.Add(FinanceAutonomyTriggers.Schedule);
            if (eventTypes is { Count: > 0 }) triggers.Add(FinanceAutonomyTriggers.BusinessEvent);
            var version = new FinanceAutonomyGrantVersion(Guid.NewGuid(), companyId, grantId, versionNumber,
                FinanceAutonomyLevel.ReadMonitor, triggers, ["read"], ["get_cash_balance"],
                100, null, 1, schedule ? cron : null, timezone, "00:00", "23:59", 10080,
                FinanceAutonomyConfirmationBehaviors.NoConfirmation, "company_owner", now.AddDays(-40),
                expiresUtc ?? now.AddDays(40), "catalogue-v1", Hash("capability-policy"), "authority-v1",
                Hash("authority"), Guid.NewGuid(), now.AddDays(-40), false, eventTypes,
                minimumIntervalMinutes, maximumRunsPerWindow, 5, FinanceAutonomyCatchUpBehaviors.Latest,
                1, lateEventToleranceMinutes);
            version.Activate(Guid.NewGuid(), "Reviewed trigger", now.AddDays(-39));
            grant.Activate(version.Id, grant.Version, now.AddDays(-39));
            grant.Versions.Add(version);
            db.AddRange(company, agent, grant);
            await db.SaveChangesAsync();

            var clock = new MutableTimeProvider(now);
            var membership = new MutableMembership(companyId, Guid.NewGuid());
            var audit = new CollectingAuditWriter();
            var policy = new MutableTriggerPolicy(grant, version, clock);
            var runService = new FinanceAutonomyRunService(db, policy, membership, audit, clock);
            var triggerService = new FinanceAutonomyTriggerService(db, runService,
                coverage ?? new StaticCoverage(), membership, audit, clock);
            return new Fixture(db, triggerService, policy, clock, membership, audit,
                grant, version, companyId, agentId);
        }

        public FinanceAutonomyEventSignal Signal(string eventType, string eventId, string sourceId) =>
            new(CompanyId, eventType, eventId, "v1", "finance_record", sourceId, Clock.UtcNow,
                Clock.UtcNow, eventType, Hash($"{eventId}:{sourceId}"), eventType.Replace('_', ' '),
                $"correlation:{eventId}");

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MutableTriggerPolicy(FinanceAutonomyGrant grant,
        FinanceAutonomyGrantVersion version, MutableTimeProvider clock) : IFinanceAutonomyPolicyEvaluator
    {
        public bool Allowed { get; set; } = true;
        public Task<FinanceAutonomyDecisionDto> EvaluateAsync(FinanceAutonomyEvaluationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new FinanceAutonomyDecisionDto(
                Allowed, Allowed ? FinanceAutonomyDecisionReasonCodes.Allowed : FinanceAutonomyDecisionReasonCodes.Paused,
                Allowed ? "Allowed" : "Finance autonomy is paused.", grant.Id, version.Id, version.VersionNumber,
                FinanceAutonomyLevels.ReadMonitor, false, false, 100, 1, null,
                FinanceAutonomyPolicyVersions.V1, version.CatalogueVersion, version.AuthorityVersion,
                version.AuthorityHash, clock.UtcNow));
    }

    private sealed class StaticCoverage : IFinanceAgentCoverageCatalogue
    {
        public IReadOnlyList<FinanceAgentCoverageCapabilityManifest> ListManifests() => FinanceAgentCoverageCatalogue.Manifests;
        public Task<FinanceAgentEffectiveCoverageDto> GetEffectiveCoverageAsync(Guid companyId, Guid agentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyCoverage : IFinanceAgentCoverageCatalogue
    {
        public IReadOnlyList<FinanceAgentCoverageCapabilityManifest> ListManifests() => [];
        public Task<FinanceAgentEffectiveCoverageDto> GetEffectiveCoverageAsync(Guid companyId, Guid agentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MutableMembership(Guid companyId, Guid userId) : ICompanyMembershipContextResolver
    {
        public CompanyMembershipRole Role { get; set; } = CompanyMembershipRole.Owner;
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) =>
            ResolveAsync(companyId, cancellationToken);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid requestedCompanyId,
            CancellationToken cancellationToken) => Task.FromResult<ResolvedCompanyMembershipContext?>(
                requestedCompanyId == companyId ? new(Guid.NewGuid(), companyId, userId, "Trigger company",
                    Role, CompanyMembershipStatus.Active, "UTC", "SEK") : null);
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    private sealed class CollectingAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        { Events.Add(auditEvent); return Task.CompletedTask; }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
