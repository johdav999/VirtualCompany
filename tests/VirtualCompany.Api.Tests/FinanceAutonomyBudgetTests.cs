using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

public sealed class FinanceAutonomyBudgetTests
{
    [Fact]
    public void Exact_boundary_is_allowed_and_one_unit_over_is_rejected()
    {
        var limits = Limits(records: 10, amount: 100m, modelCalls: 2);
        var current = Usage(records: 4, amount: 40m, modelCalls: 1);
        Assert.Null(limits.FirstExceeded(current, Usage(records: 6, amount: 60m, modelCalls: 1)));
        Assert.Equal("records_evaluated", limits.FirstExceeded(current, Usage(records: 7)));
        Assert.Equal("amount_exposure", limits.FirstExceeded(current, Usage(amount: 60.01m)));
        Assert.Equal("model_calls", limits.FirstExceeded(current, Usage(modelCalls: 2)));
    }

    [Fact]
    public async Task Reservations_aggregate_split_actions_and_reconcile_actual_usage()
    {
        await using var f = Fixture.Create(windowAmount: 100m);
        var first = await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1,
            new(AmountExposure: 60m, ToolCalls: 1), default);
        Assert.True(first.Allowed);
        await f.Db.SaveChangesAsync();

        var denied = await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run2.Id, f.Step2.Id, 1,
            new(AmountExposure: 50m, ToolCalls: 1), default);
        Assert.False(denied.Allowed);
        Assert.Equal(FinanceAutonomyBudgetReasonCodes.WindowExceeded, denied.ReasonCode);

        await f.Service.ReconcileForAttemptAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1,
            new(AmountExposure: 55m, ToolCalls: 1), false, default);
        await f.Db.SaveChangesAsync();
        var allowed = await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run2.Id, f.Step2.Id, 1,
            new(AmountExposure: 45m, ToolCalls: 1), default);
        Assert.True(allowed.Allowed);
        await f.Db.SaveChangesAsync();

        var window = await f.Db.FinanceAutonomyBudgetWindows.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(55m, window.Consumed.AmountExposure);
        Assert.Equal(45m, window.Reserved.AmountExposure);
    }

    [Fact]
    public async Task Retries_do_not_reset_consumed_usage_and_are_counted_separately()
    {
        await using var f = Fixture.Create(windowAmount: 1000m);
        Assert.True((await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1,
            new(ModelCalls: 1, ToolCalls: 1), default)).Allowed);
        await f.Db.SaveChangesAsync();
        await f.Service.ReconcileForAttemptAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1,
            new(ModelCalls: 1, ToolCalls: 1), false, default);
        await f.Db.SaveChangesAsync();

        Assert.True((await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 2,
            new(ModelCalls: 1, ToolCalls: 1), default)).Allowed);
        await f.Db.SaveChangesAsync();
        var window = await f.Db.FinanceAutonomyBudgetWindows.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1, window.Consumed.ModelCalls);
        Assert.Equal(1, window.Reserved.ModelCalls);
        Assert.Equal(1, window.Reserved.Retries);
    }

    [Fact]
    public async Task Per_run_snapshot_aggregates_usage_across_multiple_steps()
    {
        await using var f = Fixture.Create(windowAmount: 1000m);
        var hash = Hash("second-step");
        var second = new FinanceAutonomyRunStep(Guid.NewGuid(), f.CompanyId, f.Run1.Id, 2, "second", "read",
            "get_cash_balance", ["inspect"], 3, FinanceAutonomyPolicyVersions.V1, "authority-v1", hash, hash,
            hash, "Second bounded read", true, null, f.Clock.UtcNow);
        f.Run1.Steps.Add(second);
        f.Db.FinanceAutonomyRunSteps.Add(second);
        await f.Db.SaveChangesAsync();
        Assert.True((await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1,
            new(RecordsEvaluated: 60), default)).Allowed);
        await f.Db.SaveChangesAsync();
        var denied = await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, second.Id, 1,
            new(RecordsEvaluated: 41), default);
        Assert.False(denied.Allowed);
        Assert.Equal(FinanceAutonomyBudgetReasonCodes.PerRunExceeded, denied.ReasonCode);
    }

    [Fact]
    public async Task Local_midnight_rolls_to_a_new_window_without_resetting_history()
    {
        await using var f = Fixture.Create(new DateTime(2026, 9, 1, 21, 59, 0, DateTimeKind.Utc),
            "Europe/Stockholm", 1000m);
        Assert.True((await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1,
            new(ToolCalls: 1), default)).Allowed);
        await f.Db.SaveChangesAsync();
        f.Clock.UtcNow = new DateTime(2026, 9, 1, 22, 1, 0, DateTimeKind.Utc);
        Assert.True((await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run2.Id, f.Step2.Id, 1,
            new(ToolCalls: 1), default)).Allowed);
        await f.Db.SaveChangesAsync();
        Assert.Equal(2, await f.Db.FinanceAutonomyBudgetWindows.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await f.Db.FinanceAutonomyBudgetReservations.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Repeated_provider_ambiguity_opens_circuit_blocks_work_and_requires_operator_reset()
    {
        await using var f = Fixture.Create(windowAmount: 1000m, providerAmbiguityThreshold: 2);
        var signal = new RecordFinanceAutonomyCircuitSignalCommand(f.AgentId, "daily_cash",
            FinanceAutonomyCircuitSignals.ProviderAmbiguity, "circuit-correlation", "Provider result remained ambiguous.");
        await f.Service.RecordCircuitSignalAsync(f.CompanyId, signal, default);
        await f.Service.RecordCircuitSignalAsync(f.CompanyId, signal, default);

        var circuit = await f.Db.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(FinanceAutonomyCircuitStatus.Open, circuit.Status);
        Assert.Equal(FinanceAutonomyControlState.Paused,
            (await f.Db.FinanceAutonomyControls.IgnoreQueryFilters().SingleAsync()).State);
        Assert.Equal(FinanceAutonomyBudgetAlertStatus.Open,
            (await f.Db.FinanceAutonomyBudgetAlerts.IgnoreQueryFilters().SingleAsync()).Status);
        Assert.Equal(FinanceAutonomyBudgetReasonCodes.CircuitOpen,
            (await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1, new(ToolCalls: 1), default)).ReasonCode);

        await f.Service.ResetCircuitAsync(f.CompanyId, circuit.Id, circuit.Version, default);
        Assert.True((await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run1.Id, f.Step1.Id, 1,
            new(ToolCalls: 1), default)).Allowed);
        f.Operating.EmergencyStop("Operator emergency stop");
        await f.Db.SaveChangesAsync();
        Assert.Equal(FinanceAutonomyBudgetReasonCodes.EmergencyStopped,
            (await f.Service.ReserveForClaimAsync(f.CompanyId, f.Run2.Id, f.Step2.Id, 1, new(ToolCalls: 1), default)).ReasonCode);
    }

    [Fact]
    public async Task Operational_queries_are_tenant_scoped_and_mutations_require_manager_access()
    {
        await using var f = Fixture.Create(windowAmount: 1000m);
        Assert.Empty((await f.Service.GetAsync(f.CompanyId, 20, default)).Alerts);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetAsync(Guid.NewGuid(), 20, default));
        var reader = f.WithRole(CompanyMembershipRole.Employee);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ResetCircuitAsync(
            f.CompanyId, Guid.NewGuid(), 0, default));
    }

    [Fact]
    public void Budget_API_separates_finance_read_access_from_manager_mutations()
    {
        var controller = typeof(FinanceAutonomyBudgetsController);
        Assert.Equal(CompanyPolicies.FinanceView,
            Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
        foreach (var method in new[] { "UpsertPolicy", "ResetCircuit" })
            Assert.Contains(controller.GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>(), x => x.Policy == CompanyPolicies.CompanyManager);
    }

    private static FinanceAutonomyUsageValues Usage(int records = 0, decimal amount = 0, int modelCalls = 0) =>
        new(records, 0, 0, amount, 0, 0, modelCalls, 0, 0, 0, 0);
    private static FinanceAutonomyUsageLimits Limits(int? records = null, decimal? amount = null, int? modelCalls = null) =>
        new(records, null, null, amount, null, null, modelCalls, null, null, null, null);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, FinanceAutonomyBudgetService service, MutableClock clock,
            Guid companyId, Guid agentId, FinanceAutonomyRun run1, FinanceAutonomyRunStep step1,
            FinanceAutonomyRun run2, FinanceAutonomyRunStep step2, CompanyOperatingConfiguration operating,
            CollectingAudit audit)
        { Db = db; Service = service; Clock = clock; CompanyId = companyId; AgentId = agentId; Run1 = run1; Step1 = step1; Run2 = run2; Step2 = step2; Operating = operating; Audit = audit; }
        public VirtualCompanyDbContext Db { get; }
        public FinanceAutonomyBudgetService Service { get; }
        public MutableClock Clock { get; }
        public Guid CompanyId { get; }
        public Guid AgentId { get; }
        public FinanceAutonomyRun Run1 { get; }
        public FinanceAutonomyRunStep Step1 { get; }
        public FinanceAutonomyRun Run2 { get; }
        public FinanceAutonomyRunStep Step2 { get; }
        public CompanyOperatingConfiguration Operating { get; }
        public CollectingAudit Audit { get; }

        public static Fixture Create(DateTime? now = null, string timezone = "UTC", decimal windowAmount = 100m,
            int providerAmbiguityThreshold = 2)
        {
            var companyId = Guid.NewGuid(); var agentId = Guid.NewGuid();
            var clock = new MutableClock(now ?? new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var operating = new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
            operating.Update(null, CompanyAutonomyLevel.Recommend, timezone, 6, 60, 4, 5, 12, 3, 120, 4, 20, 1000m);
            operating.UpdateRollingLimits(48, 16, 80, 5000m);
            var policy = new FinanceAutonomyBudgetPolicy(Guid.NewGuid(), companyId, null, null, timezone, 1440,
                new(null, null, null, 1000m, null, null, 4, 20, 1000m, 10, 120),
                new(null, null, null, windowAmount, null, null, 16, 80, 5000m, 100, 86400),
                3, 3, providerAmbiguityThreshold, 5, 3, 2, 60, 60, clock.UtcNow);
            var (run1, step1) = CreateRun(companyId, agentId, "one", clock.UtcNow);
            var (run2, step2) = CreateRun(companyId, agentId, "two", clock.UtcNow);
            db.AddRange(operating, policy, run1, step1, run2, step2);
            db.SaveChanges();
            var audit = new CollectingAudit();
            var service = new FinanceAutonomyBudgetService(db,
                new Membership(companyId, CompanyMembershipRole.Owner), new Coverage(), audit, clock);
            return new(db, service, clock, companyId, agentId, run1, step1, run2, step2, operating, audit);
        }

        public FinanceAutonomyBudgetService WithRole(CompanyMembershipRole role) => new(Db,
            new Membership(CompanyId, role), new Coverage(), Audit, Clock);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static (FinanceAutonomyRun Run, FinanceAutonomyRunStep Step) CreateRun(
        Guid companyId, Guid agentId, string key, DateTime now)
    {
        var hash = Hash(key); var runId = Guid.NewGuid();
        var run = new FinanceAutonomyRun(runId, companyId, agentId, "daily_cash", Guid.NewGuid(), Guid.NewGuid(), 1,
            FinanceAutonomyTriggers.Schedule, key, now, now.AddHours(1), null, null, Hash("logical-" + key),
            "idem-" + key, "corr-" + key, "{}", hash, now, "{}", hash, "v1",
            "{\"maximumRecords\":100,\"maximumActions\":10,\"maximumAmount\":1000}", hash,
            FinanceAutonomyPolicyVersions.V1, "catalogue-v1", "authority-v1", hash,
            null, null, null, null, null, null, now);
        var step = new FinanceAutonomyRunStep(Guid.NewGuid(), companyId, runId, 1, "inspect", "read",
            "get_cash_balance", [], 3, FinanceAutonomyPolicyVersions.V1, "authority-v1", hash, hash, hash,
            "Inspect cash", true, null, now);
        run.Steps.Add(step);
        return (run, step);
    }

    private sealed class Membership(Guid companyId, CompanyMembershipRole role) : ICompanyMembershipContextResolver
    {
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) => ResolveAsync(companyId, cancellationToken);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid requestedCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedCompanyMembershipContext?>(requestedCompanyId == companyId
                ? new(Guid.NewGuid(), companyId, Guid.NewGuid(), "Budget test company", role,
                    CompanyMembershipStatus.Active, "UTC", "SEK") : null);
    }

    private sealed class Coverage : IFinanceAgentCoverageCatalogue
    {
        public IReadOnlyList<FinanceAgentCoverageCapabilityManifest> ListManifests() => FinanceAgentCoverageCatalogue.Manifests;
        public Task<FinanceAgentEffectiveCoverageDto> GetEffectiveCoverageAsync(Guid companyId, Guid agentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CollectingAudit : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        { Events.Add(auditEvent); return Task.CompletedTask; }
    }

    private sealed class MutableClock(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
