using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class SalesNarrationSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Fresh_migrations_and_concurrent_asset_claims_preserve_one_owner()
    {
        await WithDatabase(async db =>
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            var company = Guid.NewGuid();
            db.Companies.Add(new Company(company, "Synthetic narration SQL test"));
            var asset = new SalesNarrationAsset { Id = Guid.NewGuid(), CompanyId = company, CacheKey = new string('a',64),
                CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
            db.SalesNarrationAssets.Add(asset); await db.SaveChangesAsync();
            await using var other = new VirtualCompanyDbContext(Options(db.Database.GetConnectionString()!));
            var stale = await other.SalesNarrationAssets.IgnoreQueryFilters().SingleAsync();
            asset.Status = SalesNarrationAsset.Generating; asset.Version++; await db.SaveChangesAsync();
            stale.Status = SalesNarrationAsset.Generating; stale.Version++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
            other.ChangeTracker.Clear();
            other.SalesNarrationAssets.Add(new() { Id = Guid.NewGuid(), CompanyId = company, CacheKey = asset.CacheKey,
                CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        });
    }

    [ApiSqlServerFact]
    public async Task Additive_upgrade_is_repeatable_and_preserves_existing_Teams_records()
    {
        await WithDatabase(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
                CREATE TABLE companies (Id uniqueidentifier NOT NULL PRIMARY KEY);
                CREATE TABLE sales_meeting_sessions (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL,
                    CONSTRAINT AK_sessions UNIQUE(company_id,id));
                CREATE TABLE sales_presentation_decks (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL,
                    CONSTRAINT AK_decks UNIQUE(company_id,id));
                CREATE TABLE teams_meeting_calls (id uniqueidentifier NOT NULL PRIMARY KEY, evidence nvarchar(100) NOT NULL);
                INSERT teams_meeting_calls VALUES ('30000000-0000-0000-0000-000000000001',N'unchanged-teams-evidence');
                """);
            var migrations = db.Database.GetMigrations().ToArray();
            var index = Array.FindIndex(migrations, x => x.EndsWith("_AddApprovedSalesNarration"));
            var script = db.GetService<IMigrator>().GenerateScript(migrations[index-1], migrations[index], MigrationsSqlGenerationOptions.Idempotent);
            for (var pass = 0; pass < 2; pass++)
                foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
                    await db.Database.ExecuteSqlRawAsync(batch);
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM teams_meeting_calls WHERE id='30000000-0000-0000-0000-000000000001' AND evidence=N'unchanged-teams-evidence'";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        });
    }

    private static DbContextOptions<VirtualCompanyDbContext> Options(string connection) => new DbContextOptionsBuilder<VirtualCompanyDbContext>()
        .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker).Assembly.GetName().Name)).Options;
    private static async Task WithDatabase(Func<VirtualCompanyDbContext, Task> action)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!)
        { InitialCatalog = $"virtualcompany_narration_test_{Guid.NewGuid():N}", Pooling = false };
        var master = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
        await using var server = new SqlConnection(master.ConnectionString); await server.OpenAsync();
        await using (var create = new SqlCommand($"CREATE DATABASE [{connection.InitialCatalog}]", server)) await create.ExecuteNonQueryAsync();
        try { await using var db = new VirtualCompanyDbContext(Options(connection.ConnectionString)); await action(db); }
        finally
        {
            if (!Regex.IsMatch(connection.InitialCatalog, @"^virtualcompany_narration_test_[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test database name.");
            await using var drop = new SqlCommand($"ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]", server);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

