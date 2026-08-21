using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class ManualJournalPolicyTests
{
    [Fact]
    public async Task Policy_deterministically_requires_evidence_and_blocks_restricted_control_accounts()
    {
        await using var fixture = await Fixture.CreateAsync(restrictDebitAccount: true);
        var decision = await fixture.Policy.EvaluateAsync(fixture.CompanyId,
            fixture.Input(evidence: []), CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RequiresApproval);
        Assert.Contains(decision.Issues, issue => issue.ReasonCode == ManualJournalReasonCodes.EvidenceRequired);
        Assert.Contains(decision.Issues, issue => issue.ReasonCode == AccountingPostingReasonCodes.ManualPostingRestricted);
    }

    [Fact]
    public async Task Valid_manual_journal_is_allowed_for_submission_but_always_requires_human_approval()
    {
        await using var fixture = await Fixture.CreateAsync(restrictDebitAccount: false);
        var decision = await fixture.Policy.EvaluateAsync(fixture.CompanyId,
            fixture.Input(evidence: [Guid.NewGuid()]), CancellationToken.None);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresApproval);
        Assert.Equal(100m, decision.ApprovalThreshold);
        Assert.Equal("USD", decision.ApprovalCurrency);
        Assert.Contains(decision.Warnings, warning => warning.ReasonCode == ManualJournalReasonCodes.ApprovalRequired);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context, ManualJournalPolicy policy,
            Guid companyId, Guid periodId, Guid debitId, Guid creditId)
        { _connection = connection; Context = context; Policy = policy; CompanyId = companyId; PeriodId = periodId; DebitId = debitId; CreditId = creditId; }
        public VirtualCompanyDbContext Context { get; }
        public ManualJournalPolicy Policy { get; }
        public Guid CompanyId { get; }
        public Guid PeriodId { get; }
        public Guid DebitId { get; }
        public Guid CreditId { get; }
        public ManualJournalDraftInput Input(IReadOnlyList<Guid> evidence) => new(PeriodId, "G", new(2026, 8, 20), new(2026, 8, 20),
            "Record the supported month-end accrual.", "USD", [new(DebitId, 120m, 0m), new(CreditId, 0m, 120m)], evidence);

        public static async Task<Fixture> CreateAsync(bool restrictDebitAccount)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var companyId = Guid.NewGuid(); var actorId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var debitId = Guid.NewGuid(); var creditId = Guid.NewGuid();
            var context = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                new Accessor(companyId, actorId));
            await context.Database.EnsureCreatedAsync();
            context.Companies.Add(new Company(companyId, "Policy company"));
            context.FinanceAccounts.AddRange(Account(debitId, companyId, "5000", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit, restrictDebitAccount),
                Account(creditId, companyId, "3000", FinanceAccountClassValues.Equity, FinanceNormalBalanceValues.Credit, false));
            var configuration = new AccountingConfiguration(Guid.NewGuid(), companyId, "USD", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
                new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, actorId, DateTime.UtcNow);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, DateTime.UtcNow);
            context.AccountingConfigurations.Add(configuration);
            context.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(Guid.NewGuid(), companyId, "USD", 1000m, 100m, true));
            await context.SaveChangesAsync();
            var resolver = new AccountingPolicyPackResolver([new CountryNeutralAccountingPolicyPack()]);
            return new Fixture(connection, context, new ManualJournalPolicy(context, resolver), companyId, periodId, debitId, creditId);
        }

        private static FinanceAccount Account(Guid id, Guid companyId, string code, string accountClass, string normalBalance, bool restricted) =>
            new(id, companyId, code, $"Account {code}", accountClass, "USD", 0m, DateTime.UtcNow, accountClass: accountClass,
                normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true, restrictManualPosting: restricted);
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
        private sealed class Accessor(Guid companyId, Guid userId) : ICompanyContextAccessor
        {
            public Guid? CompanyId { get; private set; } = companyId; public Guid? UserId => userId; public bool IsResolved => true;
            public ResolvedCompanyMembershipContext? Membership => null;
            public void SetCompanyId(Guid? value) => CompanyId = value;
            public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
        }
    }
}
