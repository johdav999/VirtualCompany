using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

// Application authorization fixtures intentionally disable unrelated foreign keys.
// SQL Server migration verification owns relational upgrade behavior.
public sealed class TeamsDeploymentPrerequisiteTests
{
    [Fact]
    public async Task Selected_marketing_presenter_uses_shared_profile_and_only_available_tools()
    {
        await using var f = await Fixture.CreateAsync();
        var state = await f.Presenters.SelectAsync(f.Company, f.User, f.Meeting.Id, new(f.Agent.Id, 1), default);
        var runtime = await f.Presenters.ResolveAsync(f.Company, f.Meeting.Id, default);
        Assert.Equal(f.Agent.Id, state.AgentId);
        Assert.Equal(2, state.Version);
        Assert.Contains("Maya", runtime.Instructions);
        Assert.Contains("Marketing", runtime.Instructions);
        Assert.Contains("calm and evidence-led", runtime.Instructions);
        Assert.Contains("Explain campaign results", runtime.Instructions);
        Assert.DoesNotContain("You are Alex", runtime.Instructions);
        Assert.Single(runtime.Tools);
        Assert.Equal(f.Catalog.ToolName, runtime.Tools[0].Name);
        Assert.Contains(f.Audit.Events, e => e.Action == "teams.presenter.selected" && e.ActorId == f.User);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("unsupported")]
    [InlineData("denied")]
    [InlineData("approval")]
    public async Task Invalid_presenter_is_rejected_without_changing_binding(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        var id = f.Agent.Id;
        if (scenario is "foreign" or "inactive" or "unsupported")
        {
            var candidate = new Agent(Guid.NewGuid(), scenario == "foreign" ? Guid.NewGuid() : f.Company,
                "candidate", "Candidate", "Presenter", scenario == "unsupported" ? "HR" : "Marketing",
                null, AgentSeniority.Mid, scenario == "inactive" ? AgentStatus.Paused : AgentStatus.Active);
            await f.AddCandidateAsync(candidate); id = candidate.Id;
        }
        if (scenario == "missing") id = Guid.NewGuid();
        if (scenario == "denied") f.Catalog.State = AgentCapabilityStates.PermissionDenied;
        if (scenario == "approval") f.Catalog.State = AgentCapabilityStates.ApprovalRequired;
        await Assert.ThrowsAsync<TeamsCallControlException>(() =>
            f.Presenters.SelectAsync(f.Company, f.User, f.Meeting.Id, new(id, 1), default));
        Assert.Null(f.Meeting.PresenterAgentId);
        Assert.Empty(f.Audit.Events);
    }

