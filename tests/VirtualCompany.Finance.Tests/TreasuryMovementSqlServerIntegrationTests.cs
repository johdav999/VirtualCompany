using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

[Trait("Category", "SqlServer")]
public sealed class TreasuryMovementSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Sql_server_allows_only_one_bank_leg_update_from_the_same_source_version()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!)
        {
            InitialCatalog = $"virtualcompany_treasury_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var connectionString = builder.ConnectionString;
        await using (var setup = CreateContext(connectionString)) await setup.Database.MigrateAsync();

        try
        {
            var seeded = await SeedAsync(connectionString);
            var now = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
            await using var outboundContext = CreateContext(connectionString);
            await using var inboundContext = CreateContext(connectionString);
            var outbound = await outboundContext.TreasuryTransfers.IgnoreQueryFilters().SingleAsync(x => x.Id == seeded.SourceId);
            var inbound = await inboundContext.TreasuryTransfers.IgnoreQueryFilters().SingleAsync(x => x.Id == seeded.SourceId);
            outbound.AttachBankLeg(outbound.Version, TreasuryTransferLegRoles.Outbound, seeded.OutboundId, seeded.UserId, now);
            inbound.AttachBankLeg(inbound.Version, TreasuryTransferLegRoles.Inbound, seeded.InboundId, seeded.UserId, now);

            await outboundContext.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => inboundContext.SaveChangesAsync());

            await using var verification = CreateContext(connectionString);
            var persisted = await verification.TreasuryTransfers.IgnoreQueryFilters().SingleAsync(x => x.Id == seeded.SourceId);
            Assert.Equal(TreasuryMovementStatuses.InTransit, persisted.Status);
            Assert.Equal(2, persisted.Version);
            Assert.Equal(seeded.OutboundId, persisted.OutboundBankTransactionId);
            Assert.Null(persisted.InboundBankTransactionId);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<Seeded> SeedAsync(string connectionString)
    {
        var companyId = Guid.NewGuid(); var userId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);
        var fromFinance = new FinanceAccount(Guid.NewGuid(), companyId, "1930", "Operating cash", "asset", "SEK", 0m, now,
            accountClass: FinanceAccountClassValues.Asset, normalBalance: FinanceNormalBalanceValues.Debit,
            effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);
        var toFinance = new FinanceAccount(Guid.NewGuid(), companyId, "1940", "Reserve cash", "asset", "SEK", 0m, now,
            accountClass: FinanceAccountClassValues.Asset, normalBalance: FinanceNormalBalanceValues.Debit,
            effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);
        var fromBank = new CompanyBankAccount(Guid.NewGuid(), companyId, fromFinance.Id, "Operating", "Testbank", "•••• 1000", "SEK");
        var toBank = new CompanyBankAccount(Guid.NewGuid(), companyId, toFinance.Id, "Reserve", "Testbank", "•••• 2000", "SEK");
        var outbound = new BankTransaction(Guid.NewGuid(), companyId, fromBank.Id, now, now, -100m, "SEK", "TRANSFER-OUT", "Internal", importSource: "sql-test");
        var inbound = new BankTransaction(Guid.NewGuid(), companyId, toBank.Id, now, now, 100m, "SEK", "TRANSFER-IN", "Internal", importSource: "sql-test");
        var source = new TreasuryTransfer(sourceId, companyId, "sql-transfer-1", fromBank.Id, toBank.Id,
            100m, 0m, "SEK", null, 0m, null, userId, now);
        await using var db = CreateContext(connectionString);
        db.AddRange(new Company(companyId, "Treasury SQL company"), fromFinance, toFinance, fromBank, toBank,
            outbound, inbound, source);
        await db.SaveChangesAsync();
        return new(sourceId, outbound.Id, inbound.Id, userId);
    }

    private static VirtualCompanyDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker)
                    .Assembly.GetName().Name))
            .Options);

    private sealed record Seeded(Guid SourceId, Guid OutboundId, Guid InboundId, Guid UserId);
}
