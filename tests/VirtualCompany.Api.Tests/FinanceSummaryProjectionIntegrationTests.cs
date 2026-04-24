using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSummaryProjectionIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime ScenarioAsOfUtc = new(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestWebApplicationFactory _factory;

    public FinanceSummaryProjectionIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetFinanceSummary_ReturnsDeterministicPointInTimeProjection_AndUsesSimulationClockByDefault()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var stopwatch = Stopwatch.StartNew();
        var firstResponse = await client.GetAsync($"/api/companies/{seed.CompanyId:D}/finance-summary");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));

        var first = await firstResponse.Content.ReadFromJsonAsync<FinanceSummaryResponse>();
        Assert.NotNull(first);
        AssertSummary(first!, seed.CompanyId);

        var second = await client.GetFromJsonAsync<FinanceSummaryResponse>(
            $"/api/companies/{seed.CompanyId:D}/finance-summary?asOfUtc={Uri.EscapeDataString(ScenarioAsOfUtc.ToString("O"))}");

        Assert.NotNull(second);
        Assert.All(first!.RecentAssetPurchases, asset => Assert.Equal(seed.CompanyId, asset.CompanyId));
        Assert.Equivalent(first, second);
    }

    [Fact]
    public async Task GetFinanceSummary_AliasRoutesReturnSameProjection_ForDashboardAgentAndDebugConsumers()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var query = $"asOfUtc={Uri.EscapeDataString(ScenarioAsOfUtc.ToString("O"))}&recentAssetPurchaseLimit=1";

        var canonical = await client.GetFromJsonAsync<FinanceSummaryResponse>(
            $"/api/companies/{seed.CompanyId:D}/finance-summary?{query}");
        var dashboard = await client.GetFromJsonAsync<FinanceSummaryResponse>(
            $"/api/companies/{seed.CompanyId:D}/finance/dashboard/summary?{query}");
        var agent = await client.GetFromJsonAsync<FinanceSummaryResponse>(
            $"/api/companies/{seed.CompanyId:D}/finance/agent-context/summary?{query}");
        var debug = await client.GetFromJsonAsync<FinanceSummaryResponse>(
            $"/internal/companies/{seed.CompanyId:D}/finance/debug/summary?{query}");

        Assert.NotNull(canonical);
        Assert.NotNull(dashboard);
        Assert.NotNull(agent);
        Assert.NotNull(debug);

        Assert.Single(canonical!.RecentAssetPurchases);
        Assert.All(canonical.RecentAssetPurchases, asset => Assert.Equal(seed.CompanyId, asset.CompanyId));
        Assert.Equivalent(canonical, dashboard);
        Assert.Equivalent(canonical, agent);
        Assert.Equivalent(canonical, debug);
    }

    [Fact]
    public async Task GetFinanceSummary_ConsistencyCheckMatchesSourceRecords_AndRepeatedDebugReadsStayStable()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var query = $"asOfUtc={Uri.EscapeDataString(ScenarioAsOfUtc.ToString("O"))}&includeConsistencyCheck=true";

        var stopwatch = Stopwatch.StartNew();
        var firstResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/debug/summary?{query}");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));

        var first = await firstResponse.Content.ReadFromJsonAsync<FinanceSummaryResponse>();
        var second = await client.GetFromJsonAsync<FinanceSummaryResponse>(
            $"/internal/companies/{seed.CompanyId:D}/finance/debug/summary?{query}");

        Assert.NotNull(first);
        Assert.NotNull(second);
        AssertSummary(first!, seed.CompanyId);
        Assert.NotNull(first.ConsistencyCheck);
        Assert.True(first.ConsistencyCheck!.IsConsistent);
        Assert.Equal(11, first.ConsistencyCheck.SourceRecordCount);
        AssertConsistencyMetrics(first.ConsistencyCheck);
        Assert.Equivalent(first, second);
    }

    [Fact]
    public async Task GetFinanceSummary_ExcludesFutureRecords_AndDoesNotLeakCrossTenantRows()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName, seed.CompanyId);

        var summary = await client.GetFromJsonAsync<FinanceSummaryResponse>(
            $"/api/companies/{seed.CompanyId:D}/finance-summary?asOfUtc={Uri.EscapeDataString(ScenarioAsOfUtc.ToString("O"))}");

        Assert.NotNull(summary);
        Assert.Equal(1125m, summary!.CurrentCash);
        Assert.Equal(1050m, summary.AccountsReceivable);
        Assert.Equal(450m, summary.OverdueReceivables);
        Assert.Equal(1555m, summary.AccountsPayable);
        Assert.Equal(1180m, summary.OverduePayables);
        Assert.Equal(1300m, summary.MonthlyRevenue);
        Assert.Equal(1850m, summary.MonthlyCosts);
        Assert.Equal(2, summary.RecentAssetPurchaseCount);
        Assert.Equal(1150m, summary.RecentAssetPurchaseTotalAmount);
        Assert.All(summary.RecentAssetPurchases, asset => Assert.Equal(seed.CompanyId, asset.CompanyId));
        Assert.DoesNotContain(summary.RecentAssetPurchases, asset => asset.ReferenceNumber == "AST-FUTURE-001");
        Assert.DoesNotContain(summary.RecentAssetPurchases, asset => asset.ReferenceNumber == "AST-OTHER-001");
    }

    [Fact]
    public async Task GetFinanceSummary_ReturnsZeroValuedProjectionForCompanyWithoutFinanceData()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var subject = $"finance-summary-empty-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, "Empty Finance User", "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Empty Finance Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(subject, email, "Empty Finance User", companyId);
        var response = await client.GetAsync($"/api/companies/{companyId:D}/finance-summary?asOfUtc={Uri.EscapeDataString(ScenarioAsOfUtc.ToString("O"))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<FinanceSummaryResponse>();

        Assert.NotNull(summary);
        Assert.Equal(companyId, summary!.CompanyId);
        Assert.Equal(0m, summary.CurrentCash);
        Assert.Equal(0m, summary.AccountsReceivable);
        Assert.Equal(0m, summary.OverdueReceivables);
        Assert.Equal(0m, summary.AccountsPayable);
        Assert.Equal(0m, summary.OverduePayables);
        Assert.Equal(0m, summary.MonthlyRevenue);
        Assert.Equal(0m, summary.MonthlyCosts);
        Assert.False(summary.HasFinanceData);
        Assert.Empty(summary.RecentAssetPurchases);
    }

    private async Task<FinanceSummarySeed> SeedScenarioAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"finance-summary-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Summary User";

        var cashAccountId = Guid.NewGuid();
        var otherCashAccountId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var simulationSessionId = Guid.NewGuid();
        var simulationStartUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var otherCounterpartyId = Guid.NewGuid();
        var eventSequence = 0;

        SimulationEventRecord CreateSimulationEvent(
            Guid sourceEntityId,
            string eventType,
            string sourceEntityType,
            DateTime simulationDateUtc,
            decimal? cashBefore = null,
            decimal? cashDelta = null,
            decimal? cashAfter = null)
        {
            eventSequence++;
            return new SimulationEventRecord(
                Guid.NewGuid(),
                companyId,
                simulationSessionId,
                73,
                simulationStartUtc,
                simulationDateUtc,
                eventType,
                sourceEntityType,
                sourceEntityId,
                null,
                null,
                eventSequence,
                $"finance-summary-{eventSequence}",
                cashBefore,
                cashDelta,
                cashAfter,
                simulationDateUtc);
        }

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));

            var overdueInvoiceId = Guid.NewGuid();
            var partialInvoiceId = Guid.NewGuid();
            var priorInvoiceId = Guid.NewGuid();
            var futureInvoiceId = Guid.NewGuid();
            var otherInvoiceId = Guid.NewGuid();
            var overdueBillId = Guid.NewGuid();
            var partialBillId = Guid.NewGuid();
            var priorBillId = Guid.NewGuid();
            var futureBillId = Guid.NewGuid();
            dbContext.Companies.AddRange(
                new Company(companyId, "Projection Finance Company"),
                new Company(otherCompanyId, "Other Projection Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.CompanySimulationStates.Add(new CompanySimulationState(
                Guid.NewGuid(),
                companyId,
                simulationSessionId,
                CompanySimulationStatus.Running,
                simulationStartUtc,
                ScenarioAsOfUtc,
                ScenarioAsOfUtc,
                true,
                73,
                null,
                ScenarioAsOfUtc.AddDays(-10),
                ScenarioAsOfUtc));

            dbContext.FinanceAccounts.AddRange(
                new FinanceAccount(cashAccountId, companyId, "1000", "Operating Cash", "cash", "USD", 1000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(otherCashAccountId, otherCompanyId, "1000", "Other Cash", "cash", "USD", 9000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

            dbContext.FinanceBalances.AddRange(
                new FinanceBalance(Guid.NewGuid(), companyId, cashAccountId, new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), 900m, "USD"),
                new FinanceBalance(Guid.NewGuid(), otherCompanyId, otherCashAccountId, new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), 9100m, "USD"));

            dbContext.FinanceCounterparties.AddRange(
                new FinanceCounterparty(customerId, companyId, "Northwind", "customer", "northwind@example.com"),
                new FinanceCounterparty(supplierId, companyId, "Wingtip", "supplier", "wingtip@example.com"),
                new FinanceCounterparty(otherCounterpartyId, otherCompanyId, "Fourth Coffee", "supplier", "fourthcoffee@example.com"));
            var overdueInvoiceEvent = CreateSimulationEvent(overdueInvoiceId, "finance.invoice.generated", "finance_invoice", new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc));
            var partialInvoiceEvent = CreateSimulationEvent(partialInvoiceId, "finance.invoice.generated", "finance_invoice", new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc));
            var priorInvoiceEvent = CreateSimulationEvent(priorInvoiceId, "finance.invoice.generated", "finance_invoice", new DateTime(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc));
            var overdueBillEvent = CreateSimulationEvent(overdueBillId, "finance.bill.generated", "finance_bill", new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc));
            var partialBillEvent = CreateSimulationEvent(partialBillId, "finance.bill.generated", "finance_bill", new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc));
            var priorBillEvent = CreateSimulationEvent(priorBillId, "finance.bill.generated", "finance_bill", new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc));
            dbContext.FinanceInvoices.AddRange(
                new FinanceInvoice(overdueInvoiceId, companyId, customerId, "INV-OVERDUE-001", new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), 300m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid, sourceSimulationEventRecordId: overdueInvoiceEvent.Id),
                new FinanceInvoice(partialInvoiceId, companyId, customerId, "INV-PART-001", new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc), 1000m, "USD", "open", settlementStatus: FinanceSettlementStatuses.PartiallyPaid, sourceSimulationEventRecordId: partialInvoiceEvent.Id),
                new FinanceInvoice(priorInvoiceId, companyId, customerId, "INV-PRIOR-001", new DateTime(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), 150m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid, sourceSimulationEventRecordId: priorInvoiceEvent.Id),
                new FinanceInvoice(futureInvoiceId, companyId, customerId, "INV-FUTURE-001", new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), 700m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceInvoice(otherInvoiceId, otherCompanyId, otherCounterpartyId, "INV-OTHER-001", new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc), 999m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid));

            var otherBillId = Guid.NewGuid();
            dbContext.FinanceBills.AddRange(
                new FinanceBill(overdueBillId, companyId, supplierId, "BILL-OVERDUE-001", new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc), 200m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid, sourceSimulationEventRecordId: overdueBillEvent.Id),
                new FinanceBill(partialBillId, companyId, supplierId, "BILL-PART-001", new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc), 500m, "USD", "open", settlementStatus: FinanceSettlementStatuses.PartiallyPaid, sourceSimulationEventRecordId: partialBillEvent.Id),
                new FinanceBill(priorBillId, companyId, supplierId, "BILL-PRIOR-001", new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc), 80m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid, sourceSimulationEventRecordId: priorBillEvent.Id),
                new FinanceBill(futureBillId, companyId, supplierId, "BILL-FUTURE-001", new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc), 300m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid),
                new FinanceBill(otherBillId, otherCompanyId, otherCounterpartyId, "BILL-OTHER-001", new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), 444m, "USD", "open", settlementStatus: FinanceSettlementStatuses.Unpaid));

            var incomingPaymentId = Guid.NewGuid();
            var outgoingPaymentId = Guid.NewGuid();
            var futureIncomingPaymentId = Guid.NewGuid();
            var incomingPaymentEvent = CreateSimulationEvent(incomingPaymentId, "finance.payment.completed", "payment", ScenarioAsOfUtc);
            var outgoingPaymentEvent = CreateSimulationEvent(outgoingPaymentId, "finance.payment.completed", "payment", ScenarioAsOfUtc);
            var incomingAllocationEvent = CreateSimulationEvent(Guid.NewGuid(), "finance.payment.allocated", "payment_allocation", ScenarioAsOfUtc);
            var outgoingAllocationEvent = CreateSimulationEvent(Guid.NewGuid(), "finance.payment.allocated", "payment_allocation", ScenarioAsOfUtc);
            var cashReceiptTransactionId = Guid.NewGuid();
            var bankFeeTransactionId = Guid.NewGuid();
            var cashReceiptEvent = CreateSimulationEvent(cashReceiptTransactionId, "finance.transaction.posted", "finance_transaction", new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc), 900m, 250m, 1150m);
            var bankFeeEvent = CreateSimulationEvent(bankFeeTransactionId, "finance.transaction.posted", "finance_transaction", ScenarioAsOfUtc, 1150m, -25m, 1125m);

            dbContext.SimulationEventRecords.AddRange(
                overdueInvoiceEvent,
                partialInvoiceEvent,
                priorInvoiceEvent,
                overdueBillEvent,
                partialBillEvent,
                priorBillEvent,
                incomingPaymentEvent,
                outgoingPaymentEvent,
                incomingAllocationEvent,
                outgoingAllocationEvent,
                cashReceiptEvent,
                bankFeeEvent);

            dbContext.SimulationCashDeltaRecords.AddRange(
                new SimulationCashDeltaRecord(Guid.NewGuid(), companyId, cashReceiptEvent.Id, new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc), "finance_transaction", cashReceiptTransactionId, 900m, 250m, 1150m, new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc)),
                new SimulationCashDeltaRecord(Guid.NewGuid(), companyId, bankFeeEvent.Id, ScenarioAsOfUtc, "finance_transaction", bankFeeTransactionId, 1150m, -25m, 1125m, ScenarioAsOfUtc));

            dbContext.Payments.AddRange(
                new Payment(incomingPaymentId, companyId, PaymentTypes.Incoming, 400m, "USD", ScenarioAsOfUtc, "bank_transfer", PaymentStatuses.Completed, "INV-PART-001", sourceSimulationEventRecordId: incomingPaymentEvent.Id),
                new Payment(outgoingPaymentId, companyId, PaymentTypes.Outgoing, 125m, "USD", ScenarioAsOfUtc, "bank_transfer", PaymentStatuses.Completed, "BILL-PART-001", sourceSimulationEventRecordId: outgoingPaymentEvent.Id),
                new Payment(futureIncomingPaymentId, companyId, PaymentTypes.Incoming, 100m, "USD", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), "bank_transfer", PaymentStatuses.Completed, "INV-PART-001"));

            dbContext.PaymentAllocations.AddRange(
                new PaymentAllocation(Guid.NewGuid(), companyId, incomingPaymentId, partialInvoiceId, null, 400m, "USD", sourceSimulationEventRecordId: incomingAllocationEvent.Id, paymentSourceSimulationEventRecordId: incomingPaymentEvent.Id, targetSourceSimulationEventRecordId: partialInvoiceEvent.Id),
                new PaymentAllocation(Guid.NewGuid(), companyId, outgoingPaymentId, null, partialBillId, 125m, "USD", sourceSimulationEventRecordId: outgoingAllocationEvent.Id, paymentSourceSimulationEventRecordId: outgoingPaymentEvent.Id, targetSourceSimulationEventRecordId: partialBillEvent.Id),
                new PaymentAllocation(Guid.NewGuid(), companyId, futureIncomingPaymentId, partialInvoiceId, null, 100m, "USD"));

            var payableAssetId = Guid.NewGuid();
            var cashAssetId = Guid.NewGuid();
            var payableAssetEvent = CreateSimulationEvent(payableAssetId, "finance.asset.purchased", "finance_asset", new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc));
            var cashAssetEvent = CreateSimulationEvent(cashAssetId, "finance.asset.purchased", "finance_asset", new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc));
            dbContext.SimulationEventRecords.AddRange(payableAssetEvent, cashAssetEvent);

            dbContext.FinanceTransactions.AddRange(
                new FinanceTransaction(cashReceiptTransactionId, companyId, cashAccountId, null, null, null, new DateTime(2026, 4, 16, 9, 0, 0, DateTimeKind.Utc), "customer_payment", 250m, "USD", "Cash receipt", "TX-CASH-001", sourceSimulationEventRecordId: cashReceiptEvent.Id),
                new FinanceTransaction(bankFeeTransactionId, companyId, cashAccountId, null, null, null, ScenarioAsOfUtc, "bank_fee", -25m, "USD", "Bank fee", "TX-CASH-BOUNDARY", sourceSimulationEventRecordId: bankFeeEvent.Id),
                new FinanceTransaction(Guid.NewGuid(), companyId, cashAccountId, null, null, null, new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), "future_cash", 100m, "USD", "Future cash receipt", "TX-CASH-FUTURE"),
                new FinanceTransaction(Guid.NewGuid(), otherCompanyId, otherCashAccountId, null, null, null, new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc), "other_cash", 500m, "USD", "Other company cash", "TX-CASH-OTHER"));

            dbContext.FinanceAssets.AddRange(
                new FinanceAsset(payableAssetId, companyId, supplierId, "AST-PAYABLE-001", "Server Cluster", "equipment", new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc), 900m, "USD", FinanceAssetFundingBehaviors.Payable, FinanceSettlementStatuses.Unpaid, FinanceAssetStatuses.Active, sourceSimulationEventRecordId: payableAssetEvent.Id),
                new FinanceAsset(cashAssetId, companyId, supplierId, "AST-CASH-001", "Design Workstations", "equipment", new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc), 250m, "USD", FinanceAssetFundingBehaviors.Cash, FinanceSettlementStatuses.Paid, FinanceAssetStatuses.Active, sourceSimulationEventRecordId: cashAssetEvent.Id),
                new FinanceAsset(Guid.NewGuid(), companyId, supplierId, "AST-FUTURE-001", "Future Equipment", "equipment", new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc), 400m, "USD", FinanceAssetFundingBehaviors.Payable, FinanceSettlementStatuses.Unpaid, FinanceAssetStatuses.Active),
                new FinanceAsset(Guid.NewGuid(), otherCompanyId, otherCounterpartyId, "AST-OTHER-001", "Other Company Asset", "equipment", new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc), 888m, "USD", FinanceAssetFundingBehaviors.Payable, FinanceSettlementStatuses.Unpaid, FinanceAssetStatuses.Active));

            return Task.CompletedTask;
        });

        return new FinanceSummarySeed(companyId, subject, email, displayName);
    }

    private static void AssertConsistencyMetrics(FinanceSummaryConsistencyResponse consistencyCheck)
    {
        Assert.Collection(
            consistencyCheck.Metrics.OrderBy(x => x.MetricKey, StringComparer.Ordinal),
            metric => AssertMetric(metric, "accounts_payable", 1555m),
            metric => AssertMetric(metric, "accounts_receivable", 1050m),
            metric => AssertMetric(metric, "current_cash", 1125m),
            metric => AssertMetric(metric, "monthly_costs", 1850m),
            metric => AssertMetric(metric, "monthly_revenue", 1300m),
            metric => AssertMetric(metric, "overdue_payables", 1180m),
            metric => AssertMetric(metric, "overdue_receivables", 450m),
            metric => AssertMetric(metric, "recent_asset_purchase_count", 2m),
            metric => AssertMetric(metric, "recent_asset_purchase_total_amount", 1150m));
    }

    private static void AssertMetric(FinanceSummaryConsistencyMetricResponse metric, string expectedKey, decimal expectedValue)
    {
        Assert.Equal(expectedKey, metric.MetricKey);
        Assert.Equal(expectedValue, metric.ExpectedValue);
        Assert.Equal(expectedValue, metric.ActualValue);
        Assert.True(metric.IsMatch);
    }

    private static void AssertSummary(FinanceSummaryResponse summary, Guid companyId)
    {
        Assert.Equal(companyId, summary.CompanyId);
        Assert.Equal(ScenarioAsOfUtc, summary.AsOfUtc);
        Assert.Equal(1125m, summary.CurrentCash);
        Assert.Equal(1050m, summary.AccountsReceivable);
        Assert.Equal(450m, summary.OverdueReceivables);
        Assert.Equal(1555m, summary.AccountsPayable);
        Assert.Equal(1180m, summary.OverduePayables);
        Assert.Equal(1300m, summary.MonthlyRevenue);
        Assert.Equal(1850m, summary.MonthlyCosts);
        Assert.Equal("USD", summary.Currency);
        Assert.True(summary.HasFinanceData);
        Assert.Equal(2, summary.RecentAssetPurchaseCount);
        Assert.Equal(1150m, summary.RecentAssetPurchaseTotalAmount);
        Assert.NotNull(summary.Intelligence);

        Assert.Equal(1125m, summary.Intelligence!.SevenDayProjection.StartingCash);
        Assert.Equal(1050m, summary.Intelligence.SevenDayProjection.ProjectedInflows);
        Assert.Equal(280m, summary.Intelligence.SevenDayProjection.ProjectedOutflows);
        Assert.Equal(1050m, summary.Intelligence.SevenDayProjection.InvoiceInflows);
        Assert.Equal(280m, summary.Intelligence.SevenDayProjection.BillOutflows);
        Assert.Equal(0m, summary.Intelligence.SevenDayProjection.RecurringOutflows);
        Assert.Equal(1895m, summary.Intelligence.SevenDayProjection.EndingCash);

        Assert.Equal(1125m, summary.Intelligence.ThirtyDayProjection.StartingCash);
        Assert.Equal(1050m, summary.Intelligence.ThirtyDayProjection.ProjectedInflows);
        Assert.Equal(655m, summary.Intelligence.ThirtyDayProjection.ProjectedOutflows);
        Assert.Equal(1050m, summary.Intelligence.ThirtyDayProjection.InvoiceInflows);
        Assert.Equal(655m, summary.Intelligence.ThirtyDayProjection.BillOutflows);
        Assert.Equal(0m, summary.Intelligence.ThirtyDayProjection.RecurringOutflows);
        Assert.Equal(1520m, summary.Intelligence.ThirtyDayProjection.EndingCash);

        Assert.Equal("healthy", summary.Intelligence.ObligationCoverage.Severity);
        Assert.Equal("coverage_healthy", summary.Intelligence.ObligationCoverage.RecommendationCode);
        Assert.Equal("Near-term obligations are covered for the next 7 days. Pay overdue bills now and keep collections moving.", summary.Intelligence.ObligationCoverage.RecommendationText);

        Assert.Equal(new[] { "INV-PRIOR-001", "INV-OVERDUE-001" }, summary.Intelligence.OverdueInvoices.Select(x => x.InvoiceNumber).ToArray());
        Assert.Equal(new[] { "17", "9" }, summary.Intelligence.OverdueInvoices.Select(x => x.OverdueDays.ToString()).ToArray());
        Assert.Equal(new[] { "1_30", "1_30" }, summary.Intelligence.OverdueInvoices.Select(x => x.AgingBucket).ToArray());
        Assert.Equal(new[] { "45", "45" }, summary.Intelligence.OverdueInvoices.Select(x => x.PaymentPatternScore.ToString()).ToArray());
        Assert.Equal(new[] { "follow_up", "follow_up" }, summary.Intelligence.OverdueInvoices.Select(x => x.RecommendationType).ToArray());
        Assert.All(summary.Intelligence.OverdueInvoices, x => Assert.True(x.PriorityScore > 0));
        Assert.All(summary.Intelligence.OverdueInvoices, x => Assert.False(string.IsNullOrWhiteSpace(x.ScoringFactors)));
        Assert.Equal(new[] { "BILL-OVERDUE-001", "BILL-PRIOR-001", "BILL-PART-001" }, summary.Intelligence.DueSoonBills.Select(x => x.BillNumber).ToArray());
        Assert.Equal(new[] { "pay_now", "pay_now", "delay" }, summary.Intelligence.DueSoonBills.Select(x => x.RecommendationAction).ToArray());
        Assert.All(summary.Intelligence.DueSoonBills, x => Assert.False(string.IsNullOrWhiteSpace(x.VendorCriticality)));
        Assert.All(summary.Intelligence.DueSoonBills, x => Assert.False(string.IsNullOrWhiteSpace(x.CashPressure)));
        Assert.All(summary.Intelligence.DueSoonBills, x => Assert.False(string.IsNullOrWhiteSpace(x.ScoringFactors)));
        Assert.Equal(
            "Call Northwind today about INV-PRIOR-001 and secure a payment commitment. It is 17 day(s) overdue and the balance is 150.00 USD.",
            summary.Intelligence.OverdueInvoices[0].RecommendationText);
        Assert.Equal(
            "Call Northwind today about INV-OVERDUE-001 and secure a payment commitment. It is 9 day(s) overdue and the balance is 300.00 USD.",
            summary.Intelligence.OverdueInvoices[1].RecommendationText);
        Assert.Equal(
            "Pay now. BILL-OVERDUE-001 is overdue and should be cleared before lower-priority cash uses.",
            summary.Intelligence.DueSoonBills[0].RecommendationText);
        Assert.Equal(
            "Delay. Preserve cash for higher-priority obligations and review BILL-PART-001 on 2026-04-28.",
            summary.Intelligence.DueSoonBills[2].RecommendationText);
        Assert.All(summary.RecentAssetPurchases, asset => Assert.Equal(companyId, asset.CompanyId));
        Assert.Collection(
            summary.RecentAssetPurchases,
            first =>
            {
                Assert.Equal("AST-PAYABLE-001", first.ReferenceNumber);
                Assert.Equal(new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc), first.PurchasedUtc);
                Assert.Equal(900m, first.Amount);
            },
            second =>
            {
                Assert.Equal("AST-CASH-001", second.ReferenceNumber);
                Assert.Equal(new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc), second.PurchasedUtc);
                Assert.Equal(250m, second.Amount);
            });
    }

    private HttpClient CreateAuthenticatedClient(
        string subject,
        string email,
        string displayName,
        Guid activeCompanyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, activeCompanyId.ToString());
        return client;
    }

    private sealed record FinanceSummarySeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed class FinanceSummaryResponse
    {
        public Guid CompanyId { get; set; }
        public DateTime AsOfUtc { get; set; }
        public decimal CurrentCash { get; set; }
        public decimal AccountsReceivable { get; set; }
        public decimal OverdueReceivables { get; set; }
        public decimal AccountsPayable { get; set; }
        public decimal OverduePayables { get; set; }
        public decimal MonthlyRevenue { get; set; }
        public decimal MonthlyCosts { get; set; }
        public string Currency { get; set; } = string.Empty;
        public bool HasFinanceData { get; set; }
        public int RecentAssetPurchaseCount { get; set; }
        public decimal RecentAssetPurchaseTotalAmount { get; set; }
        public FinanceSummaryConsistencyResponse? ConsistencyCheck { get; set; }
        public FinanceIntelligenceResponse? Intelligence { get; set; }
        public List<FinanceSummaryAssetPurchaseResponse> RecentAssetPurchases { get; set; } = [];
    }

    private sealed class FinanceSummaryAssetPurchaseResponse
    {
        public Guid AssetId { get; set; }
        public Guid CompanyId { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime PurchasedUtc { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string FundingBehavior { get; set; } = string.Empty;
        public string FundingSettlementStatus { get; set; } = string.Empty;
    }

    private sealed class FinanceSummaryConsistencyResponse
    {
        public Guid CompanyId { get; set; }
        public DateTime AsOfUtc { get; set; }
        public bool IsConsistent { get; set; }
        public int SourceRecordCount { get; set; }
        public List<FinanceSummaryConsistencyMetricResponse> Metrics { get; set; } = [];
    }

    private sealed class FinanceIntelligenceResponse
    {
        public DateTime AsOfUtc { get; set; }
        public FinanceProjectionResponse SevenDayProjection { get; set; } = new();
        public FinanceProjectionResponse ThirtyDayProjection { get; set; } = new();
        public FinanceObligationCoverageResponse ObligationCoverage { get; set; } = new();
        public List<FinanceOverdueInvoiceResponse> OverdueInvoices { get; set; } = [];
        public List<FinanceDueSoonBillResponse> DueSoonBills { get; set; } = [];
    }

    private sealed class FinanceProjectionResponse
    {
        public int HorizonDays { get; set; }
        public decimal StartingCash { get; set; }
        public decimal ProjectedInflows { get; set; }
        public decimal ProjectedOutflows { get; set; }
        public decimal InvoiceInflows { get; set; }
        public decimal BillOutflows { get; set; }
        public decimal RecurringOutflows { get; set; }
        public decimal EndingCash { get; set; }
    }

    private sealed class FinanceObligationCoverageResponse
    {
        public int HorizonDays { get; set; }
        public decimal AvailableCash { get; set; }
        public decimal NearTermObligations { get; set; }
        public decimal CoverageRatio { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string RecommendationCode { get; set; } = string.Empty;
        public string RecommendationText { get; set; } = string.Empty;
    }

    private sealed class FinanceOverdueInvoiceResponse
    {
        public int Rank { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public int OverdueDays { get; set; }
        public decimal OutstandingAmount { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string AgingBucket { get; set; } = string.Empty;
        public int PaymentPatternScore { get; set; }
        public string PaymentPatternSeverity { get; set; } = string.Empty;
        public string PaymentPatternConfidence { get; set; } = string.Empty;
        public string RecommendationCode { get; set; } = string.Empty;
        public string RecommendationText { get; set; } = string.Empty;
        public string RecommendationSeverity { get; set; } = string.Empty;
        public int PriorityScore { get; set; }
        public string RecommendationType { get; set; } = string.Empty;
        public string ScoringFactors { get; set; } = string.Empty;
    }

    private sealed class FinanceDueSoonBillResponse
    {
        public int Rank { get; set; }
        public string BillNumber { get; set; } = string.Empty;
        public int DaysUntilDue { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string CashImpact { get; set; } = string.Empty;
        public int UrgencyScore { get; set; }
        public string RecommendationCode { get; set; } = string.Empty;
        public string RecommendationText { get; set; } = string.Empty;
        public string RecommendationAction { get; set; } = string.Empty;
        public string RecommendationSeverity { get; set; } = string.Empty;
        public string CashImpactRationale { get; set; } = string.Empty;
        public string VendorCriticality { get; set; } = string.Empty;
        public string VendorCriticalityReason { get; set; } = string.Empty;
        public string CashPressure { get; set; } = string.Empty;
        public string CashPressureReason { get; set; } = string.Empty;
        public int DueDateFactor { get; set; }
        public int AmountFactor { get; set; }
        public int VendorCriticalityFactor { get; set; }
        public int CashPressureFactor { get; set; }
        public string ScoringFactors { get; set; } = string.Empty;
    }
}
END_OF_PATCH