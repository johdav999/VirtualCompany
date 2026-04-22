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

public sealed class CompanySimulationFinanceDeterminismTests
{
    [Fact]
    public async Task Different_seeds_shift_generated_finance_scenarios_and_periodic_anomalies()
    {
        var companyId = Guid.Parse("7b97f0a2-2e56-4ddb-bffa-3a1d0a65ef01");
        var first = await RunSnapshotAsync(
            companyId,
            73,
            """{"financeGeneration":{"anomalyCadenceDays":3,"anomalyOffsetDays":0}}""");
        var second = await RunSnapshotAsync(
            companyId,
            991,
            """{"financeGeneration":{"anomalyCadenceDays":3,"anomalyOffsetDays":0}}""");

        Assert.NotEqual(first.InvoiceNumbers, second.InvoiceNumbers);
        Assert.NotEqual(first.BillNumbers, second.BillNumbers);
        Assert.NotEqual(first.TransactionReferences, second.TransactionReferences);
        Assert.NotEqual(first.AnomalyTypes, second.AnomalyTypes);
        Assert.True(first.AlertDates.Count < first.InvoiceNumbers.Count);
        Assert.True(second.AlertDates.Count < second.InvoiceNumbers.Count);
    }

    [Fact]
    public async Task Same_seed_keeps_periodic_anomaly_spacing_for_the_same_replay_window()
    {
        var snapshot = await RunSnapshotAsync(
            Guid.Parse("6c89a653-28fa-4201-aa4a-f3b5d3d70b5a"),
            73,
            """{"financeGeneration":{"anomalyCadenceDays":3,"anomalyOffsetDays":1}}""");

        Assert.True(snapshot.AlertDates.Count >= 2);

        var gaps = snapshot.AlertDates
            .Zip(snapshot.AlertDates.Skip(1), (left, right) => (right - left).Days)
            .ToList();

        Assert.All(gaps, gap => Assert.Equal(3, gap));
    }

    [Fact]
    public async Task Same_seed_and_start_date_replay_preserves_causal_links_and_event_identifiers()
    {
        var companyId = Guid.Parse("8e34a7d4-bfd3-4d7f-9a0c-17f7ea8fe3a5");
        const int seed = 73;
        const string configurationJson = """{"financeGeneration":{"anomalyCadenceDays":3,"anomalyOffsetDays":1}}""";

        var first = await RunSnapshotAsync(companyId, seed, configurationJson);
        var second = await RunSnapshotAsync(companyId, seed, configurationJson);

        Assert.Equal(first.InvoiceCausality, second.InvoiceCausality);
        Assert.Equal(first.BillCausality, second.BillCausality);
        Assert.Equal(first.PaymentCausality, second.PaymentCausality);
        Assert.Equal(first.TransactionCausality, second.TransactionCausality);
        Assert.Equal(first.BalanceCausality, second.BalanceCausality);
        Assert.Equal(first.AllocationCausality, second.AllocationCausality);
        Assert.Equal(first.CashDeltaRecords, second.CashDeltaRecords);
        Assert.Equal(first.SimulationEventLinks, second.SimulationEventLinks);

        Assert.NotEmpty(first.InvoiceCausality);
        Assert.NotEmpty(first.BillCausality);
        Assert.NotEmpty(first.PaymentCausality);
        Assert.NotEmpty(first.TransactionCausality);
        Assert.NotEmpty(first.BalanceCausality);
        Assert.NotEmpty(first.AllocationCausality);
        Assert.NotEmpty(first.CashDeltaRecords);

        Assert.All(first.InvoiceCausality, x => Assert.NotEqual(Guid.Empty, x.SourceSimulationEventRecordId));
        Assert.All(first.BillCausality, x => Assert.NotEqual(Guid.Empty, x.SourceSimulationEventRecordId));
        Assert.All(first.PaymentCausality, x => Assert.NotEqual(Guid.Empty, x.SourceSimulationEventRecordId));
        Assert.All(first.TransactionCausality, x => Assert.NotEqual(Guid.Empty, x.SourceSimulationEventRecordId));
        Assert.All(first.BalanceCausality, x => Assert.NotEqual(Guid.Empty, x.SourceSimulationEventRecordId));
        Assert.All(first.AllocationCausality, x => Assert.NotEqual(Guid.Empty, x.SourceSimulationEventRecordId));
        Assert.All(first.CashDeltaRecords, x =>
        {
            Assert.NotEqual(Guid.Empty, x.SimulationEventRecordId);
            Assert.Equal(decimal.Round(x.CashBefore + x.CashDelta, 2, MidpointRounding.AwayFromZero), x.CashAfter);
        });
    }

