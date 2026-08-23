using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchCutoverMigrationTests
{
    [Fact]
    public void Migration_adds_tenant_scoped_cutover_snapshot_checks_approval_and_materialization_tables()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchFinalCutover();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("accounting_provider_switch_cutover_executions", tables.Keys);
        Assert.Contains("accounting_provider_switch_final_snapshots", tables.Keys);
        Assert.Contains("accounting_provider_switch_final_checks", tables.Keys);
        Assert.Contains("accounting_provider_switch_activation_approvals", tables.Keys);
        Assert.Contains("accounting_provider_switch_native_materializations", tables.Keys);
        Assert.All(tables.Values, table => Assert.Contains(table.Columns,
            column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_cutover_executions" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "switch_id"]) && index.Filter is not null);
        Assert.Contains(builder.Operations.OfType<AddColumnOperation>(), column =>
            column.Table == "accounting_provider_switch_target_transfer_items" && column.Name == "sanitized_payload_json");
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
