using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchAssessmentServiceTests
{
    [Fact]
    public async Task Assessment_is_idempotent_tenant_safe_and_resumes_to_durable_results()
    {
        await using var fixture = await Fixture.CreateAsync();
        var providerSwitch = await fixture.SwitchService.CreateAsync(fixture.CreateSwitch(), CancellationToken.None);
        var command = new StartAccountingProviderSwitchAssessmentCommand(fixture.CompanyId, providerSwitch.Id,
            providerSwitch.Version, fixture.OwnerId, "assessment-correlation", "assessment-idempotency");

        var queued = await fixture.AssessmentService.StartAsync(command, CancellationToken.None);
        var duplicate = await fixture.AssessmentService.StartAsync(command, CancellationToken.None);
        Assert.Equal(queued.Id, duplicate.Id);
        Assert.Equal("queued", queued.Status);

        for (var index = 0; index < 12; index++)
            Assert.Equal(1, await fixture.AssessmentService.RunDueAsync(CancellationToken.None));
        var partial = await fixture.AssessmentService.GetAsync(new(fixture.CompanyId, providerSwitch.Id, queued.Id), CancellationToken.None);
        Assert.InRange(partial.ProgressPercent, 1, 99);

        var resumed = fixture.CreateAssessmentService();
        for (var index = 0; index < 40; index++)
        {
            var state = await resumed.GetAsync(new(fixture.CompanyId, providerSwitch.Id, queued.Id), CancellationToken.None);
            if (state.Status == "completed") break;
            Assert.Equal(1, await resumed.RunDueAsync(CancellationToken.None));
        }

        var completed = await resumed.GetAsync(new(fixture.CompanyId, providerSwitch.Id, queued.Id), CancellationToken.None);
        Assert.Equal("completed", completed.Status);
        Assert.Equal(100, completed.ProgressPercent);
        Assert.Equal(44, completed.Capabilities.Count);
        Assert.Equal(34, completed.Datasets.Count);
        Assert.Equal(34, await fixture.Context.AccountingProviderSwitchDatasets.IgnoreQueryFilters().CountAsync());
        Assert.All(completed.Datasets, dataset => Assert.Equal(64, dataset.IntegrityHash.Length));
        Assert.Equal(AccountingProviderSwitchStatuses.ReadyForPlanning,
            (await fixture.SwitchService.GetAsync(new(fixture.CompanyId, providerSwitch.Id), CancellationToken.None)).Status);

        var crossCompany = await Assert.ThrowsAsync<AccountingAuthorityException>(() => resumed.GetAsync(
            new(Guid.NewGuid(), providerSwitch.Id, queued.Id), CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.AssessmentNotFound, crossCompany.ReasonCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        private readonly SqliteConnection _connection;
        private readonly IAuditEventWriter _audit;
        private readonly IAccountingProviderSwitchAdapterResolver _resolver;
        private readonly IOptions<AccountingProviderSwitchAssessmentWorkerOptions> _options;
        private readonly TimeProvider _time;

        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context, Guid companyId, Guid ownerId,
            Guid periodId, AccountingProviderSwitchService switchService, IAuditEventWriter audit,
            IAccountingProviderSwitchAdapterResolver resolver, IOptions<AccountingProviderSwitchAssessmentWorkerOptions> options,
            TimeProvider time)
        {
            _connection = connection;
            Context = context;
            CompanyId = companyId;
            OwnerId = ownerId;
            PeriodId = periodId;
            SwitchService = switchService;
            _audit = audit;
            _resolver = resolver;
            _options = options;
            _time = time;
            AssessmentService = CreateAssessmentService();
        }

        public VirtualCompanyDbContext Context { get; }
        public Guid CompanyId { get; }
        public Guid OwnerId { get; }
        public Guid PeriodId { get; }
        public AccountingProviderSwitchService SwitchService { get; }
        public AccountingProviderSwitchAssessmentService AssessmentService { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var context = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var company = Guid.NewGuid();
            var owner = Guid.NewGuid();
            var period = Guid.NewGuid();
            context.Companies.Add(new Company(company, "Assessment company"));
            context.Users.Add(new User(owner, $"{owner:N}@example.com", "Assessment owner", "test", owner.ToString("N")));
            context.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, owner,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            context.FiscalPeriods.Add(new FiscalPeriod(period, company, "September 2026",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
            context.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(Guid.NewGuid(), company,
                new DateOnly(2026, 1, 1), null, AccountingAuthorityValues.InternalLedger, null,
                owner, "Current authority.", Now));
            await context.SaveChangesAsync();
            var time = new FixedTimeProvider(Now);
            var audit = new AuditEventWriter(context);
            var switchService = new AccountingProviderSwitchService(context, audit, time);
            var resolver = new TestResolver(new DeterministicAdapter());
            var options = Options.Create(new AccountingProviderSwitchAssessmentWorkerOptions
            {
                ClaimBatchSize = 1,
                PageSize = 10,
                LeaseSeconds = 60,
                MaximumAttempts = 3
            });
            return new Fixture(connection, context, company, owner, period, switchService, audit, resolver, options, time);
        }

        public AccountingProviderSwitchAssessmentService CreateAssessmentService() => new(Context, _resolver,
            new AccountingProviderSwitchGapPolicy(), SwitchService, _audit, _options, _time,
            NullLogger<AccountingProviderSwitchAssessmentService>.Instance);

        public CreateAccountingProviderSwitchCommand CreateSwitch() => new(CompanyId, "internal", null,
            "external", "fortnox", PeriodId, AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
            "Assess the target.", OwnerId, null, OwnerId, "create-assessment-switch");

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestResolver(IAccountingProviderSwitchAdapter adapter) : IAccountingProviderSwitchAdapterResolver
    {
        public IAccountingProviderSwitchAdapter GetRequired(string endpointKind, string? providerKey) => adapter;
    }

    private sealed class DeterministicAdapter : IAccountingProviderSwitchAdapter
    {
        public bool CanHandle(string endpointKind, string? providerKey) => true;
        public Task<ProviderMigrationCapabilityProfile> GetCapabilityProfileAsync(Guid companyId,
            AccountingProviderSwitchEndpointDto endpoint, string correlationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderMigrationCapabilityProfile(endpoint.Kind, endpoint.ProviderKey,
                AccountingProviderSwitchCapabilityKeys.All.Select(x => new ProviderMigrationCapability(x, "supported", "Test capability.")).ToArray(),
                new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc)));
        public Task<ProviderSwitchInventoryExtractionResult> ExtractInventoryAsync(
            ProviderSwitchInventoryExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderSwitchInventoryExtractionResult(request.DatasetKey, "available", "supported",
                1, 10m, "SEK", null, "test-v1", new string('a', 64), "{}", true));
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
