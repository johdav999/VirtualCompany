using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class CustomerInvoiceAccountingPolicyTests
{
    [Fact]
    public async Task Exclusive_tax_pack_produces_balanced_receivable_revenue_and_tax_lines()
    {
        await using var fixture = await Fixture.CreateAsync("exclusive", 125m);

        var preview = await fixture.PreviewAsync([new("Consulting", 100m, "standard")]);

        Assert.True(preview.IsReady);
        Assert.Equal(100m, preview.NetAmount);
        Assert.Equal(25m, preview.TaxAmount);
        Assert.Equal(125m, preview.GrossAmount);
        Assert.Equal(preview.JournalLines.Sum(x => x.DebitAmount), preview.JournalLines.Sum(x => x.CreditAmount));
        Assert.Contains(preview.JournalLines, x => x.AccountRole == "tax_payable" && x.CreditAmount == 25m);
    }

    [Fact]
    public async Task Inclusive_tax_pack_extracts_tax_from_the_document_total()
    {
        await using var fixture = await Fixture.CreateAsync("inclusive", 120m, rate: 0.20m);

        var preview = await fixture.PreviewAsync([new("Subscription", 120m, "standard")]);

        Assert.True(preview.IsReady);
        Assert.Equal(100m, preview.NetAmount);
        Assert.Equal(20m, preview.TaxAmount);
        Assert.Equal(120m, preview.GrossAmount);
    }

    [Fact]
    public async Task Exempt_multiline_invoice_has_no_tax_and_preserves_each_revenue_line()
    {
        await using var fixture = await Fixture.CreateAsync("exclusive", 100m);

        var preview = await fixture.PreviewAsync([
            new("Exempt service", 60m, "exempt"),
            new("Exempt support", 40m, "exempt")]);

        Assert.True(preview.IsReady);
        Assert.Equal(0m, preview.TaxAmount);
        Assert.Equal(2, preview.JournalLines.Count(x => x.AccountRole == "revenue"));
        Assert.DoesNotContain(preview.JournalLines, x => x.AccountRole == "tax_payable");
    }

    [Fact]
    public async Task Inclusive_rounding_is_deterministic_and_balanced()
    {
        await using var fixture = await Fixture.CreateAsync("inclusive", 100m, rate: 0.075m);

        var first = await fixture.PreviewAsync([new("Rounded service", 100m, "standard")]);
        var second = await fixture.PreviewAsync([new("Rounded service", 100m, "standard")]);

        Assert.Equal(93.02m, first.NetAmount);
        Assert.Equal(6.98m, first.TaxAmount);
        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Equal(first.JournalLines.Sum(x => x.DebitAmount), first.JournalLines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task Credit_note_uses_the_exact_inverse_journal_shape()
    {
        await using var invoice = await Fixture.CreateAsync("exclusive", 125m, documentKind: FinanceDocumentKinds.Invoice);
        await using var credit = await Fixture.CreateAsync("exclusive", 125m, documentKind: FinanceDocumentKinds.CreditNote);
        var lines = new[] { new CustomerInvoiceAccountingLineInput("Correction", 100m, "standard") };

        var invoicePreview = await invoice.PreviewAsync(lines);
        var creditPreview = await credit.PreviewAsync(lines);

        Assert.True(creditPreview.IsReady);
        Assert.Equal(invoicePreview.JournalLines.Sum(x => x.DebitAmount), creditPreview.JournalLines.Sum(x => x.CreditAmount));
        Assert.Equal(invoicePreview.JournalLines.Sum(x => x.CreditAmount), creditPreview.JournalLines.Sum(x => x.DebitAmount));
    }

    [Fact]
    public async Task Unknown_tax_rule_returns_a_stable_blocking_reason()
    {
        await using var fixture = await Fixture.CreateAsync("exclusive", 100m);

        var preview = await fixture.PreviewAsync([new("Unsupported", 100m, "missing-rule")]);

        Assert.False(preview.IsReady);
        Assert.Contains(preview.Issues, x => x.IsBlocking && x.ReasonCode == CustomerInvoiceAccountingReasonCodes.TaxRuleUnsupported);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context, CustomerInvoiceAccountingPolicy policy,
            Guid companyId, Guid actorId, Guid invoiceId, Guid periodId)
        {
            _connection = connection;
            Context = context;
            Policy = policy;
            CompanyId = companyId;
            ActorId = actorId;
            InvoiceId = invoiceId;
            PeriodId = periodId;
        }

        public VirtualCompanyDbContext Context { get; }
        public CustomerInvoiceAccountingPolicy Policy { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }
        public Guid InvoiceId { get; }
        public Guid PeriodId { get; }

        public Task<CustomerInvoiceAccountingPreviewDto> PreviewAsync(IReadOnlyList<CustomerInvoiceAccountingLineInput> lines) =>
            Policy.PreviewAsync(new(CompanyId, InvoiceId, new(PeriodId, "G", null, lines), ActorId), CancellationToken.None);

        public static async Task<Fixture> CreateAsync(string method, decimal invoiceAmount, decimal rate = 0.25m,
            string documentKind = FinanceDocumentKinds.Invoice)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var invoiceId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var configurationId = Guid.NewGuid();
            var receivableId = Guid.NewGuid();
            var revenueId = Guid.NewGuid();
            var taxId = Guid.NewGuid();
            var now = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
            var context = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                new Accessor(companyId, actorId));
            await context.Database.EnsureCreatedAsync();

            var pack = new SyntheticInvoicePack(method, rate);
            var configuration = new AccountingConfiguration(configurationId, companyId, "USD", 1, 1,
                pack.Definition.PackKey, pack.Definition.Version, new DateOnly(2026, 1, 1), 2,
                AccountingRoundingModeValues.MidpointToEven, actorId, now);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, now);
            context.Companies.Add(new Company(companyId, "Invoice policy company"));
            context.FinanceCounterparties.Add(new FinanceCounterparty(customerId, companyId, "Test customer", "customer", createdUtc: now));
            context.FinanceAccounts.AddRange(
                Account(receivableId, companyId, "1100", "Accounts receivable", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit, now),
                Account(revenueId, companyId, "4000", "Revenue", FinanceAccountClassValues.Income, FinanceNormalBalanceValues.Credit, now),
                Account(taxId, companyId, "2100", "Tax payable", FinanceAccountClassValues.Liability, FinanceNormalBalanceValues.Credit, now));
            context.AccountingConfigurations.Add(configuration);
            context.AccountingConfigurationAccountRoles.AddRange(
                new(Guid.NewGuid(), companyId, configurationId, "accounts_receivable", receivableId, now),
                new(Guid.NewGuid(), companyId, configurationId, "revenue", revenueId, now),
                new(Guid.NewGuid(), companyId, configurationId, "tax_payable", taxId, now));
            context.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            context.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), companyId, "G", "General", "G", true, now));
            context.FinanceInvoices.Add(new FinanceInvoice(invoiceId, companyId, customerId, $"INV-{invoiceId:N}", now,
                now.AddDays(30), documentKind == FinanceDocumentKinds.CreditNote ? -invoiceAmount : invoiceAmount,
                "USD", "approved", createdUtc: now, updatedUtc: now, documentKind: documentKind));
            await context.SaveChangesAsync();

            return new Fixture(connection, context,
                new CustomerInvoiceAccountingPolicy(context, new AccountingPolicyPackResolver([pack])),
                companyId, actorId, invoiceId, periodId);
        }

        private static FinanceAccount Account(Guid id, Guid companyId, string code, string name,
            string accountClass, string normalBalance, DateTime now) =>
            new(id, companyId, code, name, accountClass, "USD", 0m, now, accountClass: accountClass,
                normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SyntheticInvoicePack : IAccountingPolicyPack
    {
        public SyntheticInvoicePack(string method, decimal rate)
        {
            Definition = new AccountingPolicyPackDefinition(
                "invoice-test", method == CustomerInvoiceTaxMethodValues.Inclusive ? "2.0.0" : "1.0.0",
                $"Synthetic {method} invoice policy", null, true, false, "Test policy only.", [],
                [new("accounts_receivable", "Accounts receivable", true, true), new("revenue", "Revenue", true, false),
                    new("tax_payable", "Tax payable", true, true)],
                [new("standard", "Standard tax", new DateOnly(2020, 1, 1), rate, "tax_payable", null, method),
                    new("exempt", "Exempt", new DateOnly(2020, 1, 1), 0m, null, null, CustomerInvoiceTaxMethodValues.Exempt)],
                new(true, ["document_number", "issue_date", "counterparty", "currency", "line_items"], ["invoice", "credit_note"]),
                [], new Dictionary<string, string>(), new(null, false, true, "Test retention policy."), [], ["double_entry_bookkeeping"]);
            DefinitionHash = new string(method == CustomerInvoiceTaxMethodValues.Inclusive ? '2' : '1', 64);
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
