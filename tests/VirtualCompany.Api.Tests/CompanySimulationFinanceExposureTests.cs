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

public sealed class CompanySimulationFinanceExposureTests
{
    [Fact]
    public async Task Generated_invoices_bills_and_assets_preserve_exposure_until_settlement()
    {
        var companyId = Guid.Parse("1de78357-4d8f-48b5-9aa8-050086f2c001");
        var provider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Exposure Company"));
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
                """{"financeGeneration":{"assetPurchaseCadenceDays":4,"assetPurchaseOffsetDays":1,"assetFundingBehavior":"alternate"}}"""),
            CancellationToken.None);

        provider.Advance(TimeSpan.FromSeconds(160));
        _ = await stateService.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        var fullInvoice = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.InvoiceNumber.EndsWith("-FULL", StringComparison.OrdinalIgnoreCase));
        var partialInvoice = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.InvoiceNumber.EndsWith("-PART", StringComparison.OrdinalIgnoreCase));
        var unpaidInvoice = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.SettlementStatus == FinanceSettlementStatuses.Unpaid);

        var fullInvoiceAllocated = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == fullInvoice.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var partialInvoiceAllocated = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == partialInvoice.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var unpaidInvoiceAllocated = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == unpaidInvoice.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        Assert.Equal(FinanceSettlementStatuses.Paid, fullInvoice.SettlementStatus);
        Assert.Equal(fullInvoice.Amount, fullInvoiceAllocated);
        Assert.Equal(FinanceSettlementStatuses.PartiallyPaid, partialInvoice.SettlementStatus);
        Assert.Equal(Math.Round(partialInvoice.Amount * 0.50m, 2, MidpointRounding.AwayFromZero), partialInvoiceAllocated);
        Assert.Equal(FinanceSettlementStatuses.Unpaid, unpaidInvoice.SettlementStatus);
        Assert.Equal(0m, unpaidInvoiceAllocated);

        var settledBill = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.SettlementStatus == FinanceSettlementStatuses.Paid);
        var openBill = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.SettlementStatus == FinanceSettlementStatuses.Unpaid);

        var settledBillAllocated = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.BillId == settledBill.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var openBillAllocated = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.BillId == openBill.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        Assert.Equal(settledBill.Amount, settledBillAllocated);
        Assert.Equal(0m, openBillAllocated);

        var cashFundedAsset = await dbContext.FinanceAssets
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.FundingBehavior == FinanceAssetFundingBehaviors.Cash);
        var payableFundedAsset = await dbContext.FinanceAssets
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.FundingBehavior == FinanceAssetFundingBehaviors.Payable);

        var cashAssetPaymentExists = await dbContext.Payments
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.CounterpartyReference == cashFundedAsset.ReferenceNumber && x.PaymentType == PaymentTypes.Outgoing);
        var cashAssetTransaction = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.ExternalReference == $"{cashFundedAsset.ReferenceNumber}-PAY");
        var payableAssetPaymentExists = await dbContext.Payments
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.CounterpartyReference == payableFundedAsset.ReferenceNumber);

        Assert.Equal(FinanceSettlementStatuses.Paid, cashFundedAsset.FundingSettlementStatus);
        Assert.True(cashAssetPaymentExists);
        Assert.True(cashAssetTransaction.Amount < 0m);
        Assert.Equal(FinanceSettlementStatuses.Unpaid, payableFundedAsset.FundingSettlementStatus);
        Assert.True(payableFundedAsset.HasPayableExposure);
        Assert.False(payableAssetPaymentExists);
    }

    [Fact]
    public async Task Fixed_seed_replays_asset_purchase_dates_amounts_counterparties_and_funding_behavior()
    {
        var companyId = Guid.Parse("a2238870-5e95-4e38-aa9d-a4d583228e01");
        var first = await RunAssetSnapshotAsync(companyId, 73);
        var second = await RunAssetSnapshotAsync(companyId, 73);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    private static async Task<IReadOnlyList<AssetSnapshot>> RunAssetSnapshotAsync(Guid companyId, int seed)
    {
        var provider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Companies.Add(new Company(companyId, "Asset Replay Company"));
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
                """{"financeGeneration":{"assetPurchaseCadenceDays":4,"assetPurchaseOffsetDays":1,"assetFundingBehavior":"alternate"}}"""),
            CancellationToken.None);

        provider.Advance(TimeSpan.FromSeconds(160));
        _ = await stateService.GetStateAsync(new GetCompanySimulationStateQuery(companyId), CancellationToken.None);

        return await dbContext.FinanceAssets
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ReferenceNumber)
            .Select(x => new AssetSnapshot(
                x.ReferenceNumber,
                x.Name,
                x.Category,
                x.PurchasedUtc,
                x.Amount,
                x.Currency,
                x.FundingBehavior,
                x.FundingSettlementStatus,
                x.Status,
                x.CounterpartyId))
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

    private sealed record AssetSnapshot(
        string ReferenceNumber,
        string Name,
        string Category,
        DateTime PurchasedUtc,
        decimal Amount,
        string Currency,
        string FundingBehavior,
        string FundingSettlementStatus,
        string Status,
        Guid CounterpartyId);

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