    [Fact]
    public async Task Generated_cash_movements_persist_cash_delta_records_with_company_and_simulation_date()
    {
        var snapshot = await RunSnapshotAsync(
            Guid.Parse("d77fd389-f7c9-42c4-8938-fd7cd4011d68"),
            73,
            """{"financeGeneration":{"anomalyCadenceDays":3,"anomalyOffsetDays":1}}""");

        Assert.NotEmpty(snapshot.CashDeltaRecords);
        Assert.All(snapshot.CashDeltaRecords, record =>
        {
            Assert.NotEqual(Guid.Empty, record.Id);
            Assert.NotEqual(Guid.Empty, record.SimulationEventRecordId);
            Assert.NotEqual(default, record.SimulationDateUtc);
            Assert.Equal(decimal.Round(record.CashBefore + record.CashDelta, 2, MidpointRounding.AwayFromZero), record.CashAfter);
        });

        Assert.Equal(snapshot.CashDeltaRecords.Count, snapshot.SimulationEventLinks.Count(x => x.HasCashSnapshot));
    }

    [Fact]
    public async Task Same_profile_seed_and_window_replay_preserves_profiled_supplier_bills()
    {
        var companyId = Guid.Parse("e7b2605e-d377-4308-9a5d-1496e3f60311");
        var first = await RunProfiledBillSnapshotAsync(companyId, 73, "Software", "SaaS");
        var second = await RunProfiledBillSnapshotAsync(companyId, 73, "Software", "SaaS");

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public async Task Different_company_profiles_shift_recurring_cost_suppliers_terms_and_amounts()
    {
        var saas = await RunProfiledBillSnapshotAsync(
            Guid.Parse("f6bd8adb-4681-4929-bb46-d7dca2166d2d"),
            73,
            "Software",
            "SaaS");
        var retail = await RunProfiledBillSnapshotAsync(
            Guid.Parse("04728e31-c7d7-4897-b9fb-b0314691b673"),
            73,
            "Retail",
            "Ecommerce");

        Assert.NotEqual(saas, retail);
        Assert.NotEqual(
            saas.Select(x => x.SupplierName).Distinct().ToArray(),
            retail.Select(x => x.SupplierName).Distinct().ToArray());
        Assert.NotEqual(
            saas.Select(x => x.DueUtc).Distinct().ToArray(),
            retail.Select(x => x.DueUtc).Distinct().ToArray());
        Assert.NotEqual(saas.Select(x => x.Amount).ToArray(), retail.Select(x => x.Amount).ToArray());
    }

    private static async Task<FinanceSnapshot> RunSnapshotAsync(Guid companyId, int seed, string configurationJson)
    {
        var provider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Deterministic Replay Company"));
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
        _ = await stateService.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        var invoiceNumbers = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.InvoiceNumber)
            .Select(x => x.InvoiceNumber)
            .ToListAsync();
        var billNumbers = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.BillNumber)
            .Select(x => x.BillNumber)
            .ToListAsync();
        var transactionReferences = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ExternalReference)
            .Select(x => x.ExternalReference)
            .ToListAsync();
        var invoiceCausality = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.InvoiceNumber)
            .Select(x => new EntityCausality(x.Id, x.SourceSimulationEventRecordId ?? Guid.Empty))
            .ToListAsync();
        var billCausality = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.BillNumber)
            .Select(x => new EntityCausality(x.Id, x.SourceSimulationEventRecordId ?? Guid.Empty))
            .ToListAsync();
        var paymentCausality = await dbContext.Payments
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Id)
            .Select(x => new EntityCausality(x.Id, x.SourceSimulationEventRecordId ?? Guid.Empty))
            .ToListAsync();
        var transactionCausality = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ExternalReference)
            .Select(x => new EntityCausality(x.Id, x.SourceSimulationEventRecordId ?? Guid.Empty))
            .ToListAsync();
        var balanceCausality = await dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.AsOfUtc)
            .ThenBy(x => x.Id)
            .Select(x => new EntityCausality(x.Id, x.SourceSimulationEventRecordId ?? Guid.Empty))
            .ToListAsync();
        var allocationCausality = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Id)
            .Select(x => new AllocationCausality(
                x.Id,
                x.SourceSimulationEventRecordId ?? Guid.Empty,
                x.PaymentSourceSimulationEventRecordId ?? Guid.Empty,
                x.TargetSourceSimulationEventRecordId ?? Guid.Empty))
            .ToListAsync();
        var cashDeltaRecords = await dbContext.SimulationCashDeltaRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.SimulationDateUtc)
            .ThenBy(x => x.SimulationEventRecordId)
            .Select(x => new CashDeltaSnapshot(
                x.Id,
                x.SimulationEventRecordId,
                x.SimulationDateUtc,
                x.CashBefore,
                x.CashDelta,
                x.CashAfter))
            .ToListAsync();
        var simulationEventLinks = await dbContext.SimulationEventRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.SimulationDateUtc)
            .ThenBy(x => x.SequenceNumber)
            .ThenBy(x => x.Id)
            .Select(x => new SimulationEventLink(
                x.Id,
                x.ParentEventId,
                x.SourceEntityId,
                x.DeterministicKey,
                x.CashBefore.HasValue && x.CashDelta.HasValue && x.CashAfter.HasValue))
            .ToListAsync();
        var alerts = await dbContext.Alerts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Type == AlertType.Anomaly)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();

        return new FinanceSnapshot(
            invoiceNumbers,
            billNumbers,
            transactionReferences,
            invoiceCausality,
            billCausality,
            paymentCausality,
            transactionCausality,
            balanceCausality,
            allocationCausality,
            cashDeltaRecords,
            simulationEventLinks,
            alerts
                .Select(x => x.Metadata["anomalyType"]?.GetValue<string>() ?? string.Empty)
                .ToList(),
            alerts
                .Select(x => (x.LastDetectedUtc ?? x.CreatedUtc).Date)
                .Distinct()
                .OrderBy(x => x)
                .ToList());
    }

    private static async Task<IReadOnlyList<ProfiledBillSnapshot>> RunProfiledBillSnapshotAsync(
        Guid companyId,
        int seed,
        string industry,
        string businessType)
    {
        var provider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var company = new Company(companyId, $"Profile {industry} {businessType}");
        company.UpdateWorkspaceProfile(company.Name, industry, businessType, "UTC", "USD", "en", "US");
        dbContext.Companies.Add(company);
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
                """{"financeGeneration":{"anomalyCadenceDays":3,"anomalyOffsetDays":1}}"""),
            CancellationToken.None);

        provider.Advance(TimeSpan.FromSeconds(100));
        _ = await stateService.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        return await (
            from bill in dbContext.FinanceBills.IgnoreQueryFilters()
            join supplier in dbContext.FinanceCounterparties.IgnoreQueryFilters() on bill.CounterpartyId equals supplier.Id
            where bill.CompanyId == companyId
            orderby bill.BillNumber
            select new ProfiledBillSnapshot(
                bill.BillNumber,
                supplier.Name,
                bill.DueUtc,
                bill.Amount,
                bill.Status,
                bill.SettlementStatus))
            .ToListAsync();
    }

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
        IReadOnlyList<string> InvoiceNumbers,
        IReadOnlyList<string> BillNumbers,
        IReadOnlyList<string> TransactionReferences,
        IReadOnlyList<EntityCausality> InvoiceCausality,
        IReadOnlyList<EntityCausality> BillCausality,
        IReadOnlyList<EntityCausality> PaymentCausality,
        IReadOnlyList<EntityCausality> TransactionCausality,
        IReadOnlyList<EntityCausality> BalanceCausality,
        IReadOnlyList<AllocationCausality> AllocationCausality,
        IReadOnlyList<CashDeltaSnapshot> CashDeltaRecords,
        IReadOnlyList<SimulationEventLink> SimulationEventLinks,
        IReadOnlyList<string> AnomalyTypes,
        IReadOnlyList<DateTime> AlertDates);
    private sealed record ProfiledBillSnapshot(string BillNumber, string SupplierName, DateTime DueUtc, decimal Amount, string Status, string SettlementStatus);

    private sealed record EntityCausality(Guid EntityId, Guid SourceSimulationEventRecordId);
    private sealed record AllocationCausality(Guid AllocationId, Guid SourceSimulationEventRecordId, Guid PaymentSourceSimulationEventRecordId, Guid TargetSourceSimulationEventRecordId);
    private sealed record CashDeltaSnapshot(Guid Id, Guid SimulationEventRecordId, DateTime SimulationDateUtc, decimal CashBefore, decimal CashDelta, decimal CashAfter);
    private sealed record SimulationEventLink(Guid EventId, Guid? ParentEventId, Guid? SourceEntityId, string DeterministicKey, bool HasCashSnapshot);

    private sealed class MutableTimeProvider : TimeProvider
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
