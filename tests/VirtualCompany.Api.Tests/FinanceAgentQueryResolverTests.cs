using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentQueryResolverTests
{
    private static readonly TimeSpan LocalAgentQueryPerformanceThreshold = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Resolver_returns_weekly_payables_with_explainability_and_deterministic_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var scenario = SeedScenario(dbContext);
        await dbContext.SaveChangesAsync();

        var accessor = new RequestCompanyContextAccessor();
        accessor.SetCompanyId(scenario.CompanyId);
        var service = new CompanyFinanceReadService(dbContext, accessor);

        var result = await service.ResolveAgentQueryAsync(
            new GetFinanceAgentQueryQuery(
                scenario.CompanyId,
                "what should i pay this week",
                scenario.AsOfUtc),
            CancellationToken.None);

        Assert.Equal(FinanceAgentQueryIntents.WhatShouldIPayThisWeek, result.Intent);
        Assert.Equal(new[] { scenario.OverdueBillId, scenario.DueThisWeekBillId }, result.Items.Select(x => x.RecordId));
        Assert.All(result.Items, item => Assert.Contains(item.MetricComponents, component => component.ComponentKey == "remaining_balance"));
        Assert.Equal(
            new[] { scenario.CompletedOutgoingAllocationId, scenario.OverdueBillId },
            result.Items[0].SourceRecordIds);
        Assert.Equal(
            new[] { scenario.PendingOutgoingAllocationId, scenario.DueThisWeekBillId },
            result.Items[1].SourceRecordIds);
        Assert.Contains(
            result.MetricComponents,
            component => component.ComponentKey == "recommended_payables_total" &&
                         component.SourceRecordIds.SequenceEqual(new[]
                         {
                             scenario.CompletedOutgoingAllocationId,
                             scenario.PendingOutgoingAllocationId,
                             scenario.OverdueBillId,
                             scenario.DueThisWeekBillId
                         }));
        Assert.All(result.Items, item => Assert.Contains(item.SourceRecordIds, id => id == item.RecordId));
        Assert.All(
            result.Items,
            item => Assert.Contains(
                item.MetricComponents,
                component => component.ComponentKey == "scheduled_outgoing_this_week" || component.ComponentKey == "completed_outgoing_allocations"));
    }

    [Fact]
    public async Task Resolver_returns_overdue_customers_only_for_active_company()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var scenario = SeedScenario(dbContext);
        await dbContext.SaveChangesAsync();

        var accessor = new RequestCompanyContextAccessor();
        accessor.SetCompanyId(scenario.CompanyId);
        var service = new CompanyFinanceReadService(dbContext, accessor);

        var result = await service.ResolveAgentQueryAsync(
            new GetFinanceAgentQueryQuery(
                scenario.CompanyId,
                "which customers are overdue",
                scenario.AsOfUtc),
            CancellationToken.None);

        Assert.Equal(FinanceAgentQueryIntents.WhichCustomersAreOverdue, result.Intent);
        Assert.Equal(new[] { scenario.OlderOverdueInvoiceId, scenario.RecentOverdueInvoiceId }, result.Items.Select(x => x.RecordId));
        Assert.DoesNotContain(result.SourceRecordIds, id => id == scenario.OtherCompanyInvoiceId);
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.AgingBucket)));
        Assert.Equal(
            new[] { scenario.CompletedIncomingAllocationId, scenario.OlderOverdueInvoiceId },
            result.Items[0].SourceRecordIds);
        Assert.Equal(
            new[] { scenario.RecentOverdueInvoiceId },
            result.Items[1].SourceRecordIds);
        Assert.Contains(
            result.MetricComponents,
            component => component.ComponentKey == "overdue_receivables_total" &&
                         component.SourceRecordIds.SequenceEqual(new[] { scenario.CompletedIncomingAllocationId, scenario.OlderOverdueInvoiceId, scenario.RecentOverdueInvoiceId }));
    }

    [Fact]
    public async Task Resolver_returns_cash_down_components_and_source_transaction_ids()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var scenario = SeedScenario(dbContext);
        await dbContext.SaveChangesAsync();

        var accessor = new RequestCompanyContextAccessor();
        accessor.SetCompanyId(scenario.CompanyId);
        var service = new CompanyFinanceReadService(dbContext, accessor);

        var result = await service.ResolveAgentQueryAsync(
            new GetFinanceAgentQueryQuery(
                scenario.CompanyId,
                "why is cash down this month",
                scenario.AsOfUtc),
            CancellationToken.None);

        Assert.Equal(FinanceAgentQueryIntents.WhyIsCashDownThisMonth, result.Intent);
        Assert.Contains(result.MetricComponents, component => component.ComponentKey == "net_cash_movement" && component.Delta < 0m);
        Assert.Contains(result.MetricComponents, component => component.ComponentKey == "revenue" && component.Delta < 0m);
        Assert.Contains(result.MetricComponents, component => component.ComponentKey == "payroll" && component.Delta < 0m);
        Assert.Contains(result.SourceRecordIds, id => id == scenario.CurrentRevenueTransactionId);
        Assert.Contains(result.SourceRecordIds, id => id == scenario.PreviousRevenueTransactionId);
        Assert.Equal(
            new[]
            {
                scenario.CurrentRevenueTransactionId,
                scenario.CurrentPayrollTransactionId,
                scenario.PreviousPayrollTransactionId,
                scenario.PreviousRevenueTransactionId
            },
            result.Items.Single(item => string.Equals(item.Reference, "revenue", StringComparison.Ordinal)).SourceRecordIds);
    }

    [Fact]
    public async Task Resolver_enforces_active_company_context()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var scenario = SeedScenario(dbContext);
        await dbContext.SaveChangesAsync();

        var accessor = new RequestCompanyContextAccessor();
        accessor.SetCompanyId(Guid.NewGuid());
        var service = new CompanyFinanceReadService(dbContext, accessor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveAgentQueryAsync(
            new GetFinanceAgentQueryQuery(
                scenario.CompanyId,
                "what should i pay this week",
                scenario.AsOfUtc),
            CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_handles_supported_queries_on_seeded_multi_company_dataset_within_local_threshold()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var scenario = SeedScenario(dbContext, additionalCurrentMonthTransactionsPerCompany: 250);
        await dbContext.SaveChangesAsync();

        var accessor = new RequestCompanyContextAccessor();
        accessor.SetCompanyId(scenario.CompanyId);
        var service = new CompanyFinanceReadService(dbContext, accessor);

        var supportedQueries = new[]
        {
            (FinanceAgentQueryRouting.WhatShouldIPayThisWeekPhrase, FinanceAgentQueryIntents.WhatShouldIPayThisWeek),
            (FinanceAgentQueryRouting.WhichCustomersAreOverduePhrase, FinanceAgentQueryIntents.WhichCustomersAreOverdue),
            (FinanceAgentQueryRouting.WhyIsCashDownThisMonthPhrase, FinanceAgentQueryIntents.WhyIsCashDownThisMonth)
        };

        foreach (var (queryText, expectedIntent) in supportedQueries)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await service.ResolveAgentQueryAsync(
                new GetFinanceAgentQueryQuery(
                    scenario.CompanyId,
                    queryText,
                    scenario.AsOfUtc),
                CancellationToken.None);
            stopwatch.Stop();

            Assert.Equal(expectedIntent, result.Intent);
            Assert.True(
                stopwatch.Elapsed < LocalAgentQueryPerformanceThreshold,
                $"Resolver '{queryText}' completed in {stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
            Assert.NotEmpty(result.MetricComponents);
            Assert.NotEmpty(result.SourceRecordIds);
            Assert.DoesNotContain(result.SourceRecordIds, id => id == scenario.OtherCompanyInvoiceId);
        }
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

    private static FinanceAgentScenario SeedScenario(VirtualCompanyDbContext dbContext, int additionalCurrentMonthTransactionsPerCompany = 0)
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var otherCompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var asOfUtc = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

        var company = new Company(companyId, "Finance Agent Company");
        company.UpdateWorkspaceProfile("Finance Agent Company", null, null, "UTC", "USD", null, null);
        var otherCompany = new Company(otherCompanyId, "Other Finance Agent Company");
        otherCompany.UpdateWorkspaceProfile("Other Finance Agent Company", null, null, "UTC", "USD", null, null);
        dbContext.Companies.AddRange(company, otherCompany);

        var cashAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var otherCashAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(cashAccountId, companyId, "1000", "Operating Cash", "asset", "USD", 10000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(otherCashAccountId, otherCompanyId, "1000", "Operating Cash", "asset", "USD", 9000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var customerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var secondCustomerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var supplierId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var otherCustomerId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var otherSupplierId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        dbContext.FinanceCounterparties.AddRange(
            new FinanceCounterparty(customerId, companyId, "Northwind", "customer", "northwind@example.com"),
            new FinanceCounterparty(secondCustomerId, companyId, "Fabrikam", "customer", "fabrikam@example.com"),
            new FinanceCounterparty(supplierId, companyId, "Contoso Vendor", "supplier", "vendor@example.com"),
            new FinanceCounterparty(otherCustomerId, otherCompanyId, "Other Customer", "customer", "other.customer@example.com"),
            new FinanceCounterparty(otherSupplierId, otherCompanyId, "Other Supplier", "supplier", "other.supplier@example.com"));

        var overdueBillId = Guid.Parse("10101010-1010-1010-1010-101010101010");
        var dueThisWeekBillId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        dbContext.FinanceBills.AddRange(
            new FinanceBill(overdueBillId, companyId, supplierId, "BILL-OVERDUE-001", asOfUtc.AddDays(-20), asOfUtc.AddDays(-2), 500m, "USD", "open", settlementStatus: "unpaid"),
            new FinanceBill(dueThisWeekBillId, companyId, supplierId, "BILL-WEEK-001", asOfUtc.AddDays(-4), asOfUtc.AddDays(2), 300m, "USD", "open", settlementStatus: "unpaid"),
            new FinanceBill(Guid.Parse("30303030-3030-3030-3030-303030303030"), otherCompanyId, otherSupplierId, "BILL-OTHER-001", asOfUtc.AddDays(-7), asOfUtc.AddDays(1), 999m, "USD", "open", settlementStatus: "unpaid"));

        var olderOverdueInvoiceId = Guid.Parse("40404040-4040-4040-4040-404040404040");
        var recentOverdueInvoiceId = Guid.Parse("50505050-5050-5050-5050-505050505050");
        var otherCompanyInvoiceId = Guid.Parse("60606060-6060-6060-6060-606060606060");
        dbContext.FinanceInvoices.AddRange(
            new FinanceInvoice(olderOverdueInvoiceId, companyId, customerId, "INV-OLDER-001", asOfUtc.AddDays(-50), asOfUtc.AddDays(-35), 1000m, "USD", "open", settlementStatus: "unpaid"),
            new FinanceInvoice(recentOverdueInvoiceId, companyId, secondCustomerId, "INV-RECENT-001", asOfUtc.AddDays(-20), asOfUtc.AddDays(-5), 400m, "USD", "open", settlementStatus: "unpaid"),
            new FinanceInvoice(otherCompanyInvoiceId, otherCompanyId, otherCustomerId, "INV-OTHER-001", asOfUtc.AddDays(-15), asOfUtc.AddDays(-4), 777m, "USD", "open", settlementStatus: "unpaid"));

        var completedOutgoingAllocationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var pendingOutgoingAllocationId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var completedIncomingPaymentId = Guid.Parse("90909090-9090-9090-9090-909090909090");
        dbContext.Payments.AddRange(
            new Payment(completedOutgoingPaymentId, companyId, "outgoing", 100m, "USD", asOfUtc.AddDays(-1), "ach", "completed", "BILL-OVERDUE-001"),
            new Payment(pendingOutgoingPaymentId, companyId, "outgoing", 150m, "USD", asOfUtc.AddDays(1), "ach", "pending", "BILL-WEEK-001"),
            new Payment(completedIncomingPaymentId, companyId, "incoming", 200m, "USD", asOfUtc.AddDays(-10), "ach", "completed", "INV-OLDER-001"));
        dbContext.PaymentAllocations.AddRange(
            new PaymentAllocation(completedOutgoingAllocationId, companyId, completedOutgoingPaymentId, null, overdueBillId, 100m, "USD"),
            new PaymentAllocation(pendingOutgoingAllocationId, companyId, pendingOutgoingPaymentId, null, dueThisWeekBillId, 150m, "USD"),
            new PaymentAllocation(Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000"), companyId, completedIncomingPaymentId, olderOverdueInvoiceId, null, 200m, "USD"));

        var previousRevenueTransactionId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        var currentRevenueTransactionId = Guid.Parse("13131313-1313-1313-1313-131313131313");
        dbContext.FinanceTransactions.AddRange(
            new FinanceTransaction(previousRevenueTransactionId, companyId, cashAccountId, customerId, olderOverdueInvoiceId, null, new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), "revenue", 3000m, "USD", "Collections prior month", "TX-PREV-REV"),
            new FinanceTransaction(Guid.Parse("14141414-1414-1414-1414-141414141414"), companyId, cashAccountId, supplierId, null, overdueBillId, new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc), "payroll", -1200m, "USD", "Payroll prior month", "TX-PREV-PAYROLL"),
            new FinanceTransaction(Guid.Parse("15151515-1515-1515-1515-151515151515"), companyId, cashAccountId, supplierId, null, dueThisWeekBillId, new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc), "rent", -500m, "USD", "Rent prior month", "TX-PREV-RENT"),
            new FinanceTransaction(currentRevenueTransactionId, companyId, cashAccountId, secondCustomerId, recentOverdueInvoiceId, null, new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc), "revenue", 1800m, "USD", "Collections current month", "TX-CURR-REV"),
            new FinanceTransaction(Guid.Parse("16161616-1616-1616-1616-161616161616"), companyId, cashAccountId, supplierId, null, overdueBillId, new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc), "payroll", -1600m, "USD", "Payroll current month", "TX-CURR-PAYROLL"),
            new FinanceTransaction(Guid.Parse("17171717-1717-1717-1717-171717171717"), companyId, cashAccountId, supplierId, null, dueThisWeekBillId, new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc), "rent", -500m, "USD", "Rent current month", "TX-CURR-RENT"),
            new FinanceTransaction(Guid.Parse("18181818-1818-1818-1818-181818181818"), companyId, cashAccountId, supplierId, null, null, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), "software", -300m, "USD", "Software current month", "TX-CURR-SW"),
            new FinanceTransaction(Guid.Parse("19191919-1919-1919-1919-191919191919"), otherCompanyId, otherCashAccountId, otherCustomerId, otherCompanyInvoiceId, null, new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc), "revenue", 700m, "USD", "Other company collections", "TX-OTHER-REV"));

        for (var index = 0; index < additionalCurrentMonthTransactionsPerCompany; index++)
        {
            dbContext.FinanceTransactions.Add(
                new FinanceTransaction(
                    Guid.NewGuid(),
                    companyId,
                    cashAccountId,
                    supplierId,
                    null,
                    null,
                    new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                    index % 2 == 0 ? "revenue" : "software",
                    index % 2 == 0 ? 25m : -10m,
                    "USD",
                    $"Generated transaction {index}",
                    $"GEN-A-{index:0000}"));
            dbContext.FinanceTransactions.Add(
                new FinanceTransaction(
                    Guid.NewGuid(),
                    otherCompanyId,
                    otherCashAccountId,
                    otherSupplierId,
                    null,
                    null,
                    new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                    index % 2 == 0 ? "revenue" : "software",
                    index % 2 == 0 ? 25m : -10m,
                    "USD",
                    $"Other generated transaction {index}",
                    $"GEN-B-{index:0000}"));
        }

        return new FinanceAgentScenario(
            companyId,
            otherCompanyId,
            asOfUtc,
            overdueBillId,
            completedOutgoingAllocationId,
            dueThisWeekBillId,
            pendingOutgoingAllocationId,
            olderOverdueInvoiceId,
            Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000"),
            recentOverdueInvoiceId,
            otherCompanyInvoiceId,
            Guid.Parse("14141414-1414-1414-1414-141414141414"),
            Guid.Parse("16161616-1616-1616-1616-161616161616"),
            previousRevenueTransactionId,
            currentRevenueTransactionId);
    }

    private sealed record FinanceAgentScenario(
        Guid CompanyId,
        Guid OtherCompanyId,
        DateTime AsOfUtc,
        Guid OverdueBillId,
        Guid CompletedOutgoingAllocationId,
        Guid DueThisWeekBillId,
        Guid PendingOutgoingAllocationId,
        Guid OlderOverdueInvoiceId,
        Guid CompletedIncomingAllocationId,
        Guid RecentOverdueInvoiceId,
        Guid OtherCompanyInvoiceId,
        Guid PreviousPayrollTransactionId,
        Guid CurrentPayrollTransactionId,
        Guid PreviousRevenueTransactionId,
        Guid CurrentRevenueTransactionId);
}