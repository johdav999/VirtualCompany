using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyGrantMigrationTests
{
    [Fact]
    public void Migration_is_additive_tenant_scoped_versioned_and_does_not_create_implicit_grants()
    {
        var up = Operations("Up");
        var tables = up.OfType<CreateTableOperation>().ToDictionary(x => x.Name);

        Assert.Equal(3, tables.Count);
        Assert.Contains("finance_autonomy_grants", tables.Keys);
        Assert.Contains("finance_autonomy_grant_versions", tables.Keys);
        Assert.Contains("finance_autonomy_controls", tables.Keys);
        Assert.All(tables.Values, table =>
            Assert.Contains(table.Columns, column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(tables["finance_autonomy_grant_versions"].Columns,
            column => column.Name == "capability_policy_hash" && !column.IsNullable);
        Assert.Contains(tables["finance_autonomy_grant_versions"].Columns,
            column => column.Name == "authority_hash" && !column.IsNullable);

        Assert.Contains(up.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_autonomy_grants" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "agent_id", "capability_id"]));
        Assert.Contains(up.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_autonomy_controls" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "scope_key"]));
        Assert.DoesNotContain(up, operation =>
            operation is DropTableOperation or DropColumnOperation or SqlOperation);
    }

    [Fact]
    public void Migration_down_path_removes_only_the_new_finance_autonomy_tables()
    {
        var dropped = Operations("Down").OfType<DropTableOperation>().Select(x => x.Name).ToHashSet();

        Assert.Equal(3, dropped.Count);
        Assert.Contains("finance_autonomy_grants", dropped);
        Assert.Contains("finance_autonomy_grant_versions", dropped);
        Assert.Contains("finance_autonomy_controls", dropped);
    }

    private static IReadOnlyList<MigrationOperation> Operations(string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new ImplementFinanceAutonomyGrants();
        typeof(ImplementFinanceAutonomyGrants)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
