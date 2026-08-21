using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingAdministrationServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly InitialFiscalYearStart = new(2026, 1, 1);

    [Fact]
    public async Task Complete_setup_is_atomic_idempotent_and_applies_the_country_neutral_template()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var command = fixture.CreateSetupCommand();

        var first = await fixture.Service.CompleteSetupAsync(command, CancellationToken.None);
        var replay = await fixture.Service.CompleteSetupAsync(command, CancellationToken.None);

        Assert.False(first.WasAlreadyApplied);
        Assert.True(replay.WasAlreadyApplied);
        Assert.True(first.SetupStatus.IsReady);
        Assert.False(first.SetupStatus.IsCountrySpecificComplianceConfigured);
        Assert.Equal(6, await fixture.Context.FinanceAccounts.CountAsync());
        Assert.Equal(6, await fixture.Context.AccountingConfigurationAccountRoles.CountAsync());
        Assert.Equal(12, await fixture.Context.FiscalPeriods.CountAsync());
        Assert.Equal(5, await fixture.Context.VoucherSeries.CountAsync());
        Assert.Single(await fixture.Context.AccountingConfigurations.ToListAsync());

        var auditActions = await fixture.Context.AuditEvents.Select(audit => audit.Action).ToListAsync();
        Assert.Contains(AuditEventActions.AccountingSetupCompleted, auditActions);
        Assert.Contains(AuditEventActions.AccountingFiscalYearCreated, auditActions);
    }

    [Fact]
    public async Task Fiscal_year_creation_completes_missing_months_and_then_replays_without_duplicates()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        await fixture.Service.CompleteSetupAsync(fixture.CreateSetupCommand(), CancellationToken.None);
        var nextYearStart = InitialFiscalYearStart.AddYears(1);
        fixture.Context.FiscalPeriods.Add(new FiscalPeriod(
            Guid.NewGuid(),
            fixture.CompanyId,
            "Jan 2027",
            ToUtc(nextYearStart),
            ToUtc(nextYearStart.AddMonths(1)),
            createdUtc: NowUtc,
            updatedUtc: NowUtc));
        await fixture.Context.SaveChangesAsync();

        var command = new CreateAccountingFiscalYearCommand(
            fixture.CompanyId,
            nextYearStart,
            fixture.ActorId,
            $"fiscal-year:{fixture.CompanyId:N}:{nextYearStart:yyyyMMdd}");
        var completed = await fixture.Service.CreateFiscalYearAsync(command, CancellationToken.None);
        var replay = await fixture.Service.CreateFiscalYearAsync(command, CancellationToken.None);

        Assert.False(completed.WasAlreadyPresent);
        Assert.True(replay.WasAlreadyPresent);
        Assert.Equal(12, completed.FiscalYear.Periods.Count);
        Assert.Equal(12, replay.FiscalYear.Periods.Count);
        Assert.Equal(12, await fixture.Context.FiscalPeriods.CountAsync(period =>
            period.StartUtc >= ToUtc(nextYearStart) && period.StartUtc < ToUtc(nextYearStart.AddYears(1))));
    }

    [Fact]
    public async Task Protected_setup_account_cannot_be_deactivated()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        await fixture.Service.CompleteSetupAsync(fixture.CreateSetupCommand(), CancellationToken.None);
        var cash = await fixture.Context.FinanceAccounts.SingleAsync(account => account.Code == "1000");

        var exception = await Assert.ThrowsAsync<AccountingConfigurationException>(() =>
            fixture.Service.DeactivateAccountAsync(
                new DeactivateAccountingAccountCommand(
                    fixture.CompanyId,
                    cash.Id,
                    new DateOnly(2026, 12, 31),
                    cash.UpdatedUtc,
                    fixture.ActorId),
                CancellationToken.None));

        Assert.Equal(AccountingConfigurationReasonCodes.AccountProtected, exception.ReasonCode);
        fixture.Context.ChangeTracker.Clear();
        Assert.True((await fixture.Context.FinanceAccounts.SingleAsync(account => account.Id == cash.Id)).IsPostingEnabled);
    }

    private static DateTime ToUtc(DateOnly value) => value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private sealed class AdministrationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private AdministrationFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext context,
            AccountingAdministrationService service,
            Guid companyId,
            Guid actorId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            CompanyId = companyId;
            ActorId = actorId;
        }

        public VirtualCompanyDbContext Context { get; }
        public AccountingAdministrationService Service { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }

        public CompleteAccountingSetupCommand CreateSetupCommand() => new(
            CompanyId,
            "USD",
            InitialFiscalYearStart,
            AccountingPolicyPackDefaults.CountryNeutralPackKey,
            AccountingPolicyPackDefaults.CountryNeutralVersion,
            "generic-accrual",
            AccountRoleCodeAssignments: null,
            ActorId,
            $"accounting-setup:{CompanyId:N}",
            "accounting-administration-test");

        public static async Task<AdministrationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var accessor = new TestCompanyContextAccessor(companyId, actorId);
            var context = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                accessor);
            await context.Database.EnsureCreatedAsync();
            context.Companies.Add(new Company(companyId, "Accounting administration company"));
            await context.SaveChangesAsync();

            var pack = new CountryNeutralAccountingPolicyPack();
            var resolver = new AccountingPolicyPackResolver([pack]);
            var clock = new FixedTimeProvider(new DateTimeOffset(NowUtc));
            var auditWriter = new AuditEventWriter(context);
            var configurationService = new AccountingConfigurationService(context, resolver, auditWriter, clock);
            var service = new AccountingAdministrationService(context, resolver, configurationService, auditWriter, clock);
            return new AdministrationFixture(connection, context, service, companyId, actorId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