    [Fact]
    public async Task Organizer_version_and_active_call_boundaries_are_enforced()
    {
        await using var f = await Fixture.CreateAsync();
        var stranger = Guid.NewGuid();
        f.Db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), f.Company, stranger,
            CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            f.Presenters.SelectAsync(f.Company, stranger, f.Meeting.Id, new(f.Agent.Id, 1), default));
        await Assert.ThrowsAsync<TeamsCallControlException>(() =>
            f.Presenters.SelectAsync(f.Company, f.User, f.Meeting.Id, new(f.Agent.Id, 99), default));
        await f.AddCallAsync();
        await Assert.ThrowsAsync<TeamsCallControlException>(() =>
            f.Presenters.SelectAsync(f.Company, f.User, f.Meeting.Id, new(f.Agent.Id, 1), default));
        Assert.Null(f.Meeting.PresenterAgentId);
    }

    [Fact]
    public async Task Unselected_meeting_and_permission_revocation_fail_closed()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<TeamsCallControlException>(() => f.Presenters.ResolveAsync(f.Company, f.Meeting.Id, default));
        await f.Presenters.SelectAsync(f.Company, f.User, f.Meeting.Id, new(f.Agent.Id, 1), default);
        f.Catalog.State = AgentCapabilityStates.PermissionDenied;
        await Assert.ThrowsAsync<TeamsCallControlException>(() => f.Presenters.ResolveAsync(f.Company, f.Meeting.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Presenters.ResolveAsync(Guid.NewGuid(), f.Meeting.Id, default));
    }

    [Fact]
    public async Task First_test_is_exactly_scoped_audited_and_never_approves_release()
    {
        await using var f = await Fixture.CreateAsync();
        var grant = await f.GrantAsync();
        Assert.Equal(f.Meeting.Id, grant.MeetingSessionId);
        Assert.Equal(f.Clock.Now.AddMinutes(15).UtcDateTime, grant.ExpiresUtc);
        Assert.True((await f.DecideAsync()).Allowed);
        Assert.Equal("teams_presenter.first_uat_allowed", (await f.DecideAsync()).ReasonCode);
        Assert.False((await f.Policy.EvaluateAsync(f.Company, f.User, f.Tenant, default)).Allowed);
        Assert.False((await f.Policy.EvaluateAsync(f.Company, f.User, f.Tenant, default, Guid.NewGuid())).Allowed);
        Assert.False((await f.Policy.EvaluateAsync(f.Company, Guid.NewGuid(), f.Tenant, default, f.Meeting.Id)).Allowed);
        Assert.False((await f.Policy.EvaluateAsync(f.Company, f.User, Guid.NewGuid(), default, f.Meeting.Id)).Allowed);
        Assert.False((await f.Policy.EvaluateAsync(Guid.NewGuid(), f.User, f.Tenant, default, f.Meeting.Id)).Allowed);
        Assert.False(f.Options.LiveUatApproved);
        Assert.Contains(f.Audit.Events, e => e.Action == "teams.first_uat.authorized" && e.ActorId == f.Admin.UserId &&
            e.Metadata!["meetingId"] == f.Meeting.Id.ToString("D"));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("production")]
    [InlineData("emergency")]
    [InlineData("demo")]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("organizer")]
    [InlineData("automated")]
    [InlineData("package")]
    [InlineData("consent")]
    [InlineData("budget")]
    [InlineData("pilot")]
    [InlineData("capacity")]
    public async Task First_test_preserves_every_other_rollout_gate(string gate)
    {
        await using var f = await Fixture.CreateAsync();
        await f.GrantAsync();
        switch (gate)
        {
            case "disabled": f.Options.FirstUatEnabled = false; break;
            case "production": f.Options.ProductionEnabled = true; break;
            case "emergency": f.Options.EmergencyDisabled = true; break;
            case "demo": f.Demo.Allowed = false; break;
            case "tenant": f.Options.AllowedTenantIds = []; break;
            case "company": f.Options.PilotCompanyIds = []; break;
            case "organizer": f.Options.PilotUserIds = []; break;
            case "automated": f.Options.AutomatedEvidenceApproved = false; break;
            case "package": f.Options.InstalledPackageVersion = "0.1.0"; break;
            case "consent": f.Registration.AttestPolicy(false, f.User, f.Clock.Now.UtcDateTime); await f.Db.SaveChangesAsync(); break;
            case "budget": f.Options.MonthlyCostUsed = f.Options.MonthlyCostLimit; break;
            case "pilot": f.Options.PilotEnabled = false; break;
            case "capacity": await f.AddCallAsync(Guid.NewGuid()); break;
        }
        Assert.False((await f.DecideAsync()).Allowed);
    }

    [Fact]
    public async Task Expiry_and_revocation_invalidate_bound_calls_even_if_release_is_later_approved()
    {
        await using var f = await Fixture.CreateAsync();
        await f.GrantAsync();
        await f.AddCallAsync();
        Assert.True((await f.DecideAsync()).Allowed); // A call does not count itself against admission capacity.
        f.Options.LiveUatApproved = true;
        f.Clock.Now = f.Clock.Now.AddMinutes(16);
        Assert.False((await f.DecideAsync()).Allowed);
        f.Clock.Now = f.Clock.Now.AddMinutes(-16);
        await f.Uat.RevokeAsync(f.Company, f.Admin.UserId!.Value, f.Registration.ConcurrencyVersion, default);
        Assert.False((await f.DecideAsync()).Allowed);
        Assert.Single(f.Drain.Terminated);
        Assert.Contains(f.Audit.Events, e => e.Action == "teams.first_uat.revoked");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task Grant_duration_is_bounded(int duration)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<TeamsCallControlException>(() => f.Uat.AuthorizeAsync(f.Company, f.Admin.UserId!.Value,
            new(f.User, f.Meeting.Id, duration, "Isolated test", f.Registration.ConcurrencyVersion), default));
        Assert.Null(f.Registration.FirstUatExpiresUtc);
    }

    [Fact]
    public async Task Only_platform_admin_can_authorize_or_revoke_and_scope_is_checked()
    {
        await using var f = await Fixture.CreateAsync();
        f.Admin.IsAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.GrantAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Uat.RevokeAsync(f.Company, f.Admin.UserId!.Value, 1, default));
        f.Admin.IsAdmin = true;
        await Assert.ThrowsAsync<TeamsCallControlException>(() => f.Uat.AuthorizeAsync(f.Company, f.Admin.UserId!.Value,
            new(f.User, Guid.NewGuid(), 15, "Foreign meeting", f.Registration.ConcurrencyVersion), default));
        f.Options.FirstUatEnabled = false;
        await Assert.ThrowsAsync<TeamsCallControlException>(() => f.GrantAsync());
    }

    [Fact]
    public async Task Muting_audio_keeps_instance_protected_until_provider_termination()
    {
        await using var f = await Fixture.CreateAsync();
        await f.AddCallAsync();
        var call = await f.Db.TeamsMeetingCalls.SingleAsync();
        var runtime = new RecordingRuntime();
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(f.Db).BuildServiceProvider();
        var coordinator = new TeamsMeetingMediaCoordinator(services.GetRequiredService<IServiceScopeFactory>(),
            null!, null!, null!, null!, Microsoft.Extensions.Options.Options.Create(f.Options), runtime,
            f.Clock, NullLogger<TeamsMeetingMediaCoordinator>.Instance);
        await coordinator.StopAsync(call.Id, "organizer_muted", default);
        Assert.Empty(runtime.Released);
        call.MarkEnded("terminated", f.Clock.Now.UtcDateTime); await f.Db.SaveChangesAsync();
        await coordinator.StopAsync(call.Id, "provider_terminal", default);
        Assert.Equal([call.Id], runtime.Released);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public VirtualCompanyDbContext Db { get; }
        public Guid Company { get; } = Guid.NewGuid();
        public Guid User { get; } = Guid.NewGuid();
        public Guid Tenant { get; } = Guid.NewGuid();
        public SalesMeetingSession Meeting { get; private set; } = null!;
        public TeamsTenantRegistration Registration { get; private set; } = null!;
        public Agent Agent { get; private set; } = null!;
        public Catalog Catalog { get; } = new();
        public Audit Audit { get; } = new();
        public Clock Clock { get; } = new();
        public Demo Demo { get; } = new();
        public Admin Admin { get; } = new();
        public Drain Drain { get; } = new();
        public TeamsPresenterOptions Options { get; private set; } = null!;
        public TeamsMeetingPresenterService Presenters { get; private set; } = null!;
        public FirstTeamsUatService Uat { get; private set; } = null!;
        public TeamsPresenterRolloutPolicy Policy { get; private set; } = null!;
        private Fixture(SqliteConnection connection)
        {
            this.connection = connection;
            Db = new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, new CompanyContext(Company, User));
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var f = new Fixture(connection); await f.Db.Database.EnsureCreatedAsync();
            var now = f.Clock.Now.UtcDateTime;
            var invitation = new SalesMeetingInvitation(Guid.NewGuid(), f.Company, Guid.NewGuid(), null, null,
                Guid.NewGuid(), ExternalAccountProvider.Microsoft365, "organizer@example.test", "guest@example.test",
                null, "Marketing review", "Approved deck", now, now.AddHours(1), "UTC", null, true, f.User);
            f.Meeting = new(Guid.NewGuid(), f.Company, invitation.Id, invitation.LeadId, null, null, Guid.NewGuid(),
                "Review campaign", "Customer", 30, null, "meeting", SalesMeetingConsentStatus.Granted,
                SalesMeetingRetentionPolicy.Standard, 365, now, f.User, now);
            f.Agent = new(Guid.NewGuid(), f.Company, "maya", "Maya", "Marketing assistant", "Marketing", null,
                AgentSeniority.Mid, roleBrief: "Explain campaign results", communicationProfile: new Dictionary<string, JsonNode?>
                { ["tone"] = JsonValue.Create("calm and evidence-led") });
            f.Registration = new(Guid.NewGuid(), f.Company, f.Tenant, Guid.NewGuid(), Guid.NewGuid(),
                "teams_application_hosted", "Calls.AccessMedia.All;Calls.JoinGroupCall.All", f.User, now);
            f.Registration.RecordConsentVerification("verified", "Calls.AccessMedia.All;Calls.JoinGroupCall.All", null, f.User, now);
            f.Registration.AttestPolicy(true, f.User, now);
            f.Db.Companies.Add(new Company(f.Company, "Test company"));
            f.Db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), f.Company, f.User, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            f.Db.SalesMeetingInvitations.Add(invitation); f.Db.SalesMeetingSessions.Add(f.Meeting);
            f.Db.Agents.Add(f.Agent); f.Db.TeamsTenantRegistrations.Add(f.Registration); await f.Db.SaveChangesAsync();
            f.Options = new TeamsPresenterOptions {
                Enabled = true, PilotEnabled = true, FirstUatEnabled = true, LiveUatApproved = false,
                PilotCompanyIds = [f.Company.ToString("D")], PilotUserIds = [f.User.ToString("D")],
                AllowedTenantIds = [f.Tenant.ToString("D")], PackageVersion = "1.2.0", MinimumPackageVersion = "1.1.0",
                InstalledPackageVersion = "1.2.0", AppInstallationAttested = true, AutomatedEvidenceApproved = true,
                MonthlyCostLimit = 100, MaxActiveCallsGlobal = 2 };
            var configured = Microsoft.Extensions.Options.Options.Create(f.Options);
            f.Policy = new(f.Db, f.Demo, configured, f.Clock);
            f.Presenters = new(f.Db, f.Catalog, new AgentCommunicationProfileResolver(
                new DefaultAgentCommunicationProfileProvider(), NullLogger<AgentCommunicationProfileResolver>.Instance), f.Audit, f.Clock);
            f.Uat = new(f.Db, configured, f.Demo, f.Audit, f.Clock, f.Drain, f.Admin);
            return f;
        }
        public async Task AddCandidateAsync(Agent candidate)
        {
            await using var seed = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            seed.Agents.Add(candidate); await seed.SaveChangesAsync();
        }
        public Task<FirstTeamsUatDto> GrantAsync() => Uat.AuthorizeAsync(Company, Admin.UserId!.Value,
            new(User, Meeting.Id, 15, "Isolated speech and slide test", Registration.ConcurrencyVersion), default);
        public Task<TeamsPresenterRolloutDto> DecideAsync() => Policy.EvaluateAsync(Company, User, Tenant, default, Meeting.Id);
        public async Task AddCallAsync(Guid? meeting = null)
        {
            var call = new TeamsMeetingCall(Guid.NewGuid(), Company, meeting ?? Meeting.Id, Registration.Id, User,
                Guid.NewGuid().ToString("N"), 1, 1, "host-1", Clock.Now.UtcDateTime);
            call.BindPresenter(Agent.Id, Registration.FirstUatAuthorizedUtc);
            Db.TeamsMeetingCalls.Add(call); await Db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class CompanyContext(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId => companyId;
        public Guid? UserId => userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) { }
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { }
    }
    private sealed class Catalog : IAgentCapabilityCatalog
    {
        public string State = AgentCapabilityStates.Available;
        public string ToolName => SalesMeetingRealtimeService.Tools()[0].Name;
        public IReadOnlyList<AgentCapabilityManifest> ListManifests() => [];
        public Task<AgentCapabilityCatalogDto> GetEffectiveCatalogAsync(Guid companyId, Guid agentId, CancellationToken ct) =>
            Task.FromResult(new AgentCapabilityCatalogDto(companyId, agentId, "Maya", "active", "level_0", [], DateTime.UtcNow,
                "v1", "hash", [new(ToolName, "v1", "read", "sales", State, "test", "Test authority", null, null, [], [])]));
    }
    private sealed class Audit : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken ct) { Events.Add(auditEvent); return Task.CompletedTask; }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Demo : IDemoTenantExternalSideEffectPolicy
    {
        public bool Allowed = true;
        public Task<DemoExternalSideEffectDecision> EvaluateAsync(Guid companyId, string actionType, CancellationToken ct) =>
            Task.FromResult(new DemoExternalSideEffectDecision(Allowed, Allowed ? "not_demo" : "demo", "Test boundary."));
    }
    private sealed class Admin : ICurrentUserAccessor
    {
        public bool IsAdmin = true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public ClaimsPrincipal Principal => new(new ClaimsIdentity(IsAdmin ? [new Claim(CurrentUserClaimTypes.PlatformRole, "platform_admin")] : [], "test"));
        public AuthenticatedUserIdentity Current => new(true, UserId, null);
    }
    private sealed class RecordingRuntime : ITeamsMediaHostRuntime
    {
        public List<Guid> Released { get; } = [];
        public TeamsMediaHostRuntimeStatus GetStatus() => throw new NotSupportedException();
        public Task ReserveCallAsync(Guid callId, CancellationToken ct) => Task.CompletedTask;
        public Task ReleaseCallAsync(Guid callId, CancellationToken ct) { Released.Add(callId); return Task.CompletedTask; }
        public Task<TeamsMediaHostDrainResult> BeginDrainAsync(TimeSpan? deadline, string reason, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Drain : ITeamsMediaHostDrainExecutor
    {
        public List<Guid> Terminated { get; } = [];
        public Task ForceTerminateOwnedCallsAsync(string reason, CancellationToken ct) => Task.CompletedTask;
        public Task ForceTerminateCallAsync(Guid companyId, Guid callId, string reason, CancellationToken ct)
        { Terminated.Add(callId); return Task.CompletedTask; }
    }
}
