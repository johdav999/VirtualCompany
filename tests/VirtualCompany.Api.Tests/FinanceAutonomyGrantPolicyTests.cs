using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyGrantPolicyTests
{
    [Fact]
    public async Task Proactive_work_is_denied_without_an_explicit_active_grant()
    {
        await using var fixture = await Fixture.CreateAsync();

        var decision = await fixture.Service.EvaluateAsync(fixture.Evaluation(), default);

        Assert.False(decision.IsAllowed);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.GrantMissing, decision.ReasonCode);
    }

    [Fact]
    public async Task Read_grant_does_not_authorize_recommend_or_execute_and_pause_is_immediate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var grant = await fixture.CreateAndActivateReadGrantAsync();

        var allowed = await fixture.Service.EvaluateAsync(fixture.Evaluation(), default);
        var recommend = await fixture.Service.EvaluateAsync(
            fixture.Evaluation() with { ActionClass = "recommend" }, default);

        Assert.True(allowed.IsAllowed);
        Assert.False(recommend.IsAllowed);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.ActionDenied, recommend.ReasonCode);

        var control = await fixture.Service.SetControlAsync(fixture.CompanyId,
            new("agent", fixture.AgentId, null, "paused", "Operator pause"), default);
        var paused = await fixture.Service.EvaluateAsync(fixture.Evaluation(), default);

        Assert.Equal("paused", control.State);
        Assert.False(paused.IsAllowed);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.Paused, paused.ReasonCode);

        await fixture.Service.SetControlAsync(fixture.CompanyId,
            new("agent", fixture.AgentId, null, "active", "Resume after review", control.Version), default);
        Assert.True((await fixture.Service.EvaluateAsync(fixture.Evaluation(), default)).IsAllowed);
        Assert.NotNull(grant.ActiveVersionId);
    }

    [Fact]
    public async Task Version_edits_are_prospective_and_activation_retains_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.CreateAndActivateReadGrantAsync();
        var activeId = original.ActiveVersionId;
        var definition = fixture.ReadDefinition() with { MaximumRecordsPerRun = 5 };

        var edited = await fixture.Service.CreateVersionAsync(fixture.CompanyId, original.Id,
            new(definition, original.Version, "Narrow the batch"), default);

        Assert.Equal(activeId, edited.ActiveVersionId);
        Assert.Equal(2, edited.Versions.Count);
        Assert.Equal("prospective", edited.Versions.Single(x => x.VersionNumber == 2).Status);
        Assert.Equal("active", edited.Versions.Single(x => x.VersionNumber == 1).Status);

        var activated = await fixture.Service.ActivateAsync(fixture.CompanyId, original.Id,
            edited.Versions.Single(x => x.VersionNumber == 2).Id,
            new(edited.Version, "Reviewed replacement"), default);

        Assert.Equal("superseded", activated.Versions.Single(x => x.VersionNumber == 1).Status);
        Assert.Equal("active", activated.Versions.Single(x => x.VersionNumber == 2).Status);
    }

    [Fact]
    public async Task Elevated_grant_requires_independent_review_and_never_accepts_external_effects()
    {
        await using var fixture = await Fixture.CreateAsync();
        var execute = fixture.ReadDefinition() with
        {
            Level = FinanceAutonomyLevels.SupervisedInternalExecute,
            CapabilityId = FinanceAgentCoverageCapabilityIds.TransactionReview,
            AllowedActionClasses = ["execute"],
            AllowedTools = ["categorize_transaction"],
            ConfirmationBehavior = FinanceAutonomyConfirmationBehaviors.ApprovalRequired
        };
        var created = await fixture.Service.CreateAsync(fixture.CompanyId, new(execute, "Proposed"), default);
        var version = Assert.Single(created.Versions);
        Assert.Equal("pending_review", version.Status);

        await Assert.ThrowsAsync<FinanceAutonomyValidationException>(() =>
            fixture.Service.ActivateAsync(fixture.CompanyId, created.Id, version.Id,
                new(created.Version, "Self review"), default));

        fixture.CurrentUser.UserIdValue = Guid.NewGuid();
        var active = await fixture.Service.ActivateAsync(fixture.CompanyId, created.Id, version.Id,
            new(created.Version, "Independent bounded-risk review"), default);
        Assert.NotNull(active.ActiveVersionId);
        var executeDecision = await fixture.Service.EvaluateAsync(new FinanceAutonomyEvaluationRequest(
            fixture.CompanyId, fixture.AgentId, FinanceAgentCoverageCapabilityIds.TransactionReview,
            FinanceAutonomyTriggers.ManualReview, "execute", "categorize_transaction",
            1, null, fixture.Clock.UtcNow), default);
        Assert.True(executeDecision.IsAllowed);
        Assert.True(executeDecision.RequiresApproval);

        var external = execute with
        {
            CapabilityId = FinanceAgentCoverageCapabilityIds.InvoiceReview,
            AllowedTools = ["approve_invoice"]
        };
        await Assert.ThrowsAsync<FinanceAutonomyValidationException>(() =>
            fixture.Service.CreateAsync(fixture.CompanyId, new(external), default));
    }

    [Fact]
    public async Task Invalid_schedule_and_restricted_company_role_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invalidSchedule = fixture.ReadDefinition() with
        {
            AllowedTriggers = [FinanceAutonomyTriggers.Schedule],
            ScheduleExpression = "not a cron"
        };

        await Assert.ThrowsAsync<FinanceAutonomyValidationException>(() =>
            fixture.Service.CreateAsync(fixture.CompanyId, new(invalidSchedule), default));

        fixture.CurrentUser.Role = CompanyMembershipRole.Employee;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.CreateAsync(fixture.CompanyId,
                new(fixture.ReadDefinition()), default));
    }

    [Fact]
    public async Task Business_event_grants_require_a_reviewed_narrow_event_allowlist_and_bounded_trigger_limits()
    {
        await using var fixture = await Fixture.CreateAsync();
        var missingAllowlist = fixture.ReadDefinition() with
        {
            AllowedTriggers = [FinanceAutonomyTriggers.BusinessEvent]
        };
        await Assert.ThrowsAsync<FinanceAutonomyValidationException>(() =>
            fixture.Service.CreateAsync(fixture.CompanyId, new(missingAllowlist), default));

        var reviewed = missingAllowlist with
        {
            AllowedEventTypes = [FinanceAutonomyEventTypes.StaleCashEvidence],
            MinimumIntervalMinutes = 30,
            MaximumRunsPerWindow = 2,
            DebounceMinutes = 10,
            CatchUpBehavior = FinanceAutonomyCatchUpBehaviors.Latest,
            MaximumCatchUpWindows = 1,
            LateEventToleranceMinutes = 120
        };
        var created = await fixture.Service.CreateAsync(fixture.CompanyId, new(reviewed), default);
        var version = Assert.Single(created.Versions);
        Assert.Equal([FinanceAutonomyEventTypes.StaleCashEvidence], version.AllowedEventTypes);
        Assert.Equal(30, version.MinimumIntervalMinutes);
        Assert.Equal(2, version.MaximumRunsPerWindow);
    }

    [Fact]
    public async Task Risk_or_catalogue_change_fails_closed_until_a_new_version_is_reviewed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CreateAndActivateReadGrantAsync();
        fixture.Coverage.MakeToolHumanOnly("get_cash_balance");

        var decision = await fixture.Service.EvaluateAsync(fixture.Evaluation(), default);

        Assert.False(decision.IsAllowed);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.HumanOnly, decision.ReasonCode);
    }

    [Fact]
    public async Task Expiry_freshness_limits_emergency_stop_and_tenant_scope_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var grant = await fixture.CreateAndActivateReadGrantAsync();
        var version = grant.Versions.Single(x => x.Id == grant.ActiveVersionId);

        fixture.Clock.UtcNow = version.ExpiresUtc!.Value.AddMinutes(1);
        var expired = await fixture.Service.EvaluateAsync(fixture.Evaluation(), default);
        fixture.Clock.UtcNow = version.ExpiresUtc.Value.AddHours(-1);
        var stale = await fixture.Service.EvaluateAsync(
            fixture.Evaluation() with { EvidenceObservedUtc = fixture.Clock.UtcNow.AddHours(-3) }, default);
        var overLimit = await fixture.Service.EvaluateAsync(
            fixture.Evaluation() with { RecordCount = 11 }, default);

        Assert.Equal(FinanceAutonomyDecisionReasonCodes.GrantExpired, expired.ReasonCode);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.EvidenceStale, stale.ReasonCode);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.LimitExceeded, overLimit.ReasonCode);

        await fixture.Service.SetControlAsync(fixture.CompanyId,
            new("capability", null, FinanceAgentCoverageCapabilityIds.DailyCash,
                "emergency_stopped", "Incorrect source evidence"), default);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.EmergencyStopped,
            (await fixture.Service.EvaluateAsync(fixture.Evaluation(), default)).ReasonCode);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.GetAsync(Guid.NewGuid(), grant.Id, default));
    }

    [Fact]
    public void Api_separates_safe_queries_from_authorized_mutations()
    {
        var controllerPolicy = Assert.Single(typeof(FinanceAutonomyController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(CompanyPolicies.FinanceView, controllerPolicy.Policy);

        foreach (var methodName in new[]
                 {
                     nameof(FinanceAutonomyController.CreateAsync),
                     nameof(FinanceAutonomyController.CreateVersionAsync),
                     nameof(FinanceAutonomyController.ActivateAsync),
                     nameof(FinanceAutonomyController.RevokeAsync),
                     nameof(FinanceAutonomyController.SetControlAsync)
                 })
        {
            var method = typeof(FinanceAutonomyController).GetMethod(methodName);
            var policy = Assert.Single(method!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>());
            Assert.Equal(CompanyPolicies.CompanyManager, policy.Policy);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            VirtualCompanyDbContext db,
            FinanceAutonomyGrantService service,
            MutableMembershipResolver currentUser,
            MutableCoverage coverage,
            MutableTimeProvider clock,
            Guid companyId,
            Guid agentId)
        {
            Db = db;
            Service = service;
            CurrentUser = currentUser;
            Coverage = coverage;
            Clock = clock;
            CompanyId = companyId;
            AgentId = agentId;
        }

        public VirtualCompanyDbContext Db { get; }
        public FinanceAutonomyGrantService Service { get; }
        public MutableMembershipResolver CurrentUser { get; }
        public MutableCoverage Coverage { get; }
        public MutableTimeProvider Clock { get; }
        public Guid CompanyId { get; }
        public Guid AgentId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var agent = new Agent(Guid.NewGuid(), companyId, "laura-finance-agent", "Laura",
                "Finance Manager", "Finance", null, AgentSeniority.Senior, AgentStatus.Active,
                AgentAutonomyLevel.Guided);
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            db.Companies.Add(new Company(companyId, "Autonomy test company"));
            db.Agents.Add(agent);
            await db.SaveChangesAsync();

            var authority = AgentEffectiveAuthorityResolver.Resolve(agent, new StaticCompanyToolRegistry());
            var resolver = new MutableAuthorityResolver(authority);
            var coverage = new MutableCoverage();
            var currentUser = new MutableMembershipResolver(companyId, actorId);
            var clock = new MutableTimeProvider(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
            var service = new FinanceAutonomyGrantService(
                db, currentUser, resolver, coverage, new CollectingAuditWriter(),
                new CronosScheduleExpressionValidator(), clock);
            return new Fixture(db, service, currentUser, coverage, clock, companyId, agent.Id);
        }

        public FinanceAutonomyGrantDefinition ReadDefinition() =>
            new(AgentId, FinanceAgentCoverageCapabilityIds.DailyCash, FinanceAutonomyLevels.ReadMonitor,
                [FinanceAutonomyTriggers.ManualReview], ["read"], ["get_cash_balance"],
                10, null, 1, null, "UTC", "00:00", "23:59", 60,
                FinanceAutonomyConfirmationBehaviors.NoConfirmation, "company_owner",
                Clock.UtcNow.AddMinutes(-1), Clock.UtcNow.AddHours(2));

        public FinanceAutonomyEvaluationRequest Evaluation() =>
            new(CompanyId, AgentId, FinanceAgentCoverageCapabilityIds.DailyCash,
                FinanceAutonomyTriggers.ManualReview, "read", "get_cash_balance",
                1, null, Clock.UtcNow);

        public async Task<FinanceAutonomyGrantDto> CreateAndActivateReadGrantAsync()
        {
            var created = await Service.CreateAsync(CompanyId, new(ReadDefinition(), "Low-risk monitoring"), default);
            var version = Assert.Single(created.Versions);
            return await Service.ActivateAsync(CompanyId, created.Id, version.Id,
                new(created.Version, "Explicit activation"), default);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MutableMembershipResolver(Guid companyId, Guid userId) : ICompanyMembershipContextResolver
    {
        public Guid UserIdValue { get; set; } = userId;
        public CompanyMembershipRole Role { get; set; } = CompanyMembershipRole.Owner;
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) =>
            ResolveAsync(companyId, cancellationToken);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid requestedCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedCompanyMembershipContext?>(requestedCompanyId == companyId
                ? new ResolvedCompanyMembershipContext(Guid.NewGuid(), companyId, UserIdValue,
                    "Autonomy test company", Role, CompanyMembershipStatus.Active, "UTC", "SEK")
                : null);
    }

    private sealed class MutableAuthorityResolver(AgentEffectiveAuthorityDto authority) : IAgentEffectiveAuthorityResolver
    {
        public AgentEffectiveAuthorityDto Authority { get; set; } = authority;
        public Task<AgentEffectiveAuthorityDto> ResolveAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken) =>
            Task.FromResult(Authority);
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    private sealed class MutableCoverage : IFinanceAgentCoverageCatalogue
    {
        private IReadOnlyList<FinanceAgentCoverageCapabilityManifest> _manifests = FinanceAgentCoverageCatalogue.Manifests;
        public IReadOnlyList<FinanceAgentCoverageCapabilityManifest> ListManifests() => _manifests;
        public Task<FinanceAgentEffectiveCoverageDto> GetEffectiveCoverageAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void MakeToolHumanOnly(string tool)
        {
            _manifests = _manifests.Select(capability => capability with
            {
                Operations = capability.Operations.Select(operation =>
                    string.Equals(operation.ToolName, tool, StringComparison.OrdinalIgnoreCase)
                        ? operation with { SupportState = FinanceAgentCoverageSupportStates.HumanOnly }
                        : operation).ToArray()
            }).ToArray();
        }
    }

    private sealed class CollectingAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
