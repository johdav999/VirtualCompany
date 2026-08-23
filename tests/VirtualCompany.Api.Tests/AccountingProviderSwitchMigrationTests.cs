using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchMigrationTests
{
    [Fact]
    public void Sql_server_migration_is_additive_tenant_scoped_constrained_and_concurrent()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchLifecycle();
        var up = migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);

        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "accounting_provider_switches");
        Assert.Contains(table.Columns, column =>
            column.Name == "company_id" && !column.IsNullable);
        Assert.Contains(table.Columns, column =>
            column.Name == "version" && column.ClrType == typeof(long) && !column.IsNullable);
        Assert.Contains(table.CheckConstraints, constraint => constraint.Name == "CK_accounting_provider_switches_status");
        Assert.Contains(table.CheckConstraints, constraint => constraint.Name == "CK_accounting_provider_switches_strategy");
        Assert.Contains(table.CheckConstraints, constraint => constraint.Name == "CK_accounting_provider_switches_distinct_endpoints");
        Assert.Contains(table.ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "companies" &&
            foreignKey.Columns.SequenceEqual(["company_id"]));
        Assert.Contains(table.ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "finance_fiscal_periods" &&
            foreignKey.Columns.SequenceEqual(["effective_fiscal_period_id"]));
        Assert.Contains(table.ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "agents" &&
            foreignKey.Columns.SequenceEqual(["company_id", "responsible_agent_id"]));

        var activeIndex = Assert.Single(builder.Operations.OfType<CreateIndexOperation>(),
            operation => operation.Name == "UX_accounting_provider_switches_company_active");
        Assert.True(activeIndex.IsUnique);
        Assert.Equal(["company_id"], activeIndex.Columns);
        Assert.Contains("completed", activeIndex.Filter, StringComparison.Ordinal);
        Assert.Contains("cancelled", activeIndex.Filter, StringComparison.Ordinal);

        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
