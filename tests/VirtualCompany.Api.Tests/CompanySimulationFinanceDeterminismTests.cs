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
        var alerts = await dbContext.Alerts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Type == AlertType.Anomaly)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();

        return new FinanceSnapshot(
            invoiceNumbers,
            billNumbers,
            transactionReferences,
            alerts
                .Select(x => x.Metadata["anomalyType"]?.GetValue<string>() ?? string.Empty)
                .ToList(),
            alerts
                .Select(x => (x.LastDetectedUtc ?? x.CreatedUtc).Date)
                .Distinct()
                .OrderBy(x => x)
                .ToList());
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
        IReadOnlyList<string> AnomalyTypes,
        IReadOnlyList<DateTime> AlertDates);

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
