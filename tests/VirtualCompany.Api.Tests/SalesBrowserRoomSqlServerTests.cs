using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;
[Trait("Category", "SqlServer")]
public sealed class SalesBrowserRoomSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Fresh_database_accepts_complete_migration_chain()
    {
        await WithDatabase(async db =>
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [ApiSqlServerFact]
    public async Task Upgrade_preserves_Teams_and_enforces_tenant_uniqueness_and_consent_concurrency()
    {
        await WithDatabase(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
                CREATE TABLE sales_meeting_invitations (id uniqueidentifier NOT NULL PRIMARY KEY,company_id uniqueidentifier NOT NULL,provider nvarchar(32) NOT NULL,create_online_meeting bit NOT NULL,online_meeting_url nvarchar(2000) NULL,CONSTRAINT AK_test_invitations_company_id_id UNIQUE(company_id,id));
                INSERT sales_meeting_invitations VALUES
                  ('40000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','microsoft365',1,'https://teams.microsoft.com/preserved'),
                  ('40000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','google',1,'https://meet.google.com/preserved'),
                  ('40000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000001','microsoft365',0,NULL);
                CREATE TABLE sales_meeting_sessions (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL,
                    CONSTRAINT AK_sales_meeting_sessions_company_id_id UNIQUE(company_id,id));
                CREATE TABLE teams_meeting_calls (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL, evidence nvarchar(100) NOT NULL);
                INSERT sales_meeting_sessions VALUES ('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');
                INSERT teams_meeting_calls VALUES ('30000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001',N'preserve-call-state');
                """);
            var migrations = db.Database.GetMigrations().ToArray();
            var target = Array.FindIndex(migrations, x => x.EndsWith("_AddBrowserSalesRooms"));
            var script = db.GetService<IMigrator>().GenerateScript(migrations[target - 1], migrations[^1], MigrationsSqlGenerationOptions.Idempotent);
            for (var pass = 0; pass < 2; pass++)
                foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
                    await db.Database.ExecuteSqlRawAsync(batch);
            var company = Guid.Parse("10000000-0000-0000-0000-000000000001"); var meeting = Guid.Parse("20000000-0000-0000-0000-000000000001");
            var room = new SalesBrowserRoom(company, meeting, Guid.NewGuid(), DateTime.UtcNow.AddHours(1), DateTime.UtcNow);
            db.SalesBrowserRooms.Add(room); await db.SaveChangesAsync();
            db.SalesBrowserRooms.Add(new SalesBrowserRoom(company, meeting, Guid.NewGuid(), DateTime.UtcNow.AddHours(1), DateTime.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            db.SalesBrowserRooms.Add(new SalesBrowserRoom(Guid.NewGuid(), meeting, Guid.NewGuid(), DateTime.UtcNow.AddHours(1), DateTime.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var participant = new SalesRoomParticipant(company, room.Id, "Guest", new string('A', 64), DateTime.UtcNow.AddMinutes(30));
            db.SalesRoomParticipants.Add(participant); await db.SaveChangesAsync();
            await using var other = new VirtualCompanyDbContext(Options(db.Database.GetConnectionString()!));
            var stale = await other.SalesRoomParticipants.IgnoreQueryFilters().SingleAsync();
            participant.Consent("ai_processing", true); await db.SaveChangesAsync();
            stale.Consent("retained_transcript", true);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
            var grant = new SalesRoomInvitationGrant(company, room.Id, new string('B', 64), DateTime.UtcNow.AddMinutes(30), room.OrganizerUserId);
            db.SalesRoomInvitationGrants.Add(grant); await db.SaveChangesAsync();
            other.ChangeTracker.Clear();
            var staleGrant = await other.SalesRoomInvitationGrants.IgnoreQueryFilters().SingleAsync();
            grant.Redeem(participant.Id, DateTime.UtcNow); await db.SaveChangesAsync();
            staleGrant.Redeem(Guid.NewGuid(), DateTime.UtcNow);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
            await db.Database.OpenConnectionAsync();
            await using var check = db.Database.GetDbConnection().CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM teams_meeting_calls WHERE id='30000000-0000-0000-0000-000000000001' AND evidence=N'preserve-call-state'";
            Assert.Equal(1, Convert.ToInt32(await check.ExecuteScalarAsync()));
            check.CommandText="SELECT COUNT(*) FROM sales_meeting_invitations WHERE (conferencing='teams' AND online_meeting_url='https://teams.microsoft.com/preserved') OR (conferencing='google_meet' AND online_meeting_url='https://meet.google.com/preserved') OR (conferencing='none' AND create_online_meeting=0 AND online_meeting_url IS NULL)";
            Assert.Equal(3,Convert.ToInt32(await check.ExecuteScalarAsync()));
        });
    }
    private static DbContextOptions<VirtualCompanyDbContext> Options(string connection) => new DbContextOptionsBuilder<VirtualCompanyDbContext>()
        .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker).Assembly.GetName().Name)).Options;
    private static async Task WithDatabase(Func<VirtualCompanyDbContext, Task> action)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!)
        { InitialCatalog = $"virtualcompany_browser_room_test_{Guid.NewGuid():N}", Pooling = false };
        var master = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
        await using var server = new SqlConnection(master.ConnectionString); await server.OpenAsync();
        await using (var create = new SqlCommand($"CREATE DATABASE [{connection.InitialCatalog}]", server)) await create.ExecuteNonQueryAsync();
        try { await using var db = new VirtualCompanyDbContext(Options(connection.ConnectionString)); await action(db); }
        finally
        {
            if (!Regex.IsMatch(connection.InitialCatalog, @"^virtualcompany_browser_room_test_[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test database name.");
            await using var drop = new SqlCommand($"ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]", server);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
