using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class TeamsDeploymentMigrationSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Generated_upgrade_preserves_legacy_presenter_and_never_manufactures_uat_approval()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!)
        { InitialCatalog = $"virtualcompany_teams_upgrade_{Guid.NewGuid():N}", MultipleActiveResultSets = false };
        var master = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
        await using var server = new SqlConnection(master.ConnectionString); await server.OpenAsync();
        await using (var create = new SqlCommand($"CREATE DATABASE [{connection.InitialCatalog}]", server)) await create.ExecuteNonQueryAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlServer(connection.ConnectionString,
            sql => sql.MigrationsAssembly(typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker).Assembly.GetName().Name)).Options;
        await using var db = new VirtualCompanyDbContext(options);
        try
        {
            // Representative pre-upgrade tables: execute the actual EF-generated SQL, including its history writes.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
                CREATE TABLE agents (Id uniqueidentifier NOT NULL PRIMARY KEY, CompanyId uniqueidentifier NOT NULL,
                    TemplateId nvarchar(100) NOT NULL, Department nvarchar(100) NOT NULL, Status nvarchar(32) NOT NULL);
                CREATE TABLE sales_meeting_sessions (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL);
                CREATE TABLE teams_meeting_calls (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL, meeting_session_id uniqueidentifier NOT NULL);
                CREATE TABLE teams_tenant_registrations (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL, evidence nvarchar(100) NOT NULL);
                INSERT agents VALUES
                  ('00000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001',N'alex',N'Sales',N'active'),
                  ('00000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001',N'alex',N'Sales',N'active'),
                  ('00000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000002',N'maya',N'Marketing',N'active');
                INSERT sales_meeting_sessions VALUES
                  ('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'),
                  ('20000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002');
                INSERT teams_meeting_calls VALUES
                  (NEWID(),'10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001');
                INSERT teams_tenant_registrations VALUES (NEWID(),'10000000-0000-0000-0000-000000000001',N'consent-preserved');
                """);
            var migrations = db.Database.GetMigrations().ToArray();
            var target = migrations.Single(m => m.EndsWith("_AddTeamsFirstTestCallBinding"));
            var script = db.GetService<IMigrator>().GenerateScript("20260904143238_AddTeamsPresenterOrganizerControls",
                target, MigrationsSqlGenerationOptions.Idempotent);
            // Idempotent reapplication must preserve bindings and existing evidence.
            for (var iteration = 0; iteration < 2; iteration++)
                foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(s => !string.IsNullOrWhiteSpace(s)))
                    await db.Database.ExecuteSqlRawAsync(batch);
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM sales_meeting_sessions WHERE
                  (company_id='10000000-0000-0000-0000-000000000001' AND presenter_agent_id='00000000-0000-0000-0000-000000000001')
                  OR (company_id='10000000-0000-0000-0000-000000000002' AND presenter_agent_id IS NULL);
                """;
            Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync()));
            command.CommandText = "SELECT COUNT(*) FROM teams_meeting_calls WHERE presenter_agent_id='00000000-0000-0000-0000-000000000001' AND first_uat_authorized_at IS NULL";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
            command.CommandText = "SELECT COUNT(*) FROM teams_tenant_registrations WHERE evidence=N'consent-preserved' AND first_uat_authorized_at IS NULL AND first_uat_expires_at IS NULL";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
            SqlConnection.ClearAllPools();
            await using var drop = new SqlCommand($"ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]", server);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
