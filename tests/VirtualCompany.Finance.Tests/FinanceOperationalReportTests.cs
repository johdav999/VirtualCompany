using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class FinanceOperationalReportTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task Cutoff_current_paid_balances_currency_and_horizon_reconcile_to_included_sources_without_writes()
    {
        await using var db = await Database(); var company = Guid.NewGuid(); var foreign = Guid.NewGuid();
        var customer = new FinanceCounterparty(Guid.NewGuid(), company, "Customer", "customer");
        var vendor = new FinanceCounterparty(Guid.NewGuid(), company, "Vendor", "vendor");
        var other = new FinanceCounterparty(Guid.NewGuid(), foreign, "Foreign secret", "customer");
        db.FinanceCounterparties.AddRange(customer, vendor, other);
        FinanceInvoice Invoice(Guid c, Guid counterparty, string number, int age, decimal amount = 100, string currency = "SEK", decimal paid = 0, DateTime? issued = null, string status = "open") =>
            new(Guid.NewGuid(), c, counterparty, number, issued ?? Now.AddDays(-100), Now.Date.AddDays(-age), amount, currency, status, paidAmount: paid);
        db.FinanceInvoices.AddRange(Invoice(company, customer.Id, "Part paid", 30, 100, paid: 20), Invoice(company, customer.Id, "USD", 31, 50, "USD"),
            Invoice(company, customer.Id, "Future issued", 1, issued: Now.Date.AddDays(1)), Invoice(company, customer.Id, "Credit", 1, -40),
            Invoice(company, customer.Id, "Paid", 1, status: "paid"), Invoice(foreign, other.Id, "Foreign secret", 1), Invoice(company, customer.Id, "After horizon", -15));
        db.FinanceBills.AddRange(new FinanceBill(Guid.NewGuid(), company, vendor.Id, "At horizon", Now.AddDays(-1), Now.Date.AddDays(14), 40, "SEK", "open", paidAmount: 10),
            new FinanceBill(Guid.NewGuid(), company, vendor.Id, "Overdue", Now.AddDays(-100), Now.AddDays(-91), 15, "SEK", "open"));
        var sek = new FinanceAccount(Guid.NewGuid(), company, "1930", "Cash SEK", "asset", "SEK", 0, Now.AddDays(-100));
        var usd = new FinanceAccount(Guid.NewGuid(), company, "1931", "Cash USD", "asset", "USD", 0, Now.AddDays(-100)); db.FinanceAccounts.AddRange(sek, usd);
        db.FinanceBalances.AddRange(new FinanceBalance(Guid.NewGuid(), company, sek.Id, Now.Date, 1000, "SEK"), new FinanceBalance(Guid.NewGuid(), company, usd.Id, Now.Date, 200, "USD"),
            new FinanceBalance(Guid.NewGuid(), company, sek.Id, Now.Date.AddDays(1), 9999, "SEK"));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); var service = Service(db, company);
        var result = await service.GetAsync(new(company, DateOnly.FromDateTime(Now)), default);
        Assert.Equal(3, result.Receivables.Count); Assert.Equal(2, result.Payables.Count); Assert.Empty(db.ChangeTracker.Entries());
        var total = Assert.Single(result.Totals, x => x.Currency == "SEK"); Assert.Equal(180, total.Receivables); Assert.Equal(80, total.OverdueReceivables);
        Assert.Equal(45, total.ExpectedOutflows); Assert.Equal(80, total.ExpectedInflows); Assert.Equal(1000, total.StartingCash); Assert.Equal(1035, total.ProjectedCash);
        Assert.Equal("past_due_1_30", result.Receivables.Single(x => x.Number == "Part paid").Bucket);
        Assert.Equal("past_due_31_60", result.Receivables.Single(x => x.Number == "USD").Bucket);
        Assert.Equal(250, result.Totals.Single(x => x.Currency == "USD").ProjectedCash);
        var filtered = await service.GetAsync(new(company, DateOnly.FromDateTime(Now), 14, "SEK", "overdue"), default);
        Assert.Single(filtered.Receivables); Assert.Single(filtered.Payables); Assert.Equal(1065, filtered.Totals.Single().ProjectedCash);
        Assert.Contains("later settlements", result.Meaning); Assert.Contains("including overdue", result.ForecastAssumptions);
    }
    [Theory]
    [InlineData(0, "current")][InlineData(1, "past_due_1_30")][InlineData(30, "past_due_1_30")]
    [InlineData(60, "past_due_31_60")][InlineData(90, "past_due_61_90")][InlineData(91, "past_due_over_90")]
    public async Task Owning_aging_buckets_use_UTC_calendar_cutoff_and_missing_cash_is_not_zero(int days, string bucket)
    {
        await using var db = await Database(); var company = Guid.NewGuid(); var customer = Guid.NewGuid();
        db.FinanceCounterparties.Add(new(customer, company, "Customer", "customer"));
        db.FinanceInvoices.Add(new(Guid.NewGuid(), company, customer, "Invoice", Now.AddDays(-100), Now.Date.AddDays(-days).AddHours(23), 100, "SEK", "open"));
        await db.SaveChangesAsync(); var result = await Service(db, company).GetAsync(new(company, DateOnly.FromDateTime(Now)), default);
        Assert.Equal(bucket, result.Receivables.Single().Bucket); Assert.Equal(days, result.Receivables.Single().DaysOverdue);
        Assert.Null(result.Totals.Single().StartingCash); Assert.Null(result.Totals.Single().ProjectedCash); Assert.NotEmpty(result.CoverageGaps);
    }
    [Fact]
    public async Task Report_reuses_record_source_policy_and_links_the_same_review_scope()
    {
        await using var db = await Database(); var company = Guid.NewGuid(); var customer = Guid.NewGuid();
        db.FinanceCounterparties.Add(new(customer, company, "Customer", "customer"));
        var manual = new FinanceInvoice(Guid.NewGuid(), company, customer, "Manual", Now.AddDays(-5), Now.Date, 100, "SEK", "open");
        var provider = new FinanceInvoice(Guid.NewGuid(), company, customer, "Fortnox", Now.AddDays(-5), Now.Date, 200, "SEK", "open");
        db.FinanceInvoices.AddRange(manual, provider); db.Entry(provider).Property<string>("SourceType").CurrentValue = FinanceRecordSourceTypes.Fortnox;
        await db.SaveChangesAsync(); var service = Service(db, company);
        var selected = await service.GetAsync(new(company, DateOnly.FromDateTime(Now), SourceFilter: "fortnox"), default);
        Assert.Equal(provider.Id, Assert.Single(selected.Receivables).Id); Assert.Contains("financeSource=fortnox", selected.Receivables[0].DeepLink);
        Assert.Contains("Obligation source: fortnox", selected.Meaning);
        Assert.Equal(2, (await service.GetAsync(new(company, DateOnly.FromDateTime(Now)), default)).Receivables.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new(company, DateOnly.FromDateTime(Now), SourceFilter: "simulation"), default));
    }
    [Fact]
    public async Task Foreign_context_and_invalid_filters_cannot_read_any_report()
    {
        await using var db = await Database(); var company = Guid.NewGuid(); var service = Service(db, company);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(new(Guid.NewGuid(), DateOnly.FromDateTime(Now)), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new(company, DateOnly.FromDateTime(Now), 31), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new(company, DateOnly.FromDateTime(Now), Currency: "MIXED"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new(company, DateOnly.FromDateTime(Now), Bucket: "unknown"), default));
    }
    private static FinanceOperationalReportService Service(VirtualCompanyDbContext db, Guid company) => new(db, new CompanyFinanceReadService(db), TimeProvider.System, NullLogger<FinanceOperationalReportService>.Instance, new Context(company));
    private static async Task<VirtualCompanyDbContext> Database()
    {
        var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync(); return db;
    }
    private sealed class Context(Guid company) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = company; public Guid? UserId => null; public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }
}
