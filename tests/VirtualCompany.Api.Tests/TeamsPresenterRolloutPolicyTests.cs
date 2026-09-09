using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsPresenterRolloutPolicyTests
{
    [Fact]
    public async Task Approved_non_demo_pilot_requires_exact_company_user_tenant_package_and_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();

        var decision = await fixture.Policy.EvaluateAsync(fixture.CompanyId, fixture.UserId, fixture.TenantId, default);

        Assert.True(decision.Allowed);
        Assert.Equal(TeamsPresenterReadinessReasonCodes.Ready, decision.ReasonCode);
        Assert.True(decision.PackageCompatible);
        Assert.Equal(0, decision.ActiveCalls);
    }

    [Fact]
    public async Task Tenant_mismatch_is_stable_and_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();

        var decision = await fixture.Policy.EvaluateAsync(fixture.CompanyId, fixture.UserId, Guid.NewGuid(), default);

        Assert.False(decision.Allowed);
        Assert.Equal(TeamsPresenterReadinessReasonCodes.TenantMismatch, decision.ReasonCode);
    }

    [Fact]
    public async Task Emergency_disable_preempts_an_otherwise_ready_pilot()
    {
        await using var fixture = await Fixture.CreateAsync(options => options.EmergencyDisabled = true);

        var decision = await fixture.Policy.EvaluateAsync(fixture.CompanyId, fixture.UserId, fixture.TenantId, default);

        Assert.False(decision.Allowed);
        Assert.Equal(TeamsPresenterReadinessReasonCodes.EmergencyDisabled, decision.ReasonCode);
    }

    [Fact]
    public async Task Missing_live_uat_blocks_production_side_effects()
    {
        await using var fixture = await Fixture.CreateAsync(options => options.LiveUatApproved = false);

        var decision = await fixture.Policy.EvaluateAsync(fixture.CompanyId, fixture.UserId, fixture.TenantId, default);

        Assert.False(decision.Allowed);
        Assert.Equal(TeamsPresenterReadinessReasonCodes.LiveUatRequired, decision.ReasonCode);
    }

    [Fact]
    public async Task Exhausted_monthly_cost_limit_blocks_provider_side_effects()
    {
        await using var fixture = await Fixture.CreateAsync(options => options.MonthlyCostUsed = options.MonthlyCostLimit);

        var decision = await fixture.Policy.EvaluateAsync(fixture.CompanyId, fixture.UserId, fixture.TenantId, default);

        Assert.False(decision.Allowed);
        Assert.Equal(TeamsPresenterReadinessReasonCodes.CostLimitReached, decision.ReasonCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly VirtualCompanyDbContext db;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, Guid companyId, Guid userId,
            Guid tenantId, TeamsPresenterRolloutPolicy policy)
        { this.connection = connection; this.db = db; CompanyId = companyId; UserId = userId; TenantId = tenantId; Policy = policy; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid TenantId { get; }
        public TeamsPresenterRolloutPolicy Policy { get; }

        public static async Task<Fixture> CreateAsync(Action<TeamsPresenterOptions>? change = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var tenantId = Guid.NewGuid();
            db.Companies.Add(new Company(companyId, "Pilot company"));
            var registration = new TeamsTenantRegistration(Guid.NewGuid(), companyId, tenantId,
                Guid.NewGuid(), Guid.NewGuid(), "teams_application_hosted",
                "Calls.AccessMedia.All;Calls.JoinGroupCall.All", userId, DateTime.UtcNow);
            registration.RecordConsentVerification("verified",
                "Calls.AccessMedia.All;Calls.JoinGroupCall.All", null, userId, DateTime.UtcNow);
            registration.AttestPolicy(true, userId, DateTime.UtcNow);
            db.TeamsTenantRegistrations.Add(registration); await db.SaveChangesAsync();
            var options = new TeamsPresenterOptions
            {
                Enabled = true, PilotEnabled = true, PilotCompanyIds = [companyId.ToString("D")],
                PilotUserIds = [userId.ToString("D")], AllowedTenantIds = [tenantId.ToString("D")],
                PackageVersion = "1.2.0", MinimumPackageVersion = "1.1.0", InstalledPackageVersion = "1.2.0",
                AppInstallationAttested = true, AutomatedEvidenceApproved = true, LiveUatApproved = true,
                MaxActiveCallsGlobal = 2, MonthlyCostLimit = 100, CostCurrency = "USD"
            };
            change?.Invoke(options);
            var policy = new TeamsPresenterRolloutPolicy(db, new AllowSideEffects(), Options.Create(options));
            return new Fixture(connection, db, companyId, userId, tenantId, policy);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class AllowSideEffects : IDemoTenantExternalSideEffectPolicy
    {
        public Task<DemoExternalSideEffectDecision> EvaluateAsync(Guid companyId, string actionType, CancellationToken cancellationToken) =>
            Task.FromResult(new DemoExternalSideEffectDecision(true, "not_demo", "Allowed."));
    }
}
