using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CashAnalyticsLatencyIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private const int SampleCount = 5;
    private const double DashboardMedianTargetMilliseconds = 2000;
    private const double AgentQueryMedianTargetMilliseconds = 2000;
    private static readonly DateTime ScenarioAsOfUtc = new(2026, 4, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestWebApplicationFactory _factory;

    public CashAnalyticsLatencyIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_cash_snapshot_endpoint_meets_seeded_multi_company_latency_threshold()
    {
        var seed = await SeedScenarioAsync(datasetId: 1);

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var warmup = await GetSnapshotAsync(client, seed.CompanyId, ScenarioAsOfUtc, 30);
        Assert.Equal(seed.CompanyId, warmup.CompanyId);
        Assert.True(warmup.CurrentCashBalance > 0m);
        Assert.True(warmup.ExpectedIncomingCash > 0m);
        Assert.True(warmup.ExpectedOutgoingCash > 0m);

        var (samples, snapshot) = await MeasureAsync(
            SampleCount,
            () => GetSnapshotAsync(client, seed.CompanyId, ScenarioAsOfUtc, 30));

        var median = Median(samples);

        Assert.True(
            median <= DashboardMedianTargetMilliseconds,
            $"Dashboard finance snapshot median latency was {median:0.0} ms; target is {DashboardMedianTargetMilliseconds:0.0} ms.");
        Assert.Equal(seed.CompanyId, snapshot.CompanyId);
        Assert.True(snapshot.OverdueReceivables > 0m);
        Assert.True(snapshot.UpcomingPayables > 0m);

        var otherSnapshot = await GetSnapshotAsync(client, seed.OtherCompanyId, ScenarioAsOfUtc, 30);
        Assert.Equal(seed.OtherCompanyId, otherSnapshot.CompanyId);
        Assert.NotEqual(snapshot.CurrentCashBalance, otherSnapshot.CurrentCashBalance);
        Assert.NotEqual(snapshot.ExpectedIncomingCash, otherSnapshot.ExpectedIncomingCash);
    }

    [Fact]
    public async Task Finance_agent_cash_queries_meet_seeded_multi_company_latency_threshold_and_preserve_explainability()
    {
        var seed = await SeedScenarioAsync(datasetId: 2);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var toolContract = scope.ServiceProvider.GetRequiredService<IInternalCompanyToolContract>();

        var supportedQueries = new[]
        {
            (FinanceAgentQueryRouting.WhatShouldIPayThisWeekPhrase, FinanceAgentQueryIntents.WhatShouldIPayThisWeek),
            (FinanceAgentQueryRouting.WhichCustomersAreOverduePhrase, FinanceAgentQueryIntents.WhichCustomersAreOverdue),
            (FinanceAgentQueryRouting.WhyIsCashDownThisMonthPhrase, FinanceAgentQueryIntents.WhyIsCashDownThisMonth)
        };

        foreach (var (queryText, expectedIntent) in supportedQueries)
        {
            var warmup = await ExecuteFinanceAgentQueryAsync(toolContract, seed.CompanyId, queryText, ScenarioAsOfUtc);
            Assert.True(warmup.Success, $"Warm-up for '{queryText}' failed with status {warmup.Status}.");

            var (samples, response) = await MeasureAsync(
                SampleCount,
                () => ExecuteFinanceAgentQueryAsync(toolContract, seed.CompanyId, queryText, ScenarioAsOfUtc));

            var median = Median(samples);
            Assert.True(
                median <= AgentQueryMedianTargetMilliseconds,
                $"Finance agent query '{queryText}' median latency was {median:0.0} ms; target is {AgentQueryMedianTargetMilliseconds:0.0} ms.");

            Assert.True(response.Success, $"Finance agent query '{queryText}' failed with status {response.Status}.");
            var result = response.Data["result"]!.Deserialize<FinanceAgentQueryResultDto>();
            Assert.NotNull(result);
            Assert.Equal(seed.CompanyId, result!.CompanyId);
            Assert.Equal(expectedIntent, result.Intent);
            Assert.NotEmpty(result.MetricComponents);
            Assert.NotEmpty(result.SourceRecordIds);

            switch (expectedIntent)
            {
                case var intent when string.Equals(intent, FinanceAgentQueryIntents.WhatShouldIPayThisWeek, StringComparison.Ordinal):
                    Assert.Contains(seed.OverdueBillId, result.SourceRecordIds);
                    Assert.Contains(seed.DueThisWeekBillId, result.SourceRecordIds);
                    Assert.Contains(
                        result.MetricComponents,
                        component => component.ComponentKey == "recommended_payables_total");
                    Assert.Contains(
                        result.Items.SelectMany(item => item.MetricComponents),
                        component => component.ComponentKey == "remaining_balance");
                    break;

                case var intent when string.Equals(intent, FinanceAgentQueryIntents.WhichCustomersAreOverdue, StringComparison.Ordinal):
                    Assert.Contains(seed.OlderOverdueInvoiceId, result.SourceRecordIds);
                    Assert.Contains(seed.RecentOverdueInvoiceId, result.SourceRecordIds);
                    Assert.DoesNotContain(seed.OtherCompanyOverdueInvoiceId, result.SourceRecordIds);
                    Assert.Contains(
                        result.MetricComponents,
                        component => component.ComponentKey == "overdue_receivables_total");
                    Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.AgingBucket)));
                    break;

                default:
                    Assert.Contains(seed.CurrentRevenueTransactionId, result.SourceRecordIds);
                    Assert.Contains(seed.PreviousRevenueTransactionId, result.SourceRecordIds);
                    Assert.Contains(
                        result.MetricComponents,
                        component => component.ComponentKey == "net_cash_movement" && component.Delta < 0m);
                    Assert.Contains(
                        result.Items.SelectMany(item => item.SourceRecordIds),
                        id => id == seed.CurrentRevenueTransactionId);
                    break;
            }
        }
    }

    private async Task<CashAnalyticsLatencySeed> SeedScenarioAsync(
        int datasetId,
        int companyCount = 6,
        int extraLedgerEntriesPerCompany = 120,
        int extraDocumentsPerCompany = 80,
        int extraTransactionsPerCompany = 240)
    {
        var userId = CreateGuid(datasetId, 0, 1, 1);
        var subject = $"cash-latency-{datasetId}";
        var email = $"{subject}@example.com";
        const string displayName = "Cash Analytics Perf Owner";

        var primaryCompanyId = CreateGuid(datasetId, 1, 1, 0);
        var otherCompanyId = CreateGuid(datasetId, 2, 1, 0);

        var overdueBillId = CreateGuid(datasetId, 1, 30, 1);
        var dueThisWeekBillId = CreateGuid(datasetId, 1, 30, 2);
        var olderOverdueInvoiceId = CreateGuid(datasetId, 1, 31, 1);
        var recentOverdueInvoiceId = CreateGuid(datasetId, 1, 31, 2);
        var otherCompanyOverdueInvoiceId = CreateGuid(datasetId, 2, 31, 90);
        var previousRevenueTransactionId = CreateGuid(datasetId, 1, 50, 1);
        var currentRevenueTransactionId = CreateGuid(datasetId, 1, 50, 2);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));

            for (var companyNumber = 1; companyNumber <= companyCount; companyNumber++)
            {
                var companyId = CreateGuid(datasetId, companyNumber, 1, 0);
                var company = new Company(companyId, $"Cash Perf Company {companyNumber}");
                company.UpdateWorkspaceProfile(company.Name, null, null, "UTC", "USD", null, null);
                company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, ScenarioAsOfUtc, ScenarioAsOfUtc);
                dbContext.Companies.Add(company);
                dbContext.CompanyMemberships.Add(
                    new CompanyMembership(
                        CreateGuid(datasetId, companyNumber, 2, 0),
                        companyId,
                        userId,
                        CompanyMembershipRole.Owner,
                        CompanyMembershipStatus.Active));

                var cashAccountId = CreateGuid(datasetId, companyNumber, 10, 1);
                var revenueAccountId = CreateGuid(datasetId, companyNumber, 10, 2);
                var expenseAccountId = CreateGuid(datasetId, companyNumber, 10, 3);
                var fiscalPeriodId = CreateGuid(datasetId, companyNumber, 11, 1);
                var customerId = CreateGuid(datasetId, companyNumber, 12, 1);
                var customerTwoId = CreateGuid(datasetId, companyNumber, 12, 2);
                var supplierId = CreateGuid(datasetId, companyNumber, 12, 3);

                dbContext.FinanceAccounts.AddRange(
                    new FinanceAccount(cashAccountId, companyId, "1000", "Operating Cash", "cash", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                    new FinanceAccount(revenueAccountId, companyId, "4000", "Revenue", "revenue", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                    new FinanceAccount(expenseAccountId, companyId, "6000", "Operating Expense", "expense", "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

                dbContext.FiscalPeriods.Add(
                    new FiscalPeriod(
                        fiscalPeriodId,
                        companyId,
                        "FY26",
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

                dbContext.FinanceCounterparties.AddRange(
                    new FinanceCounterparty(customerId, companyId, $"Customer {companyNumber}A", "customer", $"customer-{datasetId}-{companyNumber}-a@example.com"),
                    new FinanceCounterparty(customerTwoId, companyId, $"Customer {companyNumber}B", "customer", $"customer-{datasetId}-{companyNumber}-b@example.com"),
                    new FinanceCounterparty(supplierId, companyId, $"Supplier {companyNumber}", "supplier", $"supplier-{datasetId}-{companyNumber}@example.com"));

                SeedBaselineLedger(
                    dbContext,
                    datasetId,
                    companyNumber,
                    companyId,
                    fiscalPeriodId,
                    cashAccountId,
                    revenueAccountId,
                    expenseAccountId);

                SeedForecastDocuments(
                    dbContext,
                    datasetId,
                    companyNumber,
                    companyId,
                    customerId,
                    customerTwoId,
                    supplierId,
                    extraDocumentsPerCompany,
                    companyNumber == 2 ? otherCompanyOverdueInvoiceId : null);

                SeedCashMovementTransactions(
                    dbContext,
                    datasetId,
                    companyNumber,
                    companyId,
                    cashAccountId,
                    customerId,
                    supplierId,
                    extraTransactionsPerCompany);

                SeedAdditionalLedgerEntries(
                    dbContext,
                    datasetId,
                    companyNumber,
                    companyId,
                    fiscalPeriodId,
                    cashAccountId,
                    revenueAccountId,
                    expenseAccountId,
                    extraLedgerEntriesPerCompany);

                if (companyNumber == 1)
                {
                    SeedPrimaryScenario(
                        dbContext,
                        datasetId,
                        companyId,
                        cashAccountId,
                        customerId,
                        customerTwoId,
                        supplierId,
                        overdueBillId,
                        dueThisWeekBillId,
                        olderOverdueInvoiceId,
                        recentOverdueInvoiceId,
                        previousRevenueTransactionId,
                        currentRevenueTransactionId);
                }
            }

            return Task.CompletedTask;
        });

        return new CashAnalyticsLatencySeed(
            primaryCompanyId,
            otherCompanyId,
            subject,
            email,
            displayName,
            overdueBillId,
            dueThisWeekBillId,
            olderOverdueInvoiceId,
            recentOverdueInvoiceId,
            otherCompanyOverdueInvoiceId,
            previousRevenueTransactionId,
            currentRevenueTransactionId);
    }

    private static void SeedBaselineLedger(
        VirtualCompanyDbContext dbContext,
        int datasetId,
        int companyNumber,
        Guid companyId,
        Guid fiscalPeriodId,
        Guid cashAccountId,
        Guid revenueAccountId,
        Guid expenseAccountId)
    {
        AddPostedLedgerEntry(
            dbContext,
            CreateGuid(datasetId, companyNumber, 20, 1),
            companyId,
            fiscalPeriodId,
            cashAccountId,
            revenueAccountId,
            ScenarioAsOfUtc.AddDays(-45),
            25000m + (companyNumber * 1000m),
            $"LE-{companyNumber:D2}-OPEN",
            "Opening cash");

        AddPostedLedgerEntry(
            dbContext,
            CreateGuid(datasetId, companyNumber, 20, 2),
            companyId,
            fiscalPeriodId,
            cashAccountId,
            expenseAccountId,
            ScenarioAsOfUtc.AddDays(-10),
            -(1200m + (companyNumber * 50m)),
            $"LE-{companyNumber:D2}-OPS",
            "Operating spend");

        AddPostedLedgerEntry(
            dbContext,
            CreateGuid(datasetId, companyNumber, 20, 3),
            companyId,
            fiscalPeriodId,
            cashAccountId,
            revenueAccountId,
            ScenarioAsOfUtc.AddDays(-3),
            1800m + (companyNumber * 25m),
            $"LE-{companyNumber:D2}-REC",
            "Collections");
    }

    private static void SeedPrimaryScenario(
        VirtualCompanyDbContext dbContext,
        int datasetId,
        Guid companyId,
        Guid cashAccountId,
        Guid customerId,
        Guid customerTwoId,
        Guid supplierId,
        Guid overdueBillId,
        Guid dueThisWeekBillId,
        Guid olderOverdueInvoiceId,
        Guid recentOverdueInvoiceId,
        Guid previousRevenueTransactionId,
        Guid currentRevenueTransactionId)
    {
        var upcomingInvoiceId = CreateGuid(datasetId, 1, 31, 3);
        var upcomingBillId = CreateGuid(datasetId, 1, 30, 3);
        var completedOutgoingPaymentId = CreateGuid(datasetId, 1, 32, 1);
        var pendingOutgoingPaymentId = CreateGuid(datasetId, 1, 32, 2);
        var completedIncomingPaymentId = CreateGuid(datasetId, 1, 32, 3);
        var pendingIncomingPaymentId = CreateGuid(datasetId, 1, 32, 4);

        dbContext.FinanceBills.AddRange(
            new FinanceBill(overdueBillId, companyId, supplierId, "BILL-OVERDUE-001", ScenarioAsOfUtc.AddDays(-20), ScenarioAsOfUtc.AddDays(-2), 500m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
            new FinanceBill(dueThisWeekBillId, companyId, supplierId, "BILL-WEEK-001", ScenarioAsOfUtc.AddDays(-4), ScenarioAsOfUtc.AddDays(2), 300m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
            new FinanceBill(upcomingBillId, companyId, supplierId, "BILL-UPCOMING-001", ScenarioAsOfUtc.AddDays(-8), ScenarioAsOfUtc.AddDays(14), 480m, "USD", "open", settlementStatus: FinanceSettlementStatuses.PartiallyPaid));

        dbContext.FinanceInvoices.AddRange(
            new FinanceInvoice(olderOverdueInvoiceId, companyId, customerId, "INV-OLDER-001", ScenarioAsOfUtc.AddDays(-50), ScenarioAsOfUtc.AddDays(-35), 1000m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
            new FinanceInvoice(recentOverdueInvoiceId, companyId, customerTwoId, "INV-RECENT-001", ScenarioAsOfUtc.AddDays(-20), ScenarioAsOfUtc.AddDays(-5), 400m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
            new FinanceInvoice(upcomingInvoiceId, companyId, customerId, "INV-UPCOMING-001", ScenarioAsOfUtc.AddDays(-6), ScenarioAsOfUtc.AddDays(10), 650m, "USD", "open", settlementStatus: FinanceSettlementStatuses.PartiallyPaid));

        dbContext.Payments.AddRange(
            new Payment(completedOutgoingPaymentId, companyId, PaymentTypes.Outgoing, 100m, "USD", ScenarioAsOfUtc.AddDays(-1), "ach", PaymentStatuses.Completed, "BILL-OVERDUE-001"),
            new Payment(pendingOutgoingPaymentId, companyId, PaymentTypes.Outgoing, 150m, "USD", ScenarioAsOfUtc.AddDays(1), "ach", PaymentStatuses.Pending, "BILL-WEEK-001"),
            new Payment(completedIncomingPaymentId, companyId, PaymentTypes.Incoming, 200m, "USD", ScenarioAsOfUtc.AddDays(-10), "ach", PaymentStatuses.Completed, "INV-OLDER-001"),
            new Payment(pendingIncomingPaymentId, companyId, PaymentTypes.Incoming, 300m, "USD", ScenarioAsOfUtc.AddDays(5), "ach", PaymentStatuses.Pending, "INV-UPCOMING-001"));

        dbContext.PaymentAllocations.AddRange(
            new PaymentAllocation(CreateGuid(datasetId, 1, 33, 1), companyId, completedOutgoingPaymentId, null, overdueBillId, 100m, "USD"),
            new PaymentAllocation(CreateGuid(datasetId, 1, 33, 2), companyId, pendingOutgoingPaymentId, null, dueThisWeekBillId, 150m, "USD"),
            new PaymentAllocation(CreateGuid(datasetId, 1, 33, 3), companyId, completedIncomingPaymentId, olderOverdueInvoiceId, null, 200m, "USD"),
            new PaymentAllocation(CreateGuid(datasetId, 1, 33, 4), companyId, pendingIncomingPaymentId, upcomingInvoiceId, null, 300m, "USD"));

        dbContext.FinanceTransactions.AddRange(
            new FinanceTransaction(previousRevenueTransactionId, companyId, cashAccountId, customerId, olderOverdueInvoiceId, null, new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), "revenue", 3000m, "USD", "Collections prior month", "TX-PREV-REV"),
            new FinanceTransaction(CreateGuid(datasetId, 1, 50, 3), companyId, cashAccountId, supplierId, null, overdueBillId, new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc), "payroll", -1200m, "USD", "Payroll prior month", "TX-PREV-PAY"),
            new FinanceTransaction(CreateGuid(datasetId, 1, 50, 4), companyId, cashAccountId, supplierId, null, dueThisWeekBillId, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), "rent", -500m, "USD", "Rent prior month", "TX-PREV-RENT"),
            new FinanceTransaction(currentRevenueTransactionId, companyId, cashAccountId, customerTwoId, recentOverdueInvoiceId, null, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc), "revenue", 1800m, "USD", "Collections current month", "TX-CURR-REV"),
            new FinanceTransaction(CreateGuid(datasetId, 1, 50, 5), companyId, cashAccountId, supplierId, null, overdueBillId, new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc), "payroll", -1600m, "USD", "Payroll current month", "TX-CURR-PAY"),
            new FinanceTransaction(CreateGuid(datasetId, 1, 50, 6), companyId, cashAccountId, supplierId, null, dueThisWeekBillId, new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc), "rent", -500m, "USD", "Rent current month", "TX-CURR-RENT"),
            new FinanceTransaction(CreateGuid(datasetId, 1, 50, 7), companyId, cashAccountId, supplierId, null, null, new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc), "software", -300m, "USD", "Software current month", "TX-CURR-SW"));
    }

    private static void SeedForecastDocuments(
        VirtualCompanyDbContext dbContext,
        int datasetId,
        int companyNumber,
        Guid companyId,
        Guid customerId,
        Guid customerTwoId,
        Guid supplierId,
        int extraDocumentsPerCompany,
        Guid? explicitOtherCompanyOverdueInvoiceId)
    {
        if (explicitOtherCompanyOverdueInvoiceId.HasValue)
        {
            dbContext.FinanceInvoices.Add(
                new FinanceInvoice(
                    explicitOtherCompanyOverdueInvoiceId.Value,
                    companyId,
                    customerId,
                    $"INV-OTHER-OVERDUE-{companyNumber:D2}",
                    ScenarioAsOfUtc.AddDays(-40),
                    ScenarioAsOfUtc.AddDays(-9),
                    777m,
                    "USD",
                    "open",
                    settlementStatus: FinanceSettlementStatuses.Unpaid));
        }

        for (var index = 0; index < extraDocumentsPerCompany; index++)
        {
            var invoiceId = CreateGuid(datasetId, companyNumber, 31, 1000 + index);
            var billId = CreateGuid(datasetId, companyNumber, 30, 1000 + index);
            var invoiceDueUtc = index % 5 == 0
                ? ScenarioAsOfUtc.AddDays(40 + (index % 7))
                : ScenarioAsOfUtc.AddDays(8 + (index % 18));
            var billDueUtc = index % 6 == 0
                ? ScenarioAsOfUtc.AddDays(45 + (index % 9))
                : ScenarioAsOfUtc.AddDays(9 + (index % 16));

            dbContext.FinanceInvoices.Add(
                new FinanceInvoice(
                    invoiceId,
                    companyId,
                    index % 2 == 0 ? customerId : customerTwoId,
                    $"INV-{companyNumber:D2}-{index:D4}",
                    ScenarioAsOfUtc.AddDays(-(index % 21) - 2),
                    invoiceDueUtc,
                    120m + (index % 11 * 15m),
                    "USD",
                    "open",
                    settlementStatus: FinanceSettlementStatuses.Unpaid));

            dbContext.FinanceBills.Add(
                new FinanceBill(
                    billId,
                    companyId,
                    supplierId,
                    $"BILL-{companyNumber:D2}-{index:D4}",
                    ScenarioAsOfUtc.AddDays(-(index % 18) - 3),
                    billDueUtc,
                    95m + (index % 9 * 20m),
                    "USD",
                    "open",
                    settlementStatus: FinanceSettlementStatuses.Unpaid));
        }
    }

    private static void SeedCashMovementTransactions(
        VirtualCompanyDbContext dbContext,
        int datasetId,
        int companyNumber,
        Guid companyId,
        Guid cashAccountId,
        Guid customerId,
        Guid supplierId,
        int extraTransactionsPerCompany)
    {
        for (var index = 0; index < extraTransactionsPerCompany; index++)
        {
            var currentDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index % 15).AddMinutes(index);
            var previousDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index % 15).AddMinutes(index);
            var revenueTransaction = index % 2 == 0;

            dbContext.FinanceTransactions.Add(
                new FinanceTransaction(
                    CreateGuid(datasetId, companyNumber, 50, 1000 + index),
                    companyId,
                    cashAccountId,
                    revenueTransaction ? customerId : supplierId,
                    null,
                    null,
                    previousDate,
                    revenueTransaction ? "revenue" : "software",
                    revenueTransaction ? 75m : -18m,
                    "USD",
                    $"Previous month generated transaction {index}",
                    $"P-{companyNumber:D2}-{index:D4}"));

            dbContext.FinanceTransactions.Add(
                new FinanceTransaction(
                    CreateGuid(datasetId, companyNumber, 51, 1000 + index),
                    companyId,
                    cashAccountId,
                    revenueTransaction ? customerId : supplierId,
                    null,
                    null,
                    currentDate,
                    revenueTransaction ? "revenue" : "software",
                    revenueTransaction ? 60m : -22m,
                    "USD",
                    $"Current month generated transaction {index}",
                    $"C-{companyNumber:D2}-{index:D4}"));
        }
    }

    private static void SeedAdditionalLedgerEntries(
        VirtualCompanyDbContext dbContext,
        int datasetId,
        int companyNumber,
        Guid companyId,
        Guid fiscalPeriodId,
        Guid cashAccountId,
        Guid revenueAccountId,
        Guid expenseAccountId,
        int extraLedgerEntriesPerCompany)
    {
        for (var index = 0; index < extraLedgerEntriesPerCompany; index++)
        {
            var entryId = CreateGuid(datasetId, companyNumber, 21, 1000 + index);
            var postedAtUtc = ScenarioAsOfUtc.AddDays(-(index % 28) - 1).AddMinutes(index);
            var cashDelta = index % 2 == 0 ? 75m : -40m;
            AddPostedLedgerEntry(
                dbContext,
                entryId,
                companyId,
                fiscalPeriodId,
                cashAccountId,
                cashDelta >= 0m ? revenueAccountId : expenseAccountId,
                postedAtUtc,
                cashDelta,
                $"LE-{companyNumber:D2}-GEN-{index:D4}",
                $"Generated cash movement {index}");
        }
    }

    private static void AddPostedLedgerEntry(
        VirtualCompanyDbContext dbContext,
        Guid entryId,
        Guid companyId,
        Guid fiscalPeriodId,
        Guid cashAccountId,
        Guid offsetAccountId,
        DateTime postedAtUtc,
        decimal cashDelta,
        string entryNumber,
        string description)
    {
        dbContext.LedgerEntries.Add(
            new LedgerEntry(
                entryId,
                companyId,
                fiscalPeriodId,
                entryNumber,
                postedAtUtc,
                LedgerEntryStatuses.Posted,
                description,
                postedAtUtc: postedAtUtc));

        if (cashDelta >= 0m)
        {
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(CreateLineGuid(entryId, 1), companyId, entryId, cashAccountId, cashDelta, 0m, "USD", description),
                new LedgerEntryLine(CreateLineGuid(entryId, 2), companyId, entryId, offsetAccountId, 0m, cashDelta, "USD", description));
        }
        else
        {
            var absoluteAmount = Math.Abs(cashDelta);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(CreateLineGuid(entryId, 1), companyId, entryId, offsetAccountId, absoluteAmount, 0m, "USD", description),
                new LedgerEntryLine(CreateLineGuid(entryId, 2), companyId, entryId, cashAccountId, 0m, absoluteAmount, "USD", description));
        }
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

    private static Task<InternalToolExecutionResponse> ExecuteFinanceAgentQueryAsync(
        IInternalCompanyToolContract toolContract,
        Guid companyId,
        string queryText,
        DateTime asOfUtc)
    {
        var request = new InternalToolExecutionRequest(
            "resolve_finance_agent_query",
            new InternalToolExecutionContext(
                companyId,
                CreateGuid(99, 0, 1, 1),
                CreateGuid(99, 0, 2, 1),
                ToolActionType.Read,
                "finance"),
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["queryText"] = JsonValue.Create(queryText),
                ["asOfUtc"] = JsonValue.Create(asOfUtc)
            });

        return toolContract.ExecuteAsync(request, CancellationToken.None);
    }

    private static async Task<(IReadOnlyList<double> Samples, T LastResult)> MeasureAsync<T>(
        int sampleCount,
        Func<Task<T>> action)
    {
        var samples = new List<double>(sampleCount);
        T? lastResult = default;

        for (var index = 0; index < sampleCount; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            lastResult = await action();
            stopwatch.Stop();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return (samples, lastResult!);
    }

    private static double Median(IReadOnlyList<double> samples)
    {
        var ordered = samples.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private HttpClient CreateAuthenticatedClient(
        string subject,
        string email,
        string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private static Guid CreateGuid(int datasetId, int companyNumber, int kind, int sequence) =>
        Guid.Parse($"{datasetId:X8}-{companyNumber:X4}-{kind:X4}-8000-{sequence:X12}");

    private static Guid CreateLineGuid(Guid entryId, int suffix)
    {
        var bytes = entryId.ToByteArray();
        bytes[15] = (byte)suffix;
        return new Guid(bytes);
    }

    private sealed record CashAnalyticsLatencySeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid OverdueBillId,
        Guid DueThisWeekBillId,
        Guid OlderOverdueInvoiceId,
        Guid RecentOverdueInvoiceId,
        Guid OtherCompanyOverdueInvoiceId,
        Guid PreviousRevenueTransactionId,
        Guid CurrentRevenueTransactionId);

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
}
