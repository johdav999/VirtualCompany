using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class AccountingCloseMigrationSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Representative_P3_upgrade_adds_close_layers_without_replaying_prior_schema()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!;
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"virtualcompany_close_upgrade_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.Migrations.PersistPreferredCompanySelection)
                    .Assembly.GetName().Name))
            .Options;

        await using var context = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
        try
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260829183551_CompleteAccountingAdministrationGovernance");
            await migrator.MigrateAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Contains(applied, id => id.EndsWith("_AddAccountingCloseOrchestration", StringComparison.Ordinal));
            Assert.Contains(applied, id => id.EndsWith("_AddAccountingCloseGovernance", StringComparison.Ordinal));

            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM (VALUES
                    (OBJECT_ID(N'accounting_close_instances', N'U')),
                    (OBJECT_ID(N'accounting_close_readiness_snapshots', N'U')),
                    (OBJECT_ID(N'accounting_close_waivers', N'U')),
                    (OBJECT_ID(N'accounting_close_sign_offs', N'U'))
                ) AS required_tables(object_id)
                WHERE object_id IS NOT NULL;
                """;
            Assert.Equal(4, Convert.ToInt32(await command.ExecuteScalarAsync()));
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
