using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanySimulationFinanceGenerationTests
{
    [Fact]
    public async Task Progression_generates_real_finance_records_approvals_audits_and_periodic_anomalies()
    {
        var companyId = Guid.NewGuid();
        var provider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Deterministic Finance Company"));
        dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(
            Guid.NewGuid(),
            companyId,
            "USD",
            1000m,
            500m,
            true,
            -2000m,
            2000m,
            60,
            20));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var generationService = new CompanySimulationFinanceGenerationService(
            dbContext,
            provider,
            NullLogger<CompanySimulationFinanceGenerationService>.Instance);
        var stateService = new CompanySimulationStateService(
            repository,
            provider,
            companyContextAccessor: null,
            distributedLockProvider: null,
            financeGenerationPolicy: generationService,
            logger: NullLogger<CompanySimulationStateService>.Instance);

        await stateService.StartAsync(
            new StartCompanySimulationCommand(
                companyId,
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                true,
                73,
                """{"financeGeneration":{"anomalyCadenceDays":2,"anomalyOffsetDays":0}}"""),
            CancellationToken.None);

        provider.Advance(TimeSpan.FromSeconds(160));
        var state = await stateService.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        var invoices = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.InvoiceNumber)
            .ToListAsync();
        var bills = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.BillNumber)
            .ToListAsync();
        var transactions = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ExternalReference)
            .ToListAsync();
        var balances = await dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync();
        var assets = await dbContext.FinanceAssets
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ReferenceNumber)
            .ToListAsync();
        var tasks = await dbContext.WorkTasks
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CorrelationId)
            .ToListAsync();
        var approvals = await dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Include(x => x.Steps)
            .ToListAsync();
        var audits = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Action.StartsWith("finance.", StringComparison.OrdinalIgnoreCase))
            .ToListAsync();
        var activity = await dbContext.ActivityEvents
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.EventType == "finance.simulation.day_generated")
            .ToListAsync();
        var alerts = await dbContext.Alerts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Type == AlertType.Anomaly)
            .OrderBy(x => x.Fingerprint)
            .ToListAsync();
        var seedAnomalies = await dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Type == AlertType.Anomaly)
            .OrderBy(x => x.Fingerprint)
            .ToListAsync();

        Assert.Equal(new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc), state.CurrentSimulatedDateTime);

        var readService = new CompanyFinanceReadService(dbContext);
        var anomalyWorkbench = await readService.GetAnomalyWorkbenchAsync(
            new GetFinanceAnomalyWorkbenchQuery(companyId, Page: 1, PageSize: 25),
            CancellationToken.None);
        var firstAnomalyTransactionId = alerts
            .Select(x => x.Evidence["transactionId"]?.GetValue<Guid>() ?? Guid.Empty)
            .First(x => x != Guid.Empty);
        var anomalyDetail = await readService.GetAnomalyDetailAsync(new GetFinanceAnomalyDetailQuery(companyId, alerts[0].Id), CancellationToken.None);
        var transactionDetail = await readService.GetTransactionDetailAsync(new GetFinanceTransactionDetailQuery(companyId, firstAnomalyTransactionId), CancellationToken.None);

        Assert.Equal(16, invoices.Count);
        Assert.Equal(16, bills.Count);
        Assert.True(transactions.Count >= 12);
        Assert.Equal(16, balances.Count);
        Assert.True(tasks.Count >= 24);
        Assert.True(approvals.Count >= 6);
        Assert.True(audits.Count >= 40);
        Assert.Equal(16, activity.Count);
        Assert.True(assets.Count >= 4);
        Assert.Contains(assets, x => x.FundingBehavior == FinanceAssetFundingBehaviors.Cash);
        Assert.Contains(assets, x => x.FundingBehavior == FinanceAssetFundingBehaviors.Payable);
        Assert.All(assets, x => Assert.Equal(FinanceAssetStatuses.Active, x.Status));
        Assert.Equal(8, seedAnomalies.Count);
        Assert.Equal(8, alerts.Count);

        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-PTH", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-FX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-PART", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-FULL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-OVER", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-SOON", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-LATE", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invoices, x => x.InvoiceNumber.EndsWith("-LOW", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(invoices, x => x.Amount < 1000m);
        Assert.Contains(invoices, x => x.Amount == 1000m);
        Assert.Contains(invoices, x => x.Amount > 1000m);
        Assert.Contains(invoices, x => x.Currency == "EUR");
        Assert.Contains(invoices, x => x.Status == "paid");
        Assert.Contains(invoices, x => x.Status == "approved");
        Assert.Contains(invoices, x => x.Status == "pending");
        Assert.Contains(invoices, x => x.Status == "pending_approval");

        Assert.Contains(approvals, x => x.ApprovalType == "invoice_review");
        Assert.Contains(approvals, x => x.ApprovalType == "bill_review");
        Assert.All(approvals, approval =>
        {
            Assert.Equal("task", approval.TargetEntityType);
            Assert.NotEmpty(approval.Steps);
            Assert.Equal(ApprovalRequestStatus.Pending, approval.Status);
        });

        Assert.Contains(alerts, x => x.Metadata["anomalyType"]?.GetValue<string>() == "duplicate_vendor_charge");
        Assert.Contains(alerts, x => x.Metadata["anomalyType"]?.GetValue<string>() == "unusually_high_amount");
        Assert.Contains(alerts, x => x.Metadata["anomalyType"]?.GetValue<string>() == "category_mismatch");
        Assert.Contains(alerts, x => x.Metadata["anomalyType"]?.GetValue<string>() == "missing_document");
        Assert.Contains(alerts, x => x.Metadata["anomalyType"]?.GetValue<string>() == "suspicious_payment_timing");
        Assert.Contains(alerts, x => x.Metadata["anomalyType"]?.GetValue<string>() == "multiple_payments");
        Assert.Contains(alerts, x => x.Metadata["anomalyType"]?.GetValue<string>() == "payment_before_expected_state_transition");

        Assert.Contains(seedAnomalies, x => x.AnomalyType == "duplicate_vendor_charge");
        Assert.Contains(seedAnomalies, x => x.AnomalyType == "unusually_high_amount");
        Assert.Contains(seedAnomalies, x => x.AnomalyType == "category_mismatch");
        Assert.Contains(seedAnomalies, x => x.AnomalyType == "missing_document");
        Assert.Contains(seedAnomalies, x => x.AnomalyType == "suspicious_payment_timing");
        Assert.Contains(seedAnomalies, x => x.AnomalyType == "multiple_payments");
        Assert.Contains(seedAnomalies, x => x.AnomalyType == "payment_before_expected_state_transition");

        Assert.Equal(8, anomalyWorkbench.TotalCount);
        Assert.Contains(anomalyWorkbench.Items, x => x.AnomalyType == "duplicate_vendor_charge");
        Assert.NotNull(anomalyDetail);
        Assert.NotNull(transactionDetail);
        Assert.True(transactionDetail!.IsFlagged);
        Assert.NotEqual("clear", transactionDetail.AnomalyState);

        Assert.Contains(tasks, x => x.Type == "invoice_approval_review");
        Assert.Contains(tasks, x => x.Type == "bill_approval_review");
        Assert.Contains(tasks, x => x.Type == "finance_transaction_anomaly_follow_up");
        Assert.Contains(tasks, x => x.Status == WorkTaskStatus.AwaitingApproval);
    }

    [Fact]
    public async Task Same_seed_and_dates_produce_the_same_logical_records_and_reruns_are_idempotent()
    {
        var firstSnapshot = await RunSnapshotAsync(73, """{"financeGeneration":{"anomalyCadenceDays":2}}""");
        var secondSnapshot = await RunSnapshotAsync(73, """{"financeGeneration":{"anomalyCadenceDays":2}}""");

        Assert.Equal(firstSnapshot.CurrentSimulatedDateTime, secondSnapshot.CurrentSimulatedDateTime);
        Assert.Equal(firstSnapshot.InvoiceNumbers, secondSnapshot.InvoiceNumbers);
        Assert.Equal(firstSnapshot.BillNumbers, secondSnapshot.BillNumbers);
        Assert.Equal(firstSnapshot.TransactionReferences, secondSnapshot.TransactionReferences);
        Assert.Equal(firstSnapshot.TaskCorrelations, secondSnapshot.TaskCorrelations);
        Assert.Equal(firstSnapshot.ApprovalTargets, secondSnapshot.ApprovalTargets);
        Assert.Equal(firstSnapshot.SeedAnomalyTypes, secondSnapshot.SeedAnomalyTypes);
        Assert.Equal(firstSnapshot.WorkbenchSnapshot.Items.Select(x => x.AnomalyType), secondSnapshot.WorkbenchSnapshot.Items.Select(x => x.AnomalyType));
        Assert.Equal(firstSnapshot.AlertFingerprints, secondSnapshot.AlertFingerprints);
        Assert.Equal(firstSnapshot.AuditKeys, secondSnapshot.AuditKeys);
    }

    private static async Task<FinanceSnapshot> RunSnapshotAsync(int seed, string configurationJson)
    {
        var companyId = Guid.NewGuid();
        var provider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Replay Company"));
        dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(
            Guid.NewGuid(),
            companyId,
            "USD",
            1000m,
            500m,
            true,
            -2000m,
            2000m,
            60,
            20));
        await dbContext.SaveChangesAsync();

        var repository = new EfCompanySimulationStateRepository(dbContext);
        var generationService = new CompanySimulationFinanceGenerationService(
            dbContext,
            provider,
            NullLogger<CompanySimulationFinanceGenerationService>.Instance);
        var stateService = new CompanySimulationStateService(
            repository,
            provider,
            companyContextAccessor: null,
            distributedLockProvider: null,
            financeGenerationPolicy: generationService,
            logger: NullLogger<CompanySimulationStateService>.Instance);

        await stateService.StartAsync(
            new StartCompanySimulationCommand(
                companyId,
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                true,
                seed,
                configurationJson),
            CancellationToken.None);

        provider.Advance(TimeSpan.FromSeconds(100));
        var state = await stateService.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        var countsBefore = await CaptureCountsAsync(dbContext, companyId);
        var readService = new CompanyFinanceReadService(dbContext);
        await readService.GetAnomalyWorkbenchAsync(new GetFinanceAnomalyWorkbenchQuery(companyId, Page: 1, PageSize: 25), CancellationToken.None);
        var countsAfter = await CaptureCountsAsync(dbContext, companyId);

        Assert.Equal(countsBefore, countsAfter);
        return new FinanceSnapshot(
            state.CurrentSimulatedDateTime,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).OrderBy(x => x.InvoiceNumber).Select(x => x.InvoiceNumber).ToListAsync(),
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).OrderBy(x => x.BillNumber).Select(x => x.BillNumber).ToListAsync(),
            await dbContext.FinanceTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).OrderBy(x => x.ExternalReference).Select(x => x.ExternalReference).ToListAsync(),
            await dbContext.WorkTasks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).OrderBy(x => x.CorrelationId).Select(x => x.CorrelationId!).ToListAsync(),
            await dbContext.ApprovalRequests.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).OrderBy(x => x.TargetEntityId).Select(x => $"{x.ApprovalType}:{x.TargetEntityType}:{x.TargetEntityId:N}").ToListAsync(),
            await dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).OrderBy(x => x.AnomalyType).ThenBy(x => x.CreatedUtc).Select(x => x.AnomalyType).ToListAsync(),
            await readService.GetAnomalyWorkbenchAsync(new GetFinanceAnomalyWorkbenchQuery(companyId, Page: 1, PageSize: 25), CancellationToken.None),
            await dbContext.Alerts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).OrderBy(x => x.Fingerprint).Select(x => x.Fingerprint).ToListAsync(),
            await dbContext.AuditEvents.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Action.StartsWith("finance.", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Action).ThenBy(x => x.TargetId).Select(x => $"{x.Action}:{x.TargetType}:{x.TargetId}").ToListAsync());
    }

    private static async Task<(int Invoices, int Bills, int Transactions, int Balances, int Tasks, int Approvals, int Alerts, int Audits)> CaptureCountsAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId) =>
        (
            await dbContext.FinanceInvoices.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceBills.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceTransactions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceBalances.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.WorkTasks.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.ApprovalRequests.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.Alerts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId),
            await dbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && x.Action.StartsWith("finance.", StringComparison.OrdinalIgnoreCase))
        );

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<VirtualCompanyDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var context = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed record FinanceSnapshot(
        DateTime? CurrentSimulatedDateTime,
        IReadOnlyList<string> InvoiceNumbers,
        IReadOnlyList<string> BillNumbers,
        IReadOnlyList<string> TransactionReferences,
        IReadOnlyList<string> TaskCorrelations,
        IReadOnlyList<string> ApprovalTargets,
        IReadOnlyList<string> SeedAnomalyTypes,
        FinanceAnomalyWorkbenchResultDto WorkbenchSnapshot,
        IReadOnlyList<string> AlertFingerprints,
        IReadOnlyList<string> AuditKeys);

    private static async Task<(int Invoices, int Bills, int Transactions, int Balances, int Tasks, int Approvals, int SeedAnomalies, int Alerts, int Audits)> CaptureCountsAsync(
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
