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

public sealed class AccountingPostingServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly PostingDate = new(2026, 8, 19);

    [Fact]
    public async Task Unbalanced_preview_and_post_reject_without_consuming_voucher()
    {
        await using var fixture = await PostingFixture.CreateAsync();
        var unbalanced = fixture.CreateEntry() with
        {
            Lines =
            [
                new(fixture.DebitAccountId, 100m, 0m, "USD"),
                new(fixture.CreditAccountId, 0m, 99m, "USD")
            ]
        };

        var preview = await fixture.Service.PreviewAsync(new PreviewAccountingEntryCommand(unbalanced), CancellationToken.None);
        var error = await Assert.ThrowsAsync<AccountingPostingException>(() =>
            fixture.Service.PostAsync(new PostAccountingEntryCommand(unbalanced), CancellationToken.None));

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Issues, issue => issue.ReasonCode == AccountingPostingReasonCodes.UnbalancedEntry);
        Assert.Equal(AccountingPostingReasonCodes.UnbalancedEntry, error.ReasonCode);
        Assert.Empty(await fixture.Context.VoucherSequences.ToListAsync());
        Assert.Empty(await fixture.Context.LedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task Post_allocates_voucher_and_same_payload_replays_while_changed_payload_conflicts()
    {
        await using var fixture = await PostingFixture.CreateAsync();
        var proposed = fixture.CreateEntry();

        var first = await fixture.Service.PostAsync(new PostAccountingEntryCommand(proposed, "first"), CancellationToken.None);
        var replay = await fixture.Service.PostAsync(new PostAccountingEntryCommand(proposed, "retry"), CancellationToken.None);
        var conflict = await Assert.ThrowsAsync<AccountingPostingException>(() => fixture.Service.PostAsync(
            new PostAccountingEntryCommand(proposed with { Description = "Changed after the first post" }), CancellationToken.None));

        Assert.False(first.IsIdempotentReplay);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(first.Journal.Id, replay.Journal.Id);
        Assert.Equal("G-2026-000001", first.Journal.EntryNumber);
        Assert.Equal(AccountingPostingReasonCodes.IdempotencyConflict, conflict.ReasonCode);
        Assert.Single(await fixture.Context.LedgerEntries.ToListAsync());
        Assert.Equal(1, (await fixture.Context.VoucherSequences.SingleAsync()).LastAllocatedNumber);
        Assert.Single(await fixture.Context.LedgerPostingIdentities.ToListAsync());
        Assert.Equal(1, await fixture.Context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Posted_journal_is_immutable_and_reversal_creates_linked_opposite_entry()
    {
        await using var fixture = await PostingFixture.CreateAsync();
        var posted = await fixture.Service.PostAsync(new PostAccountingEntryCommand(fixture.CreateEntry()), CancellationToken.None);

        var tracked = await fixture.Context.LedgerEntries.SingleAsync(entry => entry.Id == posted.Journal.Id);
        fixture.Context.Entry(tracked).Property(nameof(LedgerEntry.Description)).CurrentValue = "Forbidden edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Context.SaveChangesAsync());
        fixture.Context.ChangeTracker.Clear();

        var reversal = await fixture.Service.ReverseAsync(new ReverseAccountingEntryCommand(
            fixture.CompanyId, posted.Journal.Id, fixture.FiscalPeriodId, "G", PostingDate,
            "Correct the source classification", "1", "reverse:source-1:1", fixture.ActorId), CancellationToken.None);

        Assert.Equal(posted.Journal.Id, reversal.Journal.OriginalLedgerEntryId);
        Assert.Equal(LedgerPostingTypeValues.Reversal, reversal.Journal.PostingType);
        Assert.Equal(posted.Journal.DebitTotal, reversal.Journal.CreditTotal);
        Assert.Equal(posted.Journal.CreditTotal, reversal.Journal.DebitTotal);
        Assert.Equal(2, await fixture.Context.LedgerEntries.CountAsync());
        Assert.Equal(2, await fixture.Context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Closed_period_and_cross_company_account_are_rejected_without_disclosing_foreign_data()
    {
        await using var fixture = await PostingFixture.CreateAsync();
        var foreignCompanyId = Guid.NewGuid();
        var foreignAccountId = Guid.NewGuid();
        fixture.Context.Companies.Add(new Company(foreignCompanyId, "Other company"));
        fixture.Context.FinanceAccounts.Add(CreateAccount(foreignAccountId, foreignCompanyId, "9999", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit));
        fixture.Accessor.SetCompanyId(foreignCompanyId);
        await fixture.Context.SaveChangesAsync();
        fixture.Accessor.SetCompanyId(fixture.CompanyId);

        var crossCompany = fixture.CreateEntry() with
        {
            Lines =
            [
                new(foreignAccountId, 100m, 0m, "USD"),
                new(fixture.CreditAccountId, 0m, 100m, "USD")
            ]
        };
        var preview = await fixture.Service.PreviewAsync(new PreviewAccountingEntryCommand(crossCompany), CancellationToken.None);
        Assert.Contains(preview.Issues, issue => issue.ReasonCode == AccountingPostingReasonCodes.AccountNotFound);

        var period = await fixture.Context.FiscalPeriods.SingleAsync();
        period.Close(NowUtc);
        await fixture.Context.SaveChangesAsync();
        var closedError = await Assert.ThrowsAsync<AccountingPostingException>(() =>
            fixture.Service.PostAsync(new PostAccountingEntryCommand(fixture.CreateEntry() with { SourceId = "source-closed", IdempotencyKey = "post:source-closed:1" }), CancellationToken.None));
        Assert.Equal(AccountingPostingReasonCodes.PeriodClosed, closedError.ReasonCode);
        Assert.Empty(await fixture.Context.LedgerEntries.ToListAsync());
    }

    [Fact]
    public void Legacy_accounts_remain_explicitly_unclassified_and_cannot_post_by_default()
    {
        var account = new FinanceAccount(Guid.NewGuid(), Guid.NewGuid(), "1000", "Legacy cash", "asset", "USD", 0m, NowUtc);

        Assert.Null(account.AccountClass);
        Assert.Null(account.NormalBalance);
        Assert.False(account.IsPostingEnabled);
    }

    private static FinanceAccount CreateAccount(Guid id, Guid companyId, string code, string accountClass, string normalBalance) =>
        new(id, companyId, code, $"Account {code}", accountClass, "USD", 0m, NowUtc,
            accountClass: accountClass, normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1),
            isPostingEnabled: true);

    private sealed class PostingFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PostingFixture(SqliteConnection connection, VirtualCompanyDbContext context, AccountingPostingService service, TestCompanyContextAccessor accessor,
            Guid companyId, Guid actorId, Guid fiscalPeriodId, Guid debitAccountId, Guid creditAccountId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            Accessor = accessor;
            CompanyId = companyId;
            ActorId = actorId;
            FiscalPeriodId = fiscalPeriodId;
            DebitAccountId = debitAccountId;
            CreditAccountId = creditAccountId;
        }

        public VirtualCompanyDbContext Context { get; }
        public AccountingPostingService Service { get; }
        public TestCompanyContextAccessor Accessor { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }
        public Guid FiscalPeriodId { get; }
        public Guid DebitAccountId { get; }
        public Guid CreditAccountId { get; }

        public ProposedAccountingEntry CreateEntry() => new(
            CompanyId, FiscalPeriodId, "G", PostingDate, PostingDate, LedgerPostingTypeValues.SourceDocument,
            "Post source document", "test_source", "source-1", "1", "post:source-1:1",
            [new(DebitAccountId, 100m, 0m, "USD"), new(CreditAccountId, 0m, 100m, "USD")],
            ActorId, PolicyFacts: new Dictionary<string, string> { ["taxTreatment"] = "none" });

        public static async Task<PostingFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var fiscalPeriodId = Guid.NewGuid();
            var debitId = Guid.NewGuid();
            var creditId = Guid.NewGuid();
            var accessor = new TestCompanyContextAccessor(companyId, actorId);
            var context = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, accessor);
            await context.Database.EnsureCreatedAsync();

            context.Companies.Add(new Company(companyId, "Posting company"));
            context.Users.Add(new User(actorId, "owner@example.com", "Owner", "test", actorId.ToString("N")));
            context.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, actorId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            context.FinanceAccounts.AddRange(
                CreateAccount(debitId, companyId, "1000", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit),
                CreateAccount(creditId, companyId, "3000", FinanceAccountClassValues.Equity, FinanceNormalBalanceValues.Credit));
            context.FiscalPeriods.Add(new FiscalPeriod(fiscalPeriodId, companyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            context.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), companyId, "G", "General journal", "G", true, NowUtc));
            var configuration = new AccountingConfiguration(Guid.NewGuid(), companyId, "USD", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
                new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, actorId, NowUtc);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, NowUtc);
            context.AccountingConfigurations.Add(configuration);
            await context.SaveChangesAsync();

            var readService = new AccountingJournalReadService(context);
            var service = new AccountingPostingService(context, readService, new AuditEventWriter(context), new FixedTimeProvider(new DateTimeOffset(NowUtc)));
            return new PostingFixture(connection, context, service, accessor, companyId, actorId, fiscalPeriodId, debitId, creditId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
