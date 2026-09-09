using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class DemoScenarioServiceTests
{
    [Fact]
    public async Task Preview_and_repeated_reset_are_exact_deterministic_and_preserve_audit_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provisioned = await fixture.Service.ProvisionAsync(fixture.UserId,
            new("northstar-sales", 1, true), CancellationToken.None);
        var preview = await fixture.Service.PreviewResetAsync(provisioned.CompanyId, fixture.UserId,
            "northstar-sales", 1, CancellationToken.None);

        Assert.True(preview.CanReset);
        Assert.True(preview.AuditHistoryPreserved);
        Assert.Equal(provisioned.CompanyId, preview.CompanyId);
        Assert.Contains(preview.DisabledIntegrations, x => x.Contains("Payments", StringComparison.Ordinal));
        Assert.Equal(1, preview.AffectedRecords.Single(x => x.RecordClass == "lead").ExistingCount);

        var initialLeadId = await fixture.Db.Leads.IgnoreQueryFilters()
            .Where(x => x.CompanyId == provisioned.CompanyId).Select(x => x.Id).SingleAsync();
        var reset1 = await fixture.Service.ResetAsync(provisioned.CompanyId, fixture.UserId,
            new("northstar-sales", 1, preview.CompanyName, preview.PreviewToken), CancellationToken.None);
        var preview2 = await fixture.Service.PreviewResetAsync(provisioned.CompanyId, fixture.UserId,
            "northstar-sales", 1, CancellationToken.None);
        var reset2 = await fixture.Service.ResetAsync(provisioned.CompanyId, fixture.UserId,
            new("northstar-sales", 1, preview2.CompanyName, preview2.PreviewToken), CancellationToken.None);
        var lead = await fixture.Db.Leads.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == provisioned.CompanyId);

        Assert.Equal(initialLeadId, lead.Id);
        Assert.Equal(SalesStatuses.Open, lead.Status);
        Assert.Equal(SalesPipelineStage.NewStageId, lead.PipelineStageId);
        Assert.Equal(3, reset2.ResetGeneration);
        Assert.Equal(0, reset1.CurrentStep);
        Assert.True(fixture.Audit.Events.Count(x => x.Action == AuditEventActions.DemoScenarioReset) >= 2);
    }

    [Fact]
    public async Task Reset_rejects_non_demo_wrong_company_and_stale_preview_before_mutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var demo = await fixture.Service.ProvisionAsync(fixture.UserId, new("northstar-sales", 1, true), CancellationToken.None);
        var preview = await fixture.Service.PreviewResetAsync(demo.CompanyId, fixture.UserId, "northstar-sales", 1, CancellationToken.None);

        var stale = await Assert.ThrowsAsync<DemoScenarioException>(() => fixture.Service.ResetAsync(
            demo.CompanyId, fixture.UserId, new("northstar-sales", 1, preview.CompanyName, new string('0', 64)), CancellationToken.None));
        Assert.Equal(DemoScenarioProblemCodes.ResetPreviewStale, stale.Code);

        var normal = new Company(Guid.NewGuid(), "Real Customer Company");
        var run = new DemoScenarioRun(Guid.NewGuid(), normal.Id, "northstar-sales", 1, fixture.UserId, DateTime.UtcNow);
        fixture.Db.AddRange(normal, run, new CompanyMembership(Guid.NewGuid(), normal.Id, fixture.UserId,
            CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        await fixture.Db.SaveChangesAsync();

        var rejected = await Assert.ThrowsAsync<DemoScenarioException>(() => fixture.Service.PreviewResetAsync(
            normal.Id, fixture.UserId, "northstar-sales", 1, CancellationToken.None));
        Assert.Equal(DemoScenarioProblemCodes.NotDemoTenant, rejected.Code);
        Assert.Equal("Real Customer Company", (await fixture.Db.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == normal.Id)).Name);
    }

    [Fact]
    public async Task Typed_commands_are_ordered_idempotent_and_isolated_between_demo_companies()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.ProvisionAsync(fixture.UserId, new("northstar-sales", 1, true), CancellationToken.None);
        var second = await fixture.Service.ProvisionAsync(fixture.UserId, new("northstar-sales", 1, true), CancellationToken.None);
        await fixture.LinkForTestAsync(first.CompanyId);
        await fixture.LinkForTestAsync(second.CompanyId);
        await fixture.Service.StartAsync(first.CompanyId, fixture.UserId, CancellationToken.None);
        await fixture.Service.StartAsync(second.CompanyId, fixture.UserId, CancellationToken.None);

        var denied = await Assert.ThrowsAsync<DemoScenarioException>(() => fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId,
            new("demo.external.send_email", "blocked-1"), CancellationToken.None));
        Assert.Equal(DemoScenarioProblemCodes.CommandNotAllowed, denied.Code);
        Assert.Contains(fixture.Audit.Events, x => x.Action == AuditEventActions.DemoScenarioCommandRejected);

        var qualified = await fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId,
            new("demo.sales.qualify_lead", "step-1"), CancellationToken.None);
        var duplicate = await fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId,
            new("demo.sales.qualify_lead", "step-1"), CancellationToken.None);
        Assert.Equal(qualified.EntityId, duplicate.EntityId);
        Assert.Equal(1, await fixture.Db.DemoScenarioCommandExecutions.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == first.CompanyId && x.ResetGeneration == 1));

        var secondLead = await fixture.Db.Leads.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == second.CompanyId);
        Assert.Equal(SalesStatuses.Open, secondLead.Status);

        await fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId,
            new("demo.sales.convert_lead", "step-2"), CancellationToken.None);
        var completed = await fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId,
            new("demo.sales.move_deal_to_proposal", "step-3"), CancellationToken.None);
        Assert.Equal("completed", completed.Status.Status);
        var dealId = completed.EntityId;

        var resetPreview = await fixture.Service.PreviewResetAsync(first.CompanyId, fixture.UserId, "northstar-sales", 1, CancellationToken.None);
        await fixture.Service.ResetAsync(first.CompanyId, fixture.UserId,
            new("northstar-sales", 1, resetPreview.CompanyName, resetPreview.PreviewToken), CancellationToken.None);
        await fixture.Service.StartAsync(first.CompanyId, fixture.UserId, CancellationToken.None);
        await fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId, new("demo.sales.qualify_lead", "step-1"), CancellationToken.None);
        await fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId, new("demo.sales.convert_lead", "step-2"), CancellationToken.None);
        var repeated = await fixture.Service.ExecuteAsync(first.CompanyId, fixture.UserId,
            new("demo.sales.move_deal_to_proposal", "step-3"), CancellationToken.None);

        Assert.Equal(dealId, repeated.EntityId);
        Assert.Equal(6, await fixture.Db.DemoScenarioCommandExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == first.CompanyId));
    }

    [Fact]
    public async Task Demo_external_side_effect_policy_blocks_every_action_but_not_normal_tenants()
    {
        await using var fixture = await Fixture.CreateAsync();
        var demo = await fixture.Service.ProvisionAsync(fixture.UserId, new("northstar-sales", 1, true), CancellationToken.None);
        var normal = new Company(Guid.NewGuid(), "Normal Company");
        fixture.Db.Companies.Add(normal);
        await fixture.Db.SaveChangesAsync();
        var policy = new DemoTenantExternalSideEffectPolicy(fixture.Db);

        var blocked = await policy.EvaluateAsync(demo.CompanyId, "payment.submit", CancellationToken.None);
        var allowed = await policy.EvaluateAsync(normal.Id, "payment.submit", CancellationToken.None);

        Assert.False(blocked.Allowed);
        Assert.Equal(DemoScenarioProblemCodes.ExternalSideEffectBlocked, blocked.ReasonCode);
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public async Task Demo_controls_require_privileged_membership_and_exact_company_meeting()
    {
        await using var fixture = await Fixture.CreateAsync();
        var demo = await fixture.Service.ProvisionAsync(fixture.UserId, new("northstar-sales", 1, true), CancellationToken.None);
        var employeeId = Guid.NewGuid();
        fixture.Db.Users.Add(new User(employeeId, "demo.employee@example.invalid", "Demo Employee", "test", employeeId.ToString("N")));
        fixture.Db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), demo.CompanyId, employeeId,
            CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
        await fixture.Db.SaveChangesAsync();

        var denied = await Assert.ThrowsAsync<DemoScenarioException>(() => fixture.Service.PreviewResetAsync(
            demo.CompanyId, employeeId, "northstar-sales", 1, CancellationToken.None));
        var wrongMeeting = await Assert.ThrowsAsync<DemoScenarioException>(() => fixture.Service.LinkMeetingAsync(
            demo.CompanyId, fixture.UserId, new(Guid.NewGuid(), "northstar-sales", 1), CancellationToken.None));

        Assert.Equal(DemoScenarioProblemCodes.PermissionDenied, denied.Code);
        Assert.Equal(DemoScenarioProblemCodes.MeetingMismatch, wrongMeeting.Code);
    }

    [Fact]
    public async Task Run_concurrency_token_rejects_stale_parallel_updates()
    {
        var databaseName = $"demo-concurrency-{Guid.NewGuid():N}";
        var connectionString = $"Data Source=file:{databaseName}?mode=memory&cache=shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connectionString).Options;
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var seed = new VirtualCompanyDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            var company = new Company(companyId, "Concurrency Demo Company");
            company.MarkAsDemoTenant("northstar-sales", 1);
            seed.AddRange(company, new DemoScenarioRun(Guid.NewGuid(), companyId, "northstar-sales", 1, userId, DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var first = new VirtualCompanyDbContext(options);
        await using var second = new VirtualCompanyDbContext(options);
        var firstRun = await first.DemoScenarioRuns.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
        var staleRun = await second.DemoScenarioRuns.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
        firstRun.Reset(DateTime.UtcNow);
        staleRun.Reset(DateTime.UtcNow.AddSeconds(1));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, Guid userId,
            CollectingAuditWriter audit, DemoScenarioService service)
        {
            _connection = connection;
            Db = db;
            UserId = userId;
            Audit = audit;
            Service = service;
        }

        public VirtualCompanyDbContext Db { get; }
        public Guid UserId { get; }
        public CollectingAuditWriter Audit { get; }
        public DemoScenarioService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            var userId = Guid.NewGuid();
            db.Users.Add(new User(userId, "demo.owner@example.invalid", "Demo Owner", "test", userId.ToString("N")));
            await db.SaveChangesAsync();
            var audit = new CollectingAuditWriter();
            var service = new DemoScenarioService(
                db,
                new DemoScenarioCatalog(),
                new StubOnboardingService(db, userId),
                audit,
                Options.Create(new DemoScenarioOptions { Enabled = true, ProvisioningEnabled = true, ResetEnabled = true }));
            return new Fixture(connection, db, userId, audit, service);
        }

        public async Task LinkForTestAsync(Guid companyId)
        {
            var run = await Db.DemoScenarioRuns.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            var lead = await Db.Leads.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            var customer = await Db.CustomerCompanies.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            var contact = await Db.Contacts.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
            var meetingId = Guid.NewGuid();
            Db.SalesMeetingSessions.Add(new SalesMeetingSession(
                meetingId,
                companyId,
                Guid.NewGuid(),
                lead.Id,
                null,
                contact.Id,
                customer.Id,
                "Controlled product demonstration",
                "Synthetic buyer",
                30,
                "northstar-sales",
                $"demo-{meetingId:N}",
                SalesMeetingConsentStatus.Pending,
                SalesMeetingRetentionPolicy.Standard,
                365,
                DateTime.UtcNow,
                UserId,
                DateTime.UtcNow));
            run.LinkMeeting(meetingId, UserId, DateTime.UtcNow);
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
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

    private sealed class StubOnboardingService(VirtualCompanyDbContext db, Guid userId) : ICompanyOnboardingService
    {
        public async Task<CreateCompanyResultDto> CreateCompanyAsync(CreateCompanyCommand command, CancellationToken cancellationToken)
        {
            var company = new Company(Guid.NewGuid(), command.Name);
            company.UpdateWorkspaceProfile(command.Name, command.Industry, command.BusinessType, command.Timezone,
                command.Currency, command.Language, command.ComplianceRegion);
            db.Companies.Add(company);
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company.Id, userId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            await db.SaveChangesAsync(cancellationToken);
            return new(company.Id, company.Name, $"/dashboard?companyId={company.Id:D}", []);
        }

        public Task<OnboardingTemplateRecommendationDto?> GetRecommendedDefaultsAsync(GetOnboardingTemplateRecommendationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OnboardingTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto?> GetProgressAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto?> GetProgressAsync(Guid companyId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto> CreateWorkspaceAsync(CreateCompanyWorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto> SaveProgressAsync(SaveCompanyOnboardingProgressRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompanyOnboardingProgressDto> AbandonOnboardingAsync(AbandonCompanyOnboardingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompleteCompanyOnboardingResultDto> CompleteOnboardingAsync(CompleteCompanyOnboardingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
