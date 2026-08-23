using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchInternalReadinessPolicyTests
{
    [Fact]
    public async Task Missing_native_configuration_accounts_and_voucher_series_block_readiness()
    {
        await using var fixture = await Fixture.CreateAsync(configured: false);
        var readiness = await fixture.Policy.EvaluateAsync(
            new(fixture.CompanyId, fixture.SwitchId, fixture.PlanId), CancellationToken.None);

        Assert.False(readiness.IsReady);
        Assert.Contains(readiness.Checks, x => x.ReasonCode == AccountingProviderSwitchPreparationReasonCodes.ConfigurationMissing);
        Assert.Contains(readiness.Checks, x => x.ReasonCode == AccountingProviderSwitchPreparationReasonCodes.VoucherSeriesMissing);
        Assert.Contains(readiness.Checks, x => x.ReasonCode == AccountingProviderSwitchPreparationReasonCodes.ControlAccountsMissing);
    }

    [Fact]
    public async Task Complete_country_neutral_setup_is_ready_with_explicit_non_blocking_compliance_disclosure()
    {
        await using var fixture = await Fixture.CreateAsync(configured: true);
        var readiness = await fixture.Policy.EvaluateAsync(
            new(fixture.CompanyId, fixture.SwitchId, fixture.PlanId), CancellationToken.None);

        Assert.True(readiness.IsReady, string.Join(" | ", readiness.Checks
            .Where(x => x.IsBlocking && !x.IsReady).Select(x => $"{x.CheckKey}:{x.Explanation}")));
        Assert.False(readiness.IsStatutoryComplianceValidated);
        var disclosure = Assert.Single(readiness.Checks, x => x.CheckKey == "policy_compliance");
        Assert.False(disclosure.IsBlocking);
        Assert.False(disclosure.IsReady);
        Assert.Equal(AccountingProviderSwitchPreparationReasonCodes.PolicyComplianceDisclosure,
            disclosure.ReasonCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context,
            AccountingProviderSwitchInternalReadinessPolicy policy, Guid companyId, Guid switchId, Guid planId)
        { _connection = connection; Context = context; Policy = policy; CompanyId = companyId;
            SwitchId = switchId; PlanId = planId; }
        public VirtualCompanyDbContext Context { get; }
        public AccountingProviderSwitchInternalReadinessPolicy Policy { get; }
        public Guid CompanyId { get; }
        public Guid SwitchId { get; }
        public Guid PlanId { get; }

        public static async Task<Fixture> CreateAsync(bool configured)
        {
            var companyId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options, new TestCompanyContextAccessor(companyId, ownerId));
            await db.Database.EnsureCreatedAsync();
            var switchId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            db.Companies.Add(new Company(companyId, "Readiness company"));
            db.Users.Add(new User(ownerId, "owner@example.com", "Owner", "test", ownerId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "September 2026",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
            var providerSwitch = new AccountingProviderSwitch(switchId, companyId,
                new("external", "fortnox"), new("internal", null), periodId,
                AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems, "Move accounting.",
                ownerId, null, ownerId, "switch", Now);
            db.AccountingProviderSwitches.Add(providerSwitch);

            var packs = new IAccountingPolicyPack[] { new CountryNeutralAccountingPolicyPack(),
                new CountryNeutralBankingAccountingPolicyPack() };
            var resolver = new AccountingPolicyPackResolver(packs);
            if (configured)
            {
                var configuration = new AccountingConfiguration(Guid.NewGuid(), companyId, "SEK", 1, 1,
                    AccountingPolicyPackDefaults.CountryNeutralPackKey,
                    AccountingPolicyPackDefaults.CountryNeutralVersion, new DateOnly(2026, 1, 1), 2,
                    AccountingRoundingModeValues.MidpointToEven, ownerId, Now);
                configuration.SetSetupState(AccountingSetupStateValues.Ready, ownerId, Now);
                configuration.SetAuthority(AccountingAuthorityValues.ExternalProvider, ownerId, Now);
                db.AccountingConfigurations.Add(configuration);
                var roles = new[]
                {
                    ("cash", "1000", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit),
                    ("accounts_receivable", "1100", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit),
                    ("accounts_payable", "2000", FinanceAccountClassValues.Liability, FinanceNormalBalanceValues.Credit),
                    ("equity", "3000", FinanceAccountClassValues.Equity, FinanceNormalBalanceValues.Credit),
                    ("revenue", "4000", FinanceAccountClassValues.Income, FinanceNormalBalanceValues.Credit),
                    ("operating_expense", "5000", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit)
                };
                foreach (var role in roles)
                {
                    var account = new FinanceAccount(Guid.NewGuid(), companyId, role.Item2, role.Item1,
                        role.Item3, "SEK", 0m, Now, accountClass: role.Item3,
                        normalBalance: role.Item4, effectiveFrom: new DateOnly(2026, 1, 1),
                        isPostingEnabled: true, controlAccountRole: role.Item1);
                    db.FinanceAccounts.Add(account);
                    configuration.AccountRoles.Add(new AccountingConfigurationAccountRole(Guid.NewGuid(), companyId,
                        configuration.Id, role.Item1, account.Id, Now));
                }
                db.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), companyId, "G", "General", "G", true, Now));
            }
            await db.SaveChangesAsync();
            var configService = new AccountingConfigurationService(db, resolver, new AuditEventWriter(db),
                new FixedTimeProvider(Now));
            var rehearsalService = new ReadyRehearsalService(companyId, switchId, planId, ownerId, Now);
            var policy = new AccountingProviderSwitchInternalReadinessPolicy(db, configService,
                rehearsalService, resolver);
            return new(connection, db, policy, companyId, switchId, planId);
        }

        public async ValueTask DisposeAsync()
        { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class ReadyRehearsalService(Guid companyId, Guid switchId, Guid planId, Guid ownerId,
        DateTime now) : IAccountingProviderSwitchRehearsalService
    {
        public Task<AccountingProviderSwitchPlanReadinessDto> GetPlanReadinessAsync(
            GetAccountingProviderSwitchPlanReadinessQuery query, CancellationToken cancellationToken)
        {
            var plan = new AccountingProviderSwitchCutoverPlanDto(planId, companyId, switchId, Guid.NewGuid(),
                1, new string('a', 64), new string('b', 64),
                AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems, now.AddHours(1), now.AddHours(2),
                "Source remains authoritative.", "[]", "{}", ownerId, now, Guid.NewGuid(), "approved", true, true);
            return Task.FromResult(new AccountingProviderSwitchPlanReadinessDto(switchId, plan, true, null,
                "The approved plan is current."));
        }
        public Task<AccountingProviderSwitchRehearsalDto> StartAsync(StartAccountingProviderSwitchRehearsalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchRehearsalDto> ReplayAsync(ReplayAccountingProviderSwitchRehearsalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchRehearsalDto> GetAsync(GetAccountingProviderSwitchRehearsalQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchManualEvidenceDto> RecordManualEvidenceAsync(RecordAccountingProviderSwitchManualEvidenceCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchCutoverPlanDto> GeneratePlanAsync(GenerateAccountingProviderSwitchCutoverPlanCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchCutoverPlanDto> RequestPlanApprovalAsync(RequestAccountingProviderSwitchPlanApprovalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero); }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId => companyId;
        public Guid? UserId => userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) { }
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) { }
    }
}
