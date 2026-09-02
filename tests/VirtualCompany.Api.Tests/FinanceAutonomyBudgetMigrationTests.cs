using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyBudgetMigrationTests
{
    [Fact]
    public void Migration_is_additive_company_scoped_and_concurrency_safe()
    {
        var operations = Operations("Up");
        var tables = operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Equal(new HashSet<string>
        {
            "finance_autonomy_budget_policies", "finance_autonomy_budget_windows",
            "finance_autonomy_budget_reservations", "finance_autonomy_circuit_breakers",
            "finance_autonomy_budget_alerts"
        }, tables.Keys.ToHashSet());
        Assert.All(tables.Values, table => Assert.Contains(table.Columns,
            column => column.Name == "company_id" && !column.IsNullable));
        foreach (var tableName in new[] { "finance_autonomy_budget_policies", "finance_autonomy_budget_windows", "finance_autonomy_circuit_breakers" })
            Assert.Contains(tables[tableName].Columns,
                column => column.Name == "row_version" && !column.IsNullable && column.MaxLength == 16);
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_autonomy_budget_windows" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "policy_id", "window_start_utc", "window_end_utc"]));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_autonomy_budget_reservations" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "reservation_key", "policy_id"]));
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropColumnOperation or SqlOperation);
    }

    [Fact]
    public void Down_removes_only_prompt_four_tables()
    {
        var operations = Operations("Down");
        Assert.Equal(5, operations.OfType<DropTableOperation>().Count());
        Assert.DoesNotContain(operations.OfType<DropTableOperation>(), x =>
            x.Name is "finance_autonomy_runs" or "finance_autonomy_trigger_cursors" or "finance_autonomy_grants");
    }

    private static IReadOnlyList<MigrationOperation> Operations(string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new ImplementFinanceAutonomyBudgetsAndCircuitBreakers();
        typeof(ImplementFinanceAutonomyBudgetsAndCircuitBreakers)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
