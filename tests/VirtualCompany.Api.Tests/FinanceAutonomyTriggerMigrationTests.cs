using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyTriggerMigrationTests
{
    [Fact]
    public void Migration_adds_reviewed_limits_and_tenant_scoped_durable_trigger_state()
    {
        var operations = Operations("Up");
        var tables = operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Equal(2, tables.Count);
        Assert.Contains("finance_autonomy_trigger_cursors", tables.Keys);
        Assert.Contains("finance_autonomy_trigger_events", tables.Keys);
        Assert.All(tables.Values, table => Assert.Contains(table.Columns,
            column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(tables["finance_autonomy_trigger_cursors"].Columns,
            column => column.Name == "row_version" && !column.IsNullable && column.MaxLength == 16);
        Assert.Contains(operations.OfType<AddColumnOperation>(), x =>
            x.Table == "finance_autonomy_grant_versions" && x.Name == "allowed_event_types_json");
        Assert.Contains(operations.OfType<AddColumnOperation>(), x =>
            x.Table == "finance_autonomy_grant_versions" && x.Name == "minimum_interval_minutes" &&
            Equals(x.DefaultValue, 60));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), x =>
            x.Table == "finance_autonomy_trigger_cursors" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "grant_version_id", "trigger_kind", "trigger_key"]));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), x =>
            x.Table == "finance_autonomy_trigger_events" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "cursor_id", "source_event_id", "source_event_version"]));
        Assert.DoesNotContain(operations, x => x is DropTableOperation or DropColumnOperation or SqlOperation);
    }

    [Fact]
    public void Down_removes_only_trigger_tables_and_prompt_three_grant_columns()
    {
        var operations = Operations("Down");
        Assert.Equal(2, operations.OfType<DropTableOperation>().Count());
        Assert.Equal(7, operations.OfType<DropColumnOperation>().Count());
        Assert.DoesNotContain(operations.OfType<DropTableOperation>(), x =>
            x.Name is "finance_autonomy_runs" or "finance_autonomy_grants");
    }

    private static IReadOnlyList<MigrationOperation> Operations(string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new ImplementScheduledAndEventDrivenFinanceTriggers();
        typeof(ImplementScheduledAndEventDrivenFinanceTriggers)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
