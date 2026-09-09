using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingSessionMigrationTests
{
    [Fact]
    public void Migration_contains_relational_session_schema_and_tenant_safe_keys()
    {
        var operations = UpOperations(new AddSalesMeetingSessions());
        var table = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "sales_meeting_sessions");

        Assert.Contains(table.Columns, column => column.Name == "concurrency_version" && !column.IsNullable);
        Assert.Contains(table.Columns, column => column.Name == "retention_until_at" && !column.IsNullable);
        Assert.Contains(table.CheckConstraints, constraint => constraint.Name == "CK_sales_meeting_sessions_duration");
        Assert.Contains(table.CheckConstraints, constraint => constraint.Name == "CK_sales_meeting_sessions_retention");
        Assert.Contains(table.ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "sales_meeting_invitations" &&
            foreignKey.Columns.SequenceEqual(["company_id", "invitation_id"]) &&
            foreignKey.PrincipalColumns!.SequenceEqual(["company_id", "id"]));
        Assert.Contains(table.ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "customer_companies" &&
            foreignKey.Columns.SequenceEqual(["company_id", "customer_company_id"]) &&
            foreignKey.PrincipalColumns!.SequenceEqual(["company_id", "id"]));

        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, index =>
            index.Table == "sales_meeting_sessions" &&
            index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "invitation_id"]));
        Assert.Contains(indexes, index =>
            index.Table == "sales_meeting_sessions" &&
            index.Columns.SequenceEqual(["company_id", "status", "updated_at"]));
    }

    [ApiSqlServerFact]
    [Trait("Category", "SqlServer")]
    public async Task Latest_sql_server_migration_creates_session_table_and_constraints()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!;
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"virtualcompany_sales_meeting_session_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker)
                    .Assembly.GetName().Name))
            .Options;

        await using var context = new VirtualCompanyDbContext(options, new UnscopedCompanyContextAccessor());
        try
        {
            await context.Database.MigrateAsync();
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM sys.foreign_keys
                WHERE parent_object_id = OBJECT_ID(N'sales_meeting_sessions', N'U');
                """;

            Assert.Equal(6, Convert.ToInt32(await command.ExecuteScalarAsync()));
            Assert.False(context.Database.HasPendingModelChanges());
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static IReadOnlyList<MigrationOperation> UpOperations(Migration migration)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = migration.GetType().GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);
        return builder.Operations;
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
