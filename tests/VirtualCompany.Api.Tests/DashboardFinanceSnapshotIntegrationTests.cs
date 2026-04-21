using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DashboardFinanceSnapshotIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime ScenarioAsOfUtc = new(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ExpectedFinanceMetrics PrimaryWindow30Metrics = new(1150m, 1500m, 725m, 300m, 350m);
    private static readonly ExpectedFinanceMetrics PrimaryWindow15Metrics = new(1150m, 1000m, 575m, 300m, 200m);
    private static readonly ExpectedFinanceMetrics SecondaryWindow30Metrics = new(9000m, 999m, 450m, 0m, 450m);

    private readonly TestWebApplicationFactory _factory;

    public DashboardFinanceSnapshotIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCashMetrics_ReturnsExpectedValues_ForSeededFinanceScenario()
    {
        var seed = await SeedFinanceDashboardScenarioAsync();

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var snapshot = await GetSnapshotAsync(client, seed.CompanyId, ScenarioAsOfUtc, 30);

        AssertSnapshot(snapshot, seed.CompanyId, ScenarioAsOfUtc, 30, PrimaryWindow30Metrics);
        Assert.Equal(snapshot.CurrentCashBalance, snapshot.Cash);
        Assert.True(snapshot.HasFinanceData);
    }

    [Fact]
    public async Task GetCashMetrics_HonorsSelectedCompanyAndDateContext_ForSameUser()
    {
        var seed = await SeedFinanceDashboardScenarioAsync();

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var primarySnapshot = await GetSnapshotAsync(client, seed.CompanyId, ScenarioAsOfUtc, 30);
        var secondarySnapshot = await GetSnapshotAsync(client, seed.OtherCompanyId, ScenarioAsOfUtc, 30);

        AssertSnapshot(primarySnapshot, seed.CompanyId, ScenarioAsOfUtc, 30, PrimaryWindow30Metrics);
        AssertSnapshot(secondarySnapshot, seed.OtherCompanyId, ScenarioAsOfUtc, 30, SecondaryWindow30Metrics);

        using var scopedClient = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);
        var narrowedResponse = await scopedClient.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/dashboard/cash-metrics?asOfUtc={Uri.EscapeDataString(ScenarioAsOfUtc.ToString("O"))}&upcomingWindowDays=15");

        Assert.Equal(HttpStatusCode.OK, narrowedResponse.StatusCode);
        var narrowedSnapshot = await narrowedResponse.Content.ReadFromJsonAsync<FinanceSnapshotResponse>();

        Assert.NotNull(narrowedSnapshot);
        AssertSnapshot(narrowedSnapshot!, seed.CompanyId, ScenarioAsOfUtc, 15, PrimaryWindow15Metrics);
    }

    [Fact]
    public async Task GetCashMetrics_UsesOnlyPostedCashAccountLedgerMovements_AndReturnsIndividualMetricEndpoints()
    {
        var seed = await SeedFinanceDashboardScenarioAsync();

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);
        var baseRoute = $"/internal/companies/{seed.CompanyId:D}/finance/dashboard";
        var query = $"asOfUtc={Uri.EscapeDataString(ScenarioAsOfUtc.ToString("O"))}&upcomingWindowDays=30";

        var aggregateResponse = await client.GetAsync($"{baseRoute}/cash-metrics?{query}");
        Assert.Equal(HttpStatusCode.OK, aggregateResponse.StatusCode);

        var aggregate = await aggregateResponse.Content.ReadFromJsonAsync<FinanceSnapshotResponse>();
        Assert.NotNull(aggregate);
        AssertSnapshot(aggregate!, seed.CompanyId, ScenarioAsOfUtc, 30, PrimaryWindow30Metrics);

        await AssertMetricAsync(client, $"{baseRoute}/current-cash-balance?{query}", "current_cash_balance", PrimaryWindow30Metrics.CurrentCashBalance);
        await AssertMetricAsync(client, $"{baseRoute}/expected-incoming-cash?{query}", "expected_incoming_cash", PrimaryWindow30Metrics.ExpectedIncomingCash);
        await AssertMetricAsync(client, $"{baseRoute}/expected-outgoing-cash?{query}", "expected_outgoing_cash", PrimaryWindow30Metrics.ExpectedOutgoingCash);
        await AssertMetricAsync(client, $"{baseRoute}/overdue-receivables?{query}", "overdue_receivables", PrimaryWindow30Metrics.OverdueReceivables);
        await AssertMetricAsync(client, $"{baseRoute}/upcoming-payables?{query}", "upcoming_payables", PrimaryWindow30Metrics.UpcomingPayables);
    }

    private async Task AssertMetricAsync(HttpClient client, string route, string key, decimal amount)
    {
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var metric = await response.Content.ReadFromJsonAsync<FinanceDashboardMetricResponse>();
        Assert.NotNull(metric);
        Assert.Equal(key, metric!.MetricKey);
        Assert.Equal(amount, metric.Amount);
        Assert.Equal("USD", metric.Currency);
        Assert.Equal(ScenarioAsOfUtc, metric.AsOfUtc);
    }

    private async Task<FinanceSnapshotResponse> GetSnapshotAsync(
        HttpClient client,
        Guid companyId,
        DateTime asOfUtc,
        int upcomingWindowDays)
    {
        var response = await client.GetAsync(
            $"/api/dashboard/finance-snapshot?companyId={companyId:D}&asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O"))}&upcomingWindowDays={upcomingWindowDays}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<FinanceSnapshotResponse>();
        Assert.NotNull(snapshot);
        return snapshot!;
    }

    private static void AssertSnapshot(
        FinanceSnapshotResponse snapshot,
        Guid companyId,
        DateTime asOfUtc,
        int upcomingWindowDays,
        ExpectedFinanceMetrics expected)
    {
        Assert.Equal(companyId, snapshot.CompanyId);
        Assert.Equal(expected.CurrentCashBalance, snapshot.CurrentCashBalance);
        Assert.Equal(expected.ExpectedIncomingCash, snapshot.ExpectedIncomingCash);
        Assert.Equal(expected.ExpectedOutgoingCash, snapshot.ExpectedOutgoingCash);
        Assert.Equal(expected.OverdueReceivables, snapshot.OverdueReceivables);
        Assert.Equal(expected.UpcomingPayables, snapshot.UpcomingPayables);
        Assert.Equal("USD", snapshot.Currency);
        Assert.Equal(asOfUtc, snapshot.AsOfUtc);
        Assert.Equal(upcomingWindowDays, snapshot.UpcomingWindowDays);
    }

    private async Task<DashboardFinanceScenarioSeed> SeedFinanceDashboardScenarioAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"dashboard-finance-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Dashboard Finance Owner";

        var cashAccountId = Guid.NewGuid();
        var revenueAccountId = Guid.NewGuid();
        var expenseAccountId = Guid.NewGuid();
        var otherCashAccountId = Guid.NewGuid();
        var otherRevenueAccountId = Guid.NewGuid();
        var otherExpenseAccountId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var otherFiscalPeriodId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var otherCounterpartyId = Guid.NewGuid();

        var openingEntryId = Guid.NewGuid();
        var supplierPaymentEntryId = Guid.NewGuid();
        var customerReceiptEntryId = Guid.NewGuid();
        var draftEntryId = Guid.NewGuid();
        var futureEntryId = Guid.NewGuid();
        var accrualEntryId = Guid.NewGuid();
        var otherOpeningEntryId = Guid.NewGuid();

        var overdueInvoiceId = Guid.NewGuid();
        var upcomingInvoiceId = Guid.NewGuid();
        var scheduledInvoiceId = Guid.NewGuid();
        var futureInvoiceId = Guid.NewGuid();
        var paidInvoiceId = Guid.NewGuid();
        var overdueBillId = Guid.NewGuid();
        var upcomingBillId = Guid.NewGuid();
        var laterWindowBillId = Guid.NewGuid();
        var scheduledBillId = Guid.NewGuid();
        var paidBillId = Guid.NewGuid();
        var otherInvoiceId = Guid.NewGuid();
        var otherBillId = Guid.NewGuid();

        var scheduledIncomingPaymentId = Guid.NewGuid();
        var completedIncomingPaymentId = Guid.NewGuid();
        var scheduledOutgoingPaymentId = Guid.NewGuid();
        var completedOutgoingPaymentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Seeded Dashboard Finance Co"),
                new Company(otherCompanyId, "Other Dashboard Finance Co"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.FinanceAccounts.AddRange(
                new FinanceAccount(cashAccountId, companyId, "1000", "Operating Cash", "cash", "USD", 0m, ScenarioAsOfUtc.AddMonths(-3)),
                new FinanceAccount(revenueAccountId, companyId, "4000", "Revenue", "revenue", "USD", 0m, ScenarioAsOfUtc.AddMonths(-3)),
                new FinanceAccount(expenseAccountId, companyId, "6000", "Operating Expenses", "expense", "USD", 0m, ScenarioAsOfUtc.AddMonths(-3)),
                new FinanceAccount(otherCashAccountId, otherCompanyId, "1000", "Other Operating Cash", "cash", "USD", 0m, ScenarioAsOfUtc.AddMonths(-3)),
                new FinanceAccount(otherRevenueAccountId, otherCompanyId, "4000", "Other Revenue", "revenue", "USD", 0m, ScenarioAsOfUtc.AddMonths(-3)),
                new FinanceAccount(otherExpenseAccountId, otherCompanyId, "6000", "Other Expenses", "expense", "USD", 0m, ScenarioAsOfUtc.AddMonths(-3)));

            dbContext.FiscalPeriods.AddRange(
                new FiscalPeriod(
                    fiscalPeriodId,
                    companyId,
                    "FY26",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FiscalPeriod(
                    otherFiscalPeriodId,
                    otherCompanyId,
                    "FY26",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

            dbContext.LedgerEntries.AddRange(
                new LedgerEntry(
                    openingEntryId,
                    companyId,
                    fiscalPeriodId,
                    "LE-CASH-OPEN-001",
                    ScenarioAsOfUtc.AddDays(-20),
                    LedgerEntryStatuses.Posted,
                    "Opening customer receipt",
                    postedAtUtc: ScenarioAsOfUtc.AddDays(-20)),
                new LedgerEntry(
                    supplierPaymentEntryId,
                    companyId,
                    fiscalPeriodId,
                    "LE-CASH-OUT-001",
                    ScenarioAsOfUtc.AddDays(-3),
                    LedgerEntryStatuses.Posted,
                    "Supplier payment",
                    postedAtUtc: ScenarioAsOfUtc.AddDays(-3)),
                new LedgerEntry(
                    customerReceiptEntryId,
                    companyId,
                    fiscalPeriodId,
                    "LE-CASH-IN-002",
                    ScenarioAsOfUtc.AddDays(-1),
                    LedgerEntryStatuses.Posted,
                    "Recent customer receipt",
                    postedAtUtc: ScenarioAsOfUtc.AddDays(-1)),
                new LedgerEntry(
                    draftEntryId,
                    companyId,
                    fiscalPeriodId,
                    "LE-DRAFT-001",
                    ScenarioAsOfUtc.AddHours(-6),
                    LedgerEntryStatuses.Draft,
                    "Draft cash movement"),
                new LedgerEntry(
                    futureEntryId,
                    companyId,
                    fiscalPeriodId,
                    "LE-FUTURE-001",
                    ScenarioAsOfUtc.AddDays(2),
                    LedgerEntryStatuses.Posted,
                    "Future cash movement",
                    postedAtUtc: ScenarioAsOfUtc.AddDays(2)),
                new LedgerEntry(
                    accrualEntryId,
                    companyId,
                    fiscalPeriodId,
                    "LE-ACCRUAL-001",
                    ScenarioAsOfUtc.AddDays(-2),
                    LedgerEntryStatuses.Posted,
                    "Non-cash accrual",
                    postedAtUtc: ScenarioAsOfUtc.AddDays(-2)),
                new LedgerEntry(
                    otherOpeningEntryId,
                    otherCompanyId,
                    otherFiscalPeriodId,
                    "LE-OTHER-CASH-001",
                    ScenarioAsOfUtc.AddDays(-10),
                    LedgerEntryStatuses.Posted,
                    "Other company cash opening",
                    postedAtUtc: ScenarioAsOfUtc.AddDays(-10)));

            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), companyId, openingEntryId, cashAccountId, 1000m, 0m, "USD", "Cash in"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, openingEntryId, revenueAccountId, 0m, 1000m, "USD", "Offset"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, supplierPaymentEntryId, expenseAccountId, 250m, 0m, "USD", "Expense offset"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, supplierPaymentEntryId, cashAccountId, 0m, 250m, "USD", "Cash out"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, customerReceiptEntryId, cashAccountId, 400m, 0m, "USD", "Recent cash in"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, customerReceiptEntryId, revenueAccountId, 0m, 400m, "USD", "Offset"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, draftEntryId, cashAccountId, 700m, 0m, "USD", "Draft cash in"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, draftEntryId, revenueAccountId, 0m, 700m, "USD", "Offset"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, futureEntryId, cashAccountId, 900m, 0m, "USD", "Future cash in"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, futureEntryId, revenueAccountId, 0m, 900m, "USD", "Offset"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, accrualEntryId, expenseAccountId, 60m, 0m, "USD", "Accrual"),
                new LedgerEntryLine(Guid.NewGuid(), companyId, accrualEntryId, revenueAccountId, 0m, 60m, "USD", "Offset"),
                new LedgerEntryLine(Guid.NewGuid(), otherCompanyId, otherOpeningEntryId, otherCashAccountId, 9000m, 0m, "USD", "Other company cash"),
                new LedgerEntryLine(Guid.NewGuid(), otherCompanyId, otherOpeningEntryId, otherRevenueAccountId, 0m, 9000m, "USD", "Offset"));

            dbContext.FinanceCounterparties.AddRange(
                new FinanceCounterparty(customerId, companyId, "Northwind", "customer", "northwind@example.com"),
                new FinanceCounterparty(supplierId, companyId, "Wingtip", "supplier", "wingtip@example.com"),
                new FinanceCounterparty(otherCounterpartyId, otherCompanyId, "Fourth Coffee", "customer", "fourthcoffee@example.com"));

            dbContext.FinanceInvoices.AddRange(
                new FinanceInvoice(overdueInvoiceId, companyId, customerId, "INV-OVERDUE-001", ScenarioAsOfUtc.AddDays(-30), ScenarioAsOfUtc.AddDays(-5), 300m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceInvoice(upcomingInvoiceId, companyId, customerId, "INV-UPCOMING-001", ScenarioAsOfUtc.AddDays(-12), ScenarioAsOfUtc.AddDays(20), 500m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceInvoice(scheduledInvoiceId, companyId, customerId, "INV-SCHEDULED-001", ScenarioAsOfUtc.AddDays(-18), ScenarioAsOfUtc.AddDays(60), 1000m, "USD", "open", settlementStatus: FinanceSettlementStatuses.PartiallyPaid),
                new FinanceInvoice(futureInvoiceId, companyId, customerId, "INV-LATER-001", ScenarioAsOfUtc.AddDays(-8), ScenarioAsOfUtc.AddDays(45), 900m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceInvoice(paidInvoiceId, companyId, customerId, "INV-PAID-001", ScenarioAsOfUtc.AddDays(-20), ScenarioAsOfUtc.AddDays(-1), 200m, "USD", "paid", settlementStatus: FinanceSettlementStatuses.Paid),
                new FinanceInvoice(otherInvoiceId, otherCompanyId, otherCounterpartyId, "INV-OTHER-001", ScenarioAsOfUtc.AddDays(-4), ScenarioAsOfUtc.AddDays(3), 999m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid));

            dbContext.FinanceBills.AddRange(
                new FinanceBill(overdueBillId, companyId, supplierId, "BILL-OVERDUE-001", ScenarioAsOfUtc.AddDays(-14), ScenarioAsOfUtc.AddDays(-2), 125m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceBill(upcomingBillId, companyId, supplierId, "BILL-UPCOMING-001", ScenarioAsOfUtc.AddDays(-10), ScenarioAsOfUtc.AddDays(10), 200m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceBill(laterWindowBillId, companyId, supplierId, "BILL-LATER-001", ScenarioAsOfUtc.AddDays(-9), ScenarioAsOfUtc.AddDays(25), 150m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceBill(scheduledBillId, companyId, supplierId, "BILL-SCHEDULED-001", ScenarioAsOfUtc.AddDays(-16), ScenarioAsOfUtc.AddDays(45), 600m, "USD", "open", settlementStatus: FinanceSettlementStatuses.PartiallyPaid),
                new FinanceBill(paidBillId, companyId, supplierId, "BILL-PAID-001", ScenarioAsOfUtc.AddDays(-20), ScenarioAsOfUtc.AddDays(-3), 100m, "USD", "paid", settlementStatus: FinanceSettlementStatuses.Paid),
                new FinanceBill(otherBillId, otherCompanyId, otherCounterpartyId, "BILL-OTHER-001", ScenarioAsOfUtc.AddDays(-3), ScenarioAsOfUtc.AddDays(4), 450m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid));

            dbContext.Payments.AddRange(
                new Payment(scheduledIncomingPaymentId, companyId, PaymentTypes.Incoming, 700m, "USD", ScenarioAsOfUtc.AddDays(5), "bank_transfer", PaymentStatuses.Pending, "INV-SCHEDULED-001"),
                new Payment(completedIncomingPaymentId, companyId, PaymentTypes.Incoming, 100m, "USD", ScenarioAsOfUtc.AddDays(-2), "bank_transfer", PaymentStatuses.Completed, "INV-SCHEDULED-001"),
                new Payment(scheduledOutgoingPaymentId, companyId, PaymentTypes.Outgoing, 250m, "USD", ScenarioAsOfUtc.AddDays(6), "bank_transfer", PaymentStatuses.Pending, "BILL-SCHEDULED-001"),
                new Payment(completedOutgoingPaymentId, companyId, PaymentTypes.Outgoing, 50m, "USD", ScenarioAsOfUtc.AddDays(-4), "bank_transfer", PaymentStatuses.Completed, "BILL-SCHEDULED-001"));

            // Pending allocations inside the selected window should replace the unscheduled remainder
            // for expected cash metrics while completed allocations reduce the remaining open balance.
            dbContext.PaymentAllocations.AddRange(
                new PaymentAllocation(Guid.NewGuid(), companyId, scheduledIncomingPaymentId, scheduledInvoiceId, null, 700m, "USD"),
                new PaymentAllocation(Guid.NewGuid(), companyId, completedIncomingPaymentId, scheduledInvoiceId, null, 100m, "USD"),
                new PaymentAllocation(Guid.NewGuid(), companyId, scheduledOutgoingPaymentId, null, scheduledBillId, 250m, "USD"),
                new PaymentAllocation(Guid.NewGuid(), companyId, completedOutgoingPaymentId, null, scheduledBillId, 50m, "USD"));

            return Task.CompletedTask;
        });

        return new DashboardFinanceScenarioSeed(companyId, otherCompanyId, subject, email, displayName);
    }

    private HttpClient CreateAuthenticatedClient(
        string subject,
        string email,
        string displayName,
        Guid? activeCompanyId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        if (activeCompanyId.HasValue)
        {
            client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, activeCompanyId.Value.ToString());
        }

        return client;
    }

    private sealed record DashboardFinanceScenarioSeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed record ExpectedFinanceMetrics(
        decimal CurrentCashBalance,
        decimal ExpectedIncomingCash,
        decimal ExpectedOutgoingCash,
        decimal OverdueReceivables,
        decimal UpcomingPayables);

    private sealed class FinanceSnapshotResponse
    {
        public Guid CompanyId { get; set; }
        public decimal CurrentCashBalance { get; set; }
        public decimal ExpectedIncomingCash { get; set; }
        public decimal ExpectedOutgoingCash { get; set; }
        public decimal OverdueReceivables { get; set; }
        public decimal UpcomingPayables { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime AsOfUtc { get; set; }
        public int UpcomingWindowDays { get; set; }
        public decimal Cash { get; set; }
        public decimal BurnRate { get; set; }
        public int? RunwayDays { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
        public bool HasFinanceData { get; set; }
    }

    private sealed class FinanceDashboardMetricResponse
    {
        public string MetricKey { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime AsOfUtc { get; set; }
        public int UpcomingWindowDays { get; set; }
    }
}