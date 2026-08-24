using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceProgressionIntegrationTests
{
    private static readonly DateTime SimulationStartUtc = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int SimulationSeed = 73;
    private const int ProgressionDays = 30;
    private const string DeterministicConfigurationJson = """{"financeGeneration":{"anomalyCadenceDays":3,"anomalyOffsetDays":1}}""";

    [Fact]
    public async Task Simulation_progression_moves_invoices_and_bills_through_issue_payment_and_overdue_state()
    {
        using var factory = new TestWebApplicationFactory();
        var seed = await SeedSimulationCompanyAsync(factory, Guid.NewGuid(), "finance-progression-core");
        using var client = CreateAuthenticatedClient(factory, seed);

        await StartPausedSimulationAsync(client, seed.CompanyId);
        await StepForwardDaysAsync(client, seed.CompanyId, ProgressionDays);

        var snapshot = await CaptureProgressionSnapshotAsync(factory, seed.CompanyId);

        Assert.True(snapshot.CurrentSimulatedUtc > snapshot.OverdueInvoice.DueUtc);
        Assert.True(snapshot.CurrentSimulatedUtc > snapshot.OverdueBill.DueUtc);

        var beforeInvoiceIssue = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.InvoiceWithPayment.IssuedUtc.AddMinutes(-1));
        var afterInvoiceIssue = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.InvoiceWithPayment.IssuedUtc.AddMinutes(1));
        Assert.Equal(beforeInvoiceIssue.CurrentCash, afterInvoiceIssue.CurrentCash);
        Assert.True(
            beforeInvoiceIssue.AccountsReceivable + snapshot.InvoiceIssueNetReceivableDelta == afterInvoiceIssue.AccountsReceivable,
            $"Expected receivable delta {snapshot.InvoiceIssueNetReceivableDelta}; actual delta {afterInvoiceIssue.AccountsReceivable - beforeInvoiceIssue.AccountsReceivable}. {snapshot.InvoiceIssueDiagnostics}");

        var beforeInvoicePayment = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.InvoiceWithPayment.PaymentDate.AddMinutes(-1));
        var afterInvoicePayment = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.InvoiceWithPayment.PaymentDate.AddMinutes(1));
        Assert.Equal(beforeInvoicePayment.CurrentCash + snapshot.InvoicePaymentNetCashDelta, afterInvoicePayment.CurrentCash);
        Assert.Equal(beforeInvoicePayment.AccountsReceivable - snapshot.InvoiceWithPayment.AllocatedAmount, afterInvoicePayment.AccountsReceivable);

        var invoiceOverdueBoundaryUtc = snapshot.OverdueInvoice.IssuedUtc > snapshot.OverdueInvoice.DueUtc.Date.AddDays(1)
            ? snapshot.OverdueInvoice.IssuedUtc
            : snapshot.OverdueInvoice.DueUtc.Date.AddDays(1);
        var beforeInvoiceOverdue = await GetFinanceSummaryAsync(client, seed.CompanyId, invoiceOverdueBoundaryUtc.AddMinutes(-1));
        var afterInvoiceOverdue = await GetFinanceSummaryAsync(client, seed.CompanyId, invoiceOverdueBoundaryUtc.AddMinutes(1));
        Assert.Equal(beforeInvoiceOverdue.AccountsReceivable, afterInvoiceOverdue.AccountsReceivable);
        Assert.True(
            beforeInvoiceOverdue.OverdueReceivables + snapshot.OverdueInvoice.RemainingAmount == afterInvoiceOverdue.OverdueReceivables,
            $"Invoice {snapshot.OverdueInvoice.InvoiceNumber} due {snapshot.OverdueInvoice.DueUtc:O}; boundary {invoiceOverdueBoundaryUtc:O}; " +
            $"before as-of {beforeInvoiceOverdue.AsOfUtc:O} overdue {beforeInvoiceOverdue.OverdueReceivables}; " +
            $"after as-of {afterInvoiceOverdue.AsOfUtc:O} overdue {afterInvoiceOverdue.OverdueReceivables}; expected remaining {snapshot.OverdueInvoice.RemainingAmount}.");

        var beforeBillReceipt = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.BillWithPayment.ReceivedUtc.AddMinutes(-1));
        var afterBillReceipt = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.BillWithPayment.ReceivedUtc.AddMinutes(1));
        Assert.Equal(beforeBillReceipt.CurrentCash, afterBillReceipt.CurrentCash);
        Assert.Equal(beforeBillReceipt.AccountsPayable + snapshot.BillWithPayment.Amount, afterBillReceipt.AccountsPayable);

        var beforeBillPayment = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.BillWithPayment.PaymentDate.AddMinutes(-1));
        var afterBillPayment = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.BillWithPayment.PaymentDate.AddMinutes(1));
        Assert.Equal(beforeBillPayment.CurrentCash + snapshot.BillPaymentNetCashDelta, afterBillPayment.CurrentCash);
        Assert.Equal(beforeBillPayment.AccountsPayable - snapshot.BillWithPayment.AllocatedAmount, afterBillPayment.AccountsPayable);

        var billOverdueBoundaryUtc = snapshot.OverdueBill.ReceivedUtc > snapshot.OverdueBill.DueUtc.Date.AddDays(1)
            ? snapshot.OverdueBill.ReceivedUtc
            : snapshot.OverdueBill.DueUtc.Date.AddDays(1);
        var beforeBillOverdue = await GetFinanceSummaryAsync(client, seed.CompanyId, billOverdueBoundaryUtc.AddMinutes(-1));
        var afterBillOverdue = await GetFinanceSummaryAsync(client, seed.CompanyId, billOverdueBoundaryUtc.AddMinutes(1));
        Assert.Equal(beforeBillOverdue.AccountsPayable, afterBillOverdue.AccountsPayable);
        Assert.Equal(beforeBillOverdue.OverduePayables + snapshot.OverdueBill.RemainingAmount, afterBillOverdue.OverduePayables);
    }

    [Fact]
    public async Task Simulation_progression_applies_recurring_cost_and_asset_purchase_effects_to_summary()
    {
        using var factory = new TestWebApplicationFactory();
        var seed = await SeedSimulationCompanyAsync(factory, Guid.NewGuid(), "finance-progression-assets");
        using var client = CreateAuthenticatedClient(factory, seed);

        await StartPausedSimulationAsync(client, seed.CompanyId);
        var finalState = await StepForwardDaysAsync(client, seed.CompanyId, ProgressionDays);
        var snapshot = await CaptureProgressionSnapshotAsync(factory, seed.CompanyId);

        var latestRun = Assert.Single(finalState.RecentHistory ?? []);
        Assert.True(latestRun.DayLogs.Sum(x => x.RecurringExpenseInstancesGenerated) > 0);
        Assert.True(latestRun.DayLogs.Sum(x => x.AssetPurchasesGenerated) > 0);

        var beforeRecurringReceipt = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.RecurringBill.ReceivedUtc.AddMinutes(-1));
        var afterRecurringReceipt = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.RecurringBill.ReceivedUtc.AddMinutes(1));
        Assert.Equal(beforeRecurringReceipt.CurrentCash, afterRecurringReceipt.CurrentCash);
        Assert.True(
            beforeRecurringReceipt.AccountsPayable + snapshot.RecurringReceiptNetPayableDelta == afterRecurringReceipt.AccountsPayable,
            $"Expected payable delta {snapshot.RecurringReceiptNetPayableDelta}; actual delta {afterRecurringReceipt.AccountsPayable - beforeRecurringReceipt.AccountsPayable}. {snapshot.RecurringReceiptDiagnostics}");

        var beforePayableAsset = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.PayableAsset.PurchasedUtc.AddMinutes(-1));
        var afterPayableAsset = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.PayableAsset.PurchasedUtc.AddMinutes(1));
        Assert.Equal(beforePayableAsset.CurrentCash, afterPayableAsset.CurrentCash);
        Assert.Equal(beforePayableAsset.AccountsPayable + snapshot.PayableAsset.Amount, afterPayableAsset.AccountsPayable);
        Assert.Equal(beforePayableAsset.RecentAssetPurchaseCount + 1, afterPayableAsset.RecentAssetPurchaseCount);
        Assert.Contains(afterPayableAsset.RecentAssetPurchases, asset => asset.ReferenceNumber == snapshot.PayableAsset.ReferenceNumber);

        Assert.NotNull(snapshot.CashAsset.CashMovementUtc);
        var beforeCashAssetPayment = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.CashAsset.CashMovementUtc!.Value.AddMinutes(-1));
        var afterCashAssetPayment = await GetFinanceSummaryAsync(client, seed.CompanyId, snapshot.CashAsset.CashMovementUtc.Value.AddMinutes(1));
        Assert.Equal(beforeCashAssetPayment.AccountsPayable, afterCashAssetPayment.AccountsPayable);
        Assert.Equal(beforeCashAssetPayment.CurrentCash - snapshot.CashAsset.Amount, afterCashAssetPayment.CurrentCash);
        Assert.Contains(afterCashAssetPayment.RecentAssetPurchases, asset => asset.ReferenceNumber == snapshot.CashAsset.ReferenceNumber);
    }

    [Fact]
    public async Task Simulation_progression_exposes_identical_summary_projection_for_ui_agent_and_debug_routes()
    {
        using var factory = new TestWebApplicationFactory();
        var seed = await SeedSimulationCompanyAsync(factory, Guid.NewGuid(), "finance-progression-shared-summary");
        using var client = CreateAuthenticatedClient(factory, seed);

        await StartPausedSimulationAsync(client, seed.CompanyId);
        var finalState = await StepForwardDaysAsync(client, seed.CompanyId, ProgressionDays);

        var query = $"asOfUtc={Uri.EscapeDataString(finalState.CurrentSimulatedDateTime!.Value.ToString("O"))}&recentAssetPurchaseLimit=20&includeConsistencyCheck=true&source=simulation";

        var canonical = await GetFinanceSummaryFromRouteAsync(client, $"/api/companies/{seed.CompanyId:D}/finance-summary?{query}");
        var dashboard = await GetFinanceSummaryFromRouteAsync(client, $"/api/companies/{seed.CompanyId:D}/finance/dashboard/summary?{query}");
        var agent = await GetFinanceSummaryFromRouteAsync(client, $"/api/companies/{seed.CompanyId:D}/finance/agent-context/summary?{query}");
        var debug = await GetFinanceSummaryFromRouteAsync(client, $"/internal/companies/{seed.CompanyId:D}/finance/debug/summary?{query}");

        Assert.True(canonical.HasFinanceData);
        Assert.NotEmpty(canonical.RecentAssetPurchases);
        Assert.NotNull(canonical.ConsistencyCheck);
        Assert.True(
            canonical.ConsistencyCheck!.IsConsistent,
            string.Join(", ", canonical.ConsistencyCheck.Metrics.Where(metric => !metric.IsMatch).Select(metric => $"{metric.MetricKey}: expected {metric.ExpectedValue}, actual {metric.ActualValue}")));
        Assert.Equivalent(canonical, dashboard);
        Assert.Equivalent(canonical, agent);
        Assert.Equivalent(canonical, debug);
    }

    [Fact]
    public async Task Same_seed_profile_and_start_date_replay_produces_identical_finance_timeline_and_summary()
    {
        var companyId = Guid.Parse("0f1cb1e4-3259-4948-b11d-352f7ee9436f");

        using var firstFactory = new TestWebApplicationFactory();
        var firstSeed = await SeedSimulationCompanyAsync(firstFactory, companyId, "finance-replay-a");
        using var firstClient = CreateAuthenticatedClient(firstFactory, firstSeed);
        await StartPausedSimulationAsync(firstClient, firstSeed.CompanyId);
        var firstState = await StepForwardDaysAsync(firstClient, firstSeed.CompanyId, ProgressionDays);
        var firstSnapshot = await CaptureReplaySnapshotAsync(firstFactory, firstClient, firstSeed.CompanyId, firstState.CurrentSimulatedDateTime!.Value);

        using var secondFactory = new TestWebApplicationFactory();
        var secondSeed = await SeedSimulationCompanyAsync(secondFactory, companyId, "finance-replay-b");
        using var secondClient = CreateAuthenticatedClient(secondFactory, secondSeed);
        await StartPausedSimulationAsync(secondClient, secondSeed.CompanyId);
        var secondState = await StepForwardDaysAsync(secondClient, secondSeed.CompanyId, ProgressionDays);
        var secondSnapshot = await CaptureReplaySnapshotAsync(secondFactory, secondClient, secondSeed.CompanyId, secondState.CurrentSimulatedDateTime!.Value);

        AssertReplaySnapshotEqual(firstSnapshot, secondSnapshot);
        Assert.NotEmpty(firstSnapshot.Timeline);
        Assert.True(firstSnapshot.Summary.HasFinanceData);
        Assert.True(firstSnapshot.Summary.RecentAssetPurchaseCount > 0);
        Assert.All(firstSnapshot.Timeline, item => Assert.Equal(SimulationSeed, item.Seed));
        Assert.All(firstSnapshot.Timeline, item => Assert.Equal(SimulationStartUtc, item.StartSimulatedUtc));
        Assert.Contains(firstSnapshot.Timeline, item => string.Equals(item.EventType, "finance.invoice.generated", StringComparison.Ordinal));
        Assert.Contains(firstSnapshot.Timeline, item => string.Equals(item.EventType, "finance.bill.generated", StringComparison.Ordinal));
        Assert.Contains(firstSnapshot.Timeline, item => string.Equals(item.EventType, "finance.cash_movement.generated", StringComparison.Ordinal));
        Assert.Contains(firstSnapshot.Timeline, item => string.Equals(item.EventType, "finance.asset_purchase.generated", StringComparison.Ordinal));
    }

    private static async Task<SeededSimulationCompany> SeedSimulationCompanyAsync(
        TestWebApplicationFactory factory,
        Guid companyId,
        string subjectPrefix)
    {
        var userId = Guid.NewGuid();
        var subject = $"{subjectPrefix}-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Simulation Owner";

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));

            var company = new Company(companyId, $"Simulation Company {subjectPrefix}");
            company.UpdateWorkspaceProfile(company.Name, "Software", "SaaS", "UTC", "USD", "en", "US");

            dbContext.Companies.Add(company);
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));

            return Task.CompletedTask;
        });

        return new SeededSimulationCompany(companyId, subject, email, displayName);
    }

    private static HttpClient CreateAuthenticatedClient(TestWebApplicationFactory factory, SeededSimulationCompany seed)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, seed.DisplayName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyId.ToString());
        return client;
    }

    private static async Task StartPausedSimulationAsync(HttpClient client, Guid companyId)
    {
        var startResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{companyId:D}/simulation/start",
            new StartSimulationRequest(
                SimulationStartUtc,
                GenerationEnabled: true,
                Seed: SimulationSeed,
                DeterministicConfigurationJson: DeterministicConfigurationJson));

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        var pauseResponse = await client.PostAsync($"/internal/companies/{companyId:D}/simulation/pause", content: null);
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
    }

    private static async Task<CompanySimulationStateDto> StepForwardDaysAsync(HttpClient client, Guid companyId, int days)
    {
        CompanySimulationStateDto? state = null;
        for (var index = 0; index < days; index++)
        {
            var response = await client.PostAsync($"/internal/companies/{companyId:D}/simulation/step-forward", content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            state = await response.Content.ReadFromJsonAsync<CompanySimulationStateDto>();
            Assert.NotNull(state);
        }

        return state!;
    }

    private static async Task<FinanceSummaryResponse> GetFinanceSummaryAsync(HttpClient client, Guid companyId, DateTime asOfUtc)
    {
        var normalizedAsOfUtc = asOfUtc.Kind == DateTimeKind.Utc
            ? asOfUtc
            : DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var response = await client.GetAsync(
            $"/api/companies/{companyId:D}/finance-summary?asOfUtc={Uri.EscapeDataString(normalizedAsOfUtc.ToString("O"))}&recentAssetPurchaseLimit=20&source=simulation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<FinanceSummaryResponse>();
        Assert.NotNull(summary);
        return summary!;
    }

    private static async Task<FinanceSummaryResponse> GetFinanceSummaryFromRouteAsync(HttpClient client, string requestUri)
    {
        var response = await client.GetAsync(requestUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<FinanceSummaryResponse>();
        Assert.NotNull(summary);

        return summary!;
    }

    private static async Task<FinanceProgressionSnapshot> CaptureProgressionSnapshotAsync(TestWebApplicationFactory factory, Guid companyId)
    {
        return await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var currentSimulatedUtc = await dbContext.CompanySimulationStates
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .Select(x => x.CurrentSimulatedUtc)
                .SingleAsync();

            var invoices = await dbContext.FinanceInvoices
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId &&
                    EF.Property<string>(x, "SourceType") == FinanceRecordSourceTypes.Simulation)
                .OrderBy(x => x.IssuedUtc)
                .ToListAsync();
            var bills = await dbContext.FinanceBills
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId &&
                    EF.Property<string>(x, "SourceType") == FinanceRecordSourceTypes.Simulation)
                .OrderBy(x => x.ReceivedUtc)
                .ToListAsync();
            var payments = await dbContext.Payments
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.SourceSimulationEventRecordId != null)
                .ToListAsync();
            var allocations = await dbContext.PaymentAllocations
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Payment.SourceSimulationEventRecordId != null)
                .ToListAsync();
            var assets = await dbContext.FinanceAssets
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId &&
                    EF.Property<string>(x, "SourceType") == FinanceRecordSourceTypes.Simulation)
                .OrderBy(x => x.PurchasedUtc)
                .ToListAsync();
            var transactions = await dbContext.FinanceTransactions
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId &&
                    EF.Property<string>(x, "SourceType") == FinanceRecordSourceTypes.Simulation)
                .OrderBy(x => x.TransactionUtc)
                .ToListAsync();

            var completedIncomingPayments = payments
                .Where(x =>
                    x.PaymentType == PaymentTypes.Incoming &&
                    string.Equals(x.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.Id);
            var completedOutgoingPayments = payments
                .Where(x =>
                    x.PaymentType == PaymentTypes.Outgoing &&
                    string.Equals(x.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.Id);

            var invoiceWithPayment = invoices
                .Select(invoice =>
                {
                    var allocation = allocations
                        .Where(x => x.InvoiceId == invoice.Id && completedIncomingPayments.ContainsKey(x.PaymentId))
                        .OrderBy(x => completedIncomingPayments[x.PaymentId].PaymentDate)
                        .FirstOrDefault();

                    if (allocation is null)
                    {
                        return null;
                    }

                    var payment = completedIncomingPayments[allocation.PaymentId];
                    return new InvoiceProgressionFlow(
                        invoice.InvoiceNumber,
                        invoice.IssuedUtc,
                        invoice.DueUtc,
                        invoice.Amount,
                        payment.PaymentDate,
                        payment.Amount,
                        allocation.AllocatedAmount);
                })
                .OfType<InvoiceProgressionFlow>()
                .Where(flow => !completedIncomingPayments.Values.Any(payment =>
                    Math.Abs((payment.PaymentDate - flow.IssuedUtc).TotalMinutes) <= 1d))
                .FirstOrDefault();

            var overdueInvoice = invoices
                .Where(invoice => IsIncludedReceivable(invoice.Status, invoice.SettlementStatus))
                .Where(invoice => invoice.IssuedUtc < invoice.DueUtc.Date.AddDays(1))
                .Select(invoice =>
                {
                    var allocatedAmount = allocations
                        .Where(x => x.InvoiceId == invoice.Id && completedIncomingPayments.ContainsKey(x.PaymentId))
                        .Sum(x => x.AllocatedAmount);
                    var remainingAmount = decimal.Round(Math.Max(0m, invoice.Amount - allocatedAmount), 2, MidpointRounding.AwayFromZero);

                    return remainingAmount <= 0m || invoice.DueUtc >= currentSimulatedUtc
                        ? null
                        : new OverdueInvoiceFlow(invoice.InvoiceNumber, invoice.IssuedUtc, invoice.DueUtc, remainingAmount);
                })
                .OfType<OverdueInvoiceFlow>()
                .OrderBy(x => x.DueUtc)
                .FirstOrDefault();

            var billWithPayment = bills
                .Select(bill =>
                {
                    var allocation = allocations
                        .Where(x => x.BillId == bill.Id && completedOutgoingPayments.ContainsKey(x.PaymentId))
                        .OrderBy(x => completedOutgoingPayments[x.PaymentId].PaymentDate)
                        .FirstOrDefault();

                    if (allocation is null)
                    {
                        return null;
                    }

                    var payment = completedOutgoingPayments[allocation.PaymentId];
                    return new BillProgressionFlow(
                        bill.BillNumber,
                        bill.ReceivedUtc,
                        bill.DueUtc,
                        bill.Amount,
                        payment.PaymentDate,
                        payment.Amount,
                        allocation.AllocatedAmount);
                })
                .OfType<BillProgressionFlow>()
                .FirstOrDefault();

            var overdueBill = bills
                .Where(bill => IsIncludedPayable(bill.Status, bill.SettlementStatus))
                .Where(bill => bill.ReceivedUtc < bill.DueUtc.Date.AddDays(1))
                .Select(bill =>
                {
                    var allocatedAmount = allocations
                        .Where(x => x.BillId == bill.Id && completedOutgoingPayments.ContainsKey(x.PaymentId))
                        .Sum(x => x.AllocatedAmount);
                    var remainingAmount = decimal.Round(Math.Max(0m, bill.Amount - allocatedAmount), 2, MidpointRounding.AwayFromZero);

                    return remainingAmount <= 0m || bill.DueUtc >= currentSimulatedUtc
                        ? null
                        : new OverdueBillFlow(bill.BillNumber, bill.ReceivedUtc, bill.DueUtc, remainingAmount);
                })
                .OfType<OverdueBillFlow>()
                .OrderBy(x => x.DueUtc)
                .FirstOrDefault();

            var recurringBill = bills
                .Where(bill => bill.BillNumber != billWithPayment?.BillNumber)
                .Where(bill => bill.BillNumber != overdueBill?.BillNumber)
                .Where(bill => !string.Equals(bill.Status, "paid", StringComparison.OrdinalIgnoreCase))
                .Where(bill => !string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                .Where(bill => !completedOutgoingPayments.Values.Any(payment =>
                    Math.Abs((payment.PaymentDate - bill.ReceivedUtc).TotalMinutes) <= 1d))
                .Select(bill => new RecurringCostFlow(bill.BillNumber, bill.ReceivedUtc, bill.Amount))
                .FirstOrDefault();

            var payableAsset = assets
                .Where(x =>
                    string.Equals(x.FundingBehavior, FinanceAssetFundingBehaviors.Payable, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.FundingSettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Status, FinanceAssetStatuses.Active, StringComparison.OrdinalIgnoreCase))
                .Select(x => new AssetProgressionFlow(x.ReferenceNumber, x.PurchasedUtc, x.Amount, x.FundingBehavior, x.FundingSettlementStatus, null))
                .FirstOrDefault();

            var cashAssetEntity = assets
                .FirstOrDefault(x => string.Equals(x.FundingBehavior, FinanceAssetFundingBehaviors.Cash, StringComparison.OrdinalIgnoreCase));
            var cashAssetPayment = cashAssetEntity is null
                ? null
                : transactions.FirstOrDefault(x => string.Equals(x.ExternalReference, $"{cashAssetEntity.ReferenceNumber}-PAY", StringComparison.Ordinal));
            var cashAsset = cashAssetEntity is null
                ? null
                : new AssetProgressionFlow(
                    cashAssetEntity.ReferenceNumber,
                    cashAssetEntity.PurchasedUtc,
                    cashAssetEntity.Amount,
                    cashAssetEntity.FundingBehavior,
                    cashAssetEntity.FundingSettlementStatus,
                    cashAssetPayment?.TransactionUtc);

            var invoiceIssueWindowStart = invoiceWithPayment!.IssuedUtc.AddMinutes(-1);
            var invoiceIssueWindowEnd = invoiceWithPayment.IssuedUtc.AddMinutes(1);
            var invoiceIssueNetReceivableDelta = invoices
                .Where(x => x.IssuedUtc > invoiceIssueWindowStart && x.IssuedUtc <= invoiceIssueWindowEnd)
                .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus))
                .Sum(x => x.Amount) - allocations
                .Where(x => completedIncomingPayments.TryGetValue(x.PaymentId, out var payment) &&
                    payment.PaymentDate > invoiceIssueWindowStart && payment.PaymentDate <= invoiceIssueWindowEnd)
                .Sum(x => x.AllocatedAmount);
            var invoiceIssueDiagnostics = string.Join(" | ", invoices
                .Where(x => x.IssuedUtc > invoiceIssueWindowStart && x.IssuedUtc <= invoiceIssueWindowEnd)
                .Select(x => $"invoice={x.InvoiceNumber},amount={x.Amount},status={x.Status},settlement={x.SettlementStatus}"));

            var recurringReceiptWindowStart = recurringBill!.ReceivedUtc.AddMinutes(-1);
            var recurringReceiptWindowEnd = recurringBill.ReceivedUtc.AddMinutes(1);
            var recurringReceiptNetPayableDelta = bills
                .Where(x => x.ReceivedUtc > recurringReceiptWindowStart && x.ReceivedUtc <= recurringReceiptWindowEnd)
                .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus))
                .Sum(x => x.Amount) + assets
                .Where(x => x.PurchasedUtc > recurringReceiptWindowStart && x.PurchasedUtc <= recurringReceiptWindowEnd &&
                    string.Equals(x.FundingBehavior, FinanceAssetFundingBehaviors.Payable, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.FundingSettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Status, FinanceAssetStatuses.Active, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount) - allocations
                .Where(x => completedOutgoingPayments.TryGetValue(x.PaymentId, out var payment) &&
                    payment.PaymentDate > recurringReceiptWindowStart && payment.PaymentDate <= recurringReceiptWindowEnd)
                .Sum(x => x.AllocatedAmount);
            var recurringReceiptDiagnostics = string.Join(" | ", bills
                .Where(x => x.ReceivedUtc > recurringReceiptWindowStart && x.ReceivedUtc <= recurringReceiptWindowEnd)
                .Select(x => $"bill={x.BillNumber},amount={x.Amount},status={x.Status},settlement={x.SettlementStatus}")
                .Concat(assets
                    .Where(x => x.PurchasedUtc > recurringReceiptWindowStart && x.PurchasedUtc <= recurringReceiptWindowEnd)
                    .Select(x => $"asset={x.ReferenceNumber},amount={x.Amount},status={x.Status},funding={x.FundingBehavior},settlement={x.FundingSettlementStatus}")));

            var invoicePaymentWindowStart = invoiceWithPayment.PaymentDate.AddMinutes(-1);
            var invoicePaymentWindowEnd = invoiceWithPayment.PaymentDate.AddMinutes(1);
            var invoicePaymentNetCashDelta = transactions
                .Where(x => x.TransactionUtc > invoicePaymentWindowStart && x.TransactionUtc <= invoicePaymentWindowEnd)
                .Sum(x => x.Amount);
            var billPaymentWindowStart = billWithPayment!.PaymentDate.AddMinutes(-1);
            var billPaymentWindowEnd = billWithPayment.PaymentDate.AddMinutes(1);
            var billPaymentNetCashDelta = transactions
                .Where(x => x.TransactionUtc > billPaymentWindowStart && x.TransactionUtc <= billPaymentWindowEnd)
                .Sum(x => x.Amount);

            return new FinanceProgressionSnapshot(
                currentSimulatedUtc,
                invoiceWithPayment ?? throw new InvalidOperationException("Expected a generated invoice with a completed incoming payment allocation."),
                overdueInvoice ?? throw new InvalidOperationException("Expected an unpaid invoice to become overdue after deterministic progression."),
                billWithPayment ?? throw new InvalidOperationException("Expected a generated bill with a completed outgoing payment allocation."),
                overdueBill ?? throw new InvalidOperationException("Expected an unpaid bill to become overdue after deterministic progression."),
                recurringBill ?? throw new InvalidOperationException("Expected deterministic progression to generate a recurring bill."),
                payableAsset ?? throw new InvalidOperationException("Expected deterministic progression to generate a payable-funded asset purchase."),
                cashAsset ?? throw new InvalidOperationException("Expected deterministic progression to generate a cash-funded asset purchase."),
                invoiceIssueNetReceivableDelta,
                recurringReceiptNetPayableDelta,
                invoicePaymentNetCashDelta,
                billPaymentNetCashDelta,
                invoiceIssueDiagnostics,
                recurringReceiptDiagnostics);
        });
    }

    private static async Task<ReplaySnapshot> CaptureReplaySnapshotAsync(
        TestWebApplicationFactory factory,
        HttpClient client,
        Guid companyId,
        DateTime asOfUtc)
    {
        var summary = await GetFinanceSummaryAsync(client, companyId, asOfUtc);
        var timeline = await factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.SimulationEventRecords
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.SimulationDateUtc)
                .ThenBy(x => x.SequenceNumber)
                .ThenBy(x => x.Id)
                .Select(x => new SimulationEventSnapshot(
                    x.Id,
                    x.Seed,
                    x.StartSimulatedUtc,
                    x.SimulationDateUtc,
                    x.EventType,
                    x.SourceEntityType,
                    x.SourceEntityId,
                    x.SourceReference,
                    x.ParentEventId,
                    x.SequenceNumber,
                    x.DeterministicKey,
                    x.CashBefore,
                    x.CashDelta,
                    x.CashAfter))
                .ToListAsync());

        return new ReplaySnapshot(
            new FinanceSummarySnapshot(
                summary.CompanyId,
                summary.AsOfUtc,
                summary.CurrentCash,
                summary.AccountsReceivable,
                summary.OverdueReceivables,
                summary.AccountsPayable,
                summary.OverduePayables,
                summary.MonthlyRevenue,
                summary.MonthlyCosts,
                summary.Currency,
                summary.HasFinanceData,
                summary.RecentAssetPurchaseCount,
                summary.RecentAssetPurchaseTotalAmount,
                summary.RecentAssetPurchases
                    .Select(x => new FinanceSummaryAssetPurchaseSnapshot(
                        x.AssetId,
                        x.CompanyId,
                        x.ReferenceNumber,
                        x.Name,
                        x.Category,
                        x.PurchasedUtc,
                        x.Amount,
                        x.Currency,
                        x.FundingBehavior,
                        x.FundingSettlementStatus))
                    .ToList()),
            timeline);
    }

    private static bool IsIncludedReceivable(string status, string settlementStatus) =>
        !string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Credited, StringComparison.Ordinal) &&
        NormalizeStatus(status) is not ("cancelled" or "canceled" or "void" or "voided" or "written_off" or "rejected");

    private static bool IsIncludedPayable(string status, string settlementStatus) =>
        !string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Credited, StringComparison.Ordinal) &&
        NormalizeStatus(status) is not ("cancelled" or "canceled" or "void" or "voided");

    private static string NormalizeStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    private static void AssertReplaySnapshotEqual(ReplaySnapshot expected, ReplaySnapshot actual)
    {
        Assert.Equal(expected.Summary.CompanyId, actual.Summary.CompanyId);
        Assert.Equal(expected.Summary.AsOfUtc, actual.Summary.AsOfUtc);
        Assert.Equal(expected.Summary.CurrentCash, actual.Summary.CurrentCash);
        Assert.Equal(expected.Summary.AccountsReceivable, actual.Summary.AccountsReceivable);
        Assert.Equal(expected.Summary.OverdueReceivables, actual.Summary.OverdueReceivables);
        Assert.Equal(expected.Summary.AccountsPayable, actual.Summary.AccountsPayable);
        Assert.Equal(expected.Summary.OverduePayables, actual.Summary.OverduePayables);
        Assert.Equal(expected.Summary.MonthlyRevenue, actual.Summary.MonthlyRevenue);
        Assert.Equal(expected.Summary.MonthlyCosts, actual.Summary.MonthlyCosts);
        Assert.Equal(expected.Summary.Currency, actual.Summary.Currency);
        Assert.Equal(expected.Summary.HasFinanceData, actual.Summary.HasFinanceData);
        Assert.Equal(expected.Summary.RecentAssetPurchaseCount, actual.Summary.RecentAssetPurchaseCount);
        Assert.Equal(expected.Summary.RecentAssetPurchaseTotalAmount, actual.Summary.RecentAssetPurchaseTotalAmount);
        Assert.Equal(expected.Summary.RecentAssetPurchases, actual.Summary.RecentAssetPurchases);

        // Ordered comparisons catch replay drift caused by unstable event sequencing.
        Assert.Equal(expected.Timeline, actual.Timeline);
    }

    private sealed record SeededSimulationCompany(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed record StartSimulationRequest(
        DateTime StartSimulatedDateTime,
        bool GenerationEnabled,
        int Seed,
        string? DeterministicConfigurationJson = null);

    private sealed record InvoiceProgressionFlow(
        string InvoiceNumber,
        DateTime IssuedUtc,
        DateTime DueUtc,
        decimal Amount,
        DateTime PaymentDate,
        decimal PaymentAmount,
        decimal AllocatedAmount);

    private sealed record OverdueInvoiceFlow(
        string InvoiceNumber,
        DateTime IssuedUtc,
        DateTime DueUtc,
        decimal RemainingAmount);

    private sealed record BillProgressionFlow(
        string BillNumber,
        DateTime ReceivedUtc,
        DateTime DueUtc,
        decimal Amount,
        DateTime PaymentDate,
        decimal PaymentAmount,
        decimal AllocatedAmount);

    private sealed record OverdueBillFlow(
        string BillNumber,
        DateTime ReceivedUtc,
        DateTime DueUtc,
        decimal RemainingAmount);

    private sealed record RecurringCostFlow(
        string BillNumber,
        DateTime ReceivedUtc,
        decimal Amount);

    private sealed record AssetProgressionFlow(
        string ReferenceNumber,
        DateTime PurchasedUtc,
        decimal Amount,
        string FundingBehavior,
        string FundingSettlementStatus,
        DateTime? CashMovementUtc);

    private sealed record FinanceProgressionSnapshot(
        DateTime CurrentSimulatedUtc,
        InvoiceProgressionFlow InvoiceWithPayment,
        OverdueInvoiceFlow OverdueInvoice,
        BillProgressionFlow BillWithPayment,
        OverdueBillFlow OverdueBill,
        RecurringCostFlow RecurringBill,
        AssetProgressionFlow PayableAsset,
        AssetProgressionFlow CashAsset,
        decimal InvoiceIssueNetReceivableDelta,
        decimal RecurringReceiptNetPayableDelta,
        decimal InvoicePaymentNetCashDelta,
        decimal BillPaymentNetCashDelta,
        string InvoiceIssueDiagnostics,
        string RecurringReceiptDiagnostics);

    private sealed record ReplaySnapshot(
        FinanceSummarySnapshot Summary,
        IReadOnlyList<SimulationEventSnapshot> Timeline);

    private sealed record FinanceSummarySnapshot(
        Guid CompanyId,
        DateTime AsOfUtc,
        decimal CurrentCash,
        decimal AccountsReceivable,
        decimal OverdueReceivables,
        decimal AccountsPayable,
        decimal OverduePayables,
        decimal MonthlyRevenue,
        decimal MonthlyCosts,
        string Currency,
        bool HasFinanceData,
        int RecentAssetPurchaseCount,
        decimal RecentAssetPurchaseTotalAmount,
        IReadOnlyList<FinanceSummaryAssetPurchaseSnapshot> RecentAssetPurchases);

    private sealed record FinanceSummaryAssetPurchaseSnapshot(
        Guid AssetId,
        Guid CompanyId,
        string ReferenceNumber,
        string Name,
        string Category,
        DateTime PurchasedUtc,
        decimal Amount,
        string Currency,
        string FundingBehavior,
        string FundingSettlementStatus);

    private sealed record SimulationEventSnapshot(
        Guid EventId,
        int Seed,
        DateTime StartSimulatedUtc,
        DateTime SimulationDateUtc,
        string EventType,
        string SourceEntityType,
        Guid? SourceEntityId,
        string? SourceReference,
        Guid? ParentEventId,
        int SequenceNumber,
        string DeterministicKey,
        decimal? CashBefore,
        decimal? CashDelta,
        decimal? CashAfter);

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
        public List<FinanceSummaryAssetPurchaseResponse> RecentAssetPurchases { get; set; } = [];
        public FinanceSummaryConsistencyResponse? ConsistencyCheck { get; set; }
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

    private sealed class FinanceSummaryConsistencyMetricResponse
    {
        public string MetricKey { get; set; } = string.Empty;
        public decimal ExpectedValue { get; set; }
        public decimal ActualValue { get; set; }
        public bool IsMatch { get; set; }
    }
}
