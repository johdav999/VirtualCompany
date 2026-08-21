using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class SupplierBillAccountingPolicyTests
{
    [Fact]
    public async Task Recoverable_tax_posts_cost_tax_and_payable_in_balance()
    {
        await using var fixture = await Fixture.CreateAsync("inclusive", 120m, 0.20m, recoverable: true);
        var preview = await fixture.PreviewAsync([new("Software", 120m, fixture.ExpenseAccountId, "standard")]);

        Assert.True(preview.IsReady);
        Assert.Equal(100m, preview.NetAmount);
        Assert.Equal(20m, preview.RecoverableTaxAmount);
        Assert.Equal(0m, preview.NonRecoverableTaxAmount);
        Assert.Equal(preview.JournalLines.Sum(x => x.DebitAmount), preview.JournalLines.Sum(x => x.CreditAmount));
        Assert.Contains(preview.JournalLines, x => x.AccountRole == "tax_recoverable" && x.DebitAmount == 20m);
    }

    [Fact]
    public async Task Non_recoverable_tax_is_included_in_cost_without_false_tax_balance()
    {
        await using var fixture = await Fixture.CreateAsync("inclusive", 120m, 0.20m, recoverable: false);
        var preview = await fixture.PreviewAsync([new("Insurance", 120m, fixture.ExpenseAccountId, "standard")]);

        Assert.True(preview.IsReady);
        Assert.Equal(20m, preview.NonRecoverableTaxAmount);
        Assert.Equal(120m, preview.JournalLines.Single(x => x.AccountRole == "expense").DebitAmount);
        Assert.DoesNotContain(preview.JournalLines, x => x.AccountRole == "tax_recoverable");
    }

    [Fact]
    public async Task Exempt_asset_bill_uses_selected_asset_account()
    {
        await using var fixture = await Fixture.CreateAsync("exempt", 500m, 0m, recoverable: false);
        var preview = await fixture.PreviewAsync([new("Equipment", 500m, fixture.AssetAccountId, "exempt")]);

        Assert.True(preview.IsReady);
        Assert.Contains(preview.JournalLines, x => x.FinanceAccountId == fixture.AssetAccountId && x.AccountRole == "asset" && x.DebitAmount == 500m);
        Assert.Equal(0m, preview.RecoverableTaxAmount);
    }

    [Fact]
    public async Task Ambiguous_account_is_blocked_instead_of_using_a_fallback()
    {
        await using var fixture = await Fixture.CreateAsync("exempt", 100m, 0m, recoverable: false);
        var preview = await fixture.PreviewAsync([new("Unclassified", 100m, Guid.Empty, "exempt")]);

        Assert.False(preview.IsReady);
        Assert.Contains(preview.Issues, x => x.ReasonCode == SupplierBillAccountingReasonCodes.CostAccountMissing);
    }

    [Fact]
    public async Task Multi_line_rounding_remains_deterministic_and_balanced()
    {
        await using var fixture = await Fixture.CreateAsync("inclusive", 100m, 0.075m, recoverable: true);
        var lines = new[]
        {
            new SupplierBillAccountingLineInput("Service", 60m, fixture.ExpenseAccountId, "standard"),
            new SupplierBillAccountingLineInput("Equipment", 40m, fixture.AssetAccountId, "standard")
        };
        var first = await fixture.PreviewAsync(lines);
        var second = await fixture.PreviewAsync(lines);

        Assert.True(first.IsReady);
        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Equal(first.JournalLines.Sum(x => x.DebitAmount), first.JournalLines.Sum(x => x.CreditAmount));
        Assert.Equal(2, first.JournalLines.Count(x => x.AccountRole is "expense" or "asset"));
    }

    [Fact]
    public async Task Supplier_credit_note_is_the_inverse_of_a_bill()
    {
        await using var bill = await Fixture.CreateAsync("inclusive", 120m, 0.20m, recoverable: true);
        await using var credit = await Fixture.CreateAsync("inclusive", 120m, 0.20m, recoverable: true,
            FinanceDocumentKinds.SupplierCreditNote);
        var billPreview = await bill.PreviewAsync([new("Software", 120m, bill.ExpenseAccountId, "standard")]);
        var creditPreview = await credit.PreviewAsync([new("Software", 120m, credit.ExpenseAccountId, "standard")]);

        Assert.True(creditPreview.IsReady);
        Assert.Equal(billPreview.JournalLines.Sum(x => x.DebitAmount), creditPreview.JournalLines.Sum(x => x.CreditAmount));
        Assert.Equal(billPreview.JournalLines.Sum(x => x.CreditAmount), creditPreview.JournalLines.Sum(x => x.DebitAmount));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context,
            SupplierBillAccountingPolicy policy, Guid companyId, Guid actorId, Guid billId,
            Guid periodId, Guid expenseAccountId, Guid assetAccountId)
        {
            _connection = connection;
            Context = context;
            Policy = policy;
            CompanyId = companyId;
            ActorId = actorId;
            BillId = billId;
            PeriodId = periodId;
            ExpenseAccountId = expenseAccountId;
            AssetAccountId = assetAccountId;
        }

        public VirtualCompanyDbContext Context { get; }
        public SupplierBillAccountingPolicy Policy { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }
        public Guid BillId { get; }
        public Guid PeriodId { get; }
        public Guid ExpenseAccountId { get; }
        public Guid AssetAccountId { get; }

        public Task<SupplierBillAccountingPreviewDto> PreviewAsync(IReadOnlyList<SupplierBillAccountingLineInput> lines) =>
            Policy.PreviewAsync(new(CompanyId, BillId, new(PeriodId, "G", null, lines), ActorId), CancellationToken.None);

        public static async Task<Fixture> CreateAsync(string method, decimal amount, decimal rate, bool recoverable,
            string documentKind = FinanceDocumentKinds.SupplierInvoice)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var supplierId = Guid.NewGuid();
            var billId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var configurationId = Guid.NewGuid();
            var payableId = Guid.NewGuid();
            var expenseId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var taxId = Guid.NewGuid();
            var now = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
            var context = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                new Accessor(companyId, actorId));
            await context.Database.EnsureCreatedAsync();
            var pack = new SyntheticSupplierPack(method, rate, recoverable);
            var configuration = new AccountingConfiguration(configurationId, companyId, "USD", 1, 1,
                pack.Definition.PackKey, pack.Definition.Version, new DateOnly(2026, 1, 1), 2,
                AccountingRoundingModeValues.MidpointToEven, actorId, now);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, now);
            context.Companies.Add(new Company(companyId, "Supplier policy company"));
            context.FinanceCounterparties.Add(new FinanceCounterparty(supplierId, companyId, "Test supplier", "supplier", createdUtc: now));
            context.FinanceAccounts.AddRange(
                Account(payableId, companyId, "2000", "Accounts payable", FinanceAccountClassValues.Liability, FinanceNormalBalanceValues.Credit, now, "accounts_payable"),
                Account(expenseId, companyId, "5000", "Expense", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit, now),
                Account(assetId, companyId, "1500", "Equipment", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit, now),
                Account(taxId, companyId, "1200", "Recoverable tax", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit, now, "tax_recoverable"));
            context.AccountingConfigurations.Add(configuration);
            context.AccountingConfigurationAccountRoles.AddRange(
                new(Guid.NewGuid(), companyId, configurationId, "accounts_payable", payableId, now),
                new(Guid.NewGuid(), companyId, configurationId, "tax_recoverable", taxId, now));
            context.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "August 2026", now.Date, now.Date.AddMonths(1)));
            context.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), companyId, "G", "General", "G", true, now));
            context.FinanceBills.Add(new FinanceBill(billId, companyId, supplierId, $"BILL-{billId:N}", now,
                now.AddDays(30), documentKind == FinanceDocumentKinds.SupplierCreditNote ? -amount : amount,
                "USD", "approved", createdUtc: now, updatedUtc: now, documentKind: documentKind));
            await context.SaveChangesAsync();
            return new Fixture(connection, context,
                new SupplierBillAccountingPolicy(context, new AccountingPolicyPackResolver([pack])),
                companyId, actorId, billId, periodId, expenseId, assetId);
        }

        private static FinanceAccount Account(Guid id, Guid companyId, string code, string name,
            string accountClass, string normalBalance, DateTime now, string? role = null) =>
            new(id, companyId, code, name, accountClass, "USD", 0m, now, accountClass: accountClass,
                normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1),
                isPostingEnabled: true, controlAccountRole: role);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SyntheticSupplierPack : IAccountingPolicyPack
    {
        public SyntheticSupplierPack(string method, decimal rate, bool recoverable)
        {
            Definition = new AccountingPolicyPackDefinition("supplier-test", recoverable ? "2.0.0" : "1.0.0",
                "Synthetic supplier policy", null, true, false, "Test policy only.", [],
                [new("accounts_payable", "Accounts payable", true, true),
                    new("tax_recoverable", "Recoverable tax", recoverable, true)],
                [new("standard", "Standard tax", new DateOnly(2020, 1, 1), rate, null,
                        recoverable ? "tax_recoverable" : null, method),
                    new("exempt", "No tax", new DateOnly(2020, 1, 1), 0m, null, null,
                        CustomerInvoiceTaxMethodValues.Exempt)],
                new(true, ["document_number", "issue_date", "counterparty", "currency", "line_items"], ["invoice", "credit_note"]),
                [], new Dictionary<string, string>(), new(null, false, true, "Test retention policy."), [],
                ["double_entry_bookkeeping"]);
            DefinitionHash = new string(recoverable ? '2' : '1', 64);
        }

        public AccountingPolicyPackDefinition Definition { get; }
        public string DefinitionHash { get; }
    }

    private sealed class Accessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }
}
