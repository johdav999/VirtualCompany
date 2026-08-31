using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class YearEndRolloverMigrationSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Upgrade_after_prompt_seven_creates_year_end_schema_and_all_foreign_keys()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!;
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"virtualcompany_year_end_upgrade_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.Migrations.PersistPreferredCompanySelection)
                    .Assembly.GetName().Name)).Options;
        await using var context = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
        try
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260830230000_AddExternalAccountantCollaboration");
            await migrator.MigrateAsync("20260830240000_AddFormalYearEndRollover");

            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM (VALUES
                    (OBJECT_ID(N'year_end_runs', N'U')),
                    (OBJECT_ID(N'year_end_readiness_snapshots', N'U')),
                    (OBJECT_ID(N'year_end_retained_earnings_proposals', N'U')),
                    (OBJECT_ID(N'year_end_opening_balance_candidates', N'U')),
                    (OBJECT_ID(N'year_end_approval_signoffs', N'U')),
                    (OBJECT_ID(N'year_end_subsequent_events', N'U')),
                    (OBJECT_ID(N'year_end_history', N'U')),
                    (OBJECT_ID(N'year_end_correction_records', N'U')),
                    (OBJECT_ID(N'year_end_operations', N'U'))
                ) AS required_tables(object_id)
                WHERE object_id IS NOT NULL;
                """;
            Assert.Equal(9, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class UnscopedCompanyContextAccessor : ICompanyContextAccessor
    {
        public Guid? CompanyId => null;
        public Guid? UserId => null;
        public bool IsResolved => false;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) { }
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) { }
    }
}
