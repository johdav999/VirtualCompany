using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyExecutorMigrationTests
{
    [Fact]
    public void Migration_adds_stable_idempotency_and_reconciliation_fields_with_upgrade_backfill()
    {
        var operations = Operations("Up");
        var columns = operations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(2, columns.Length);
        Assert.Contains(columns, column => column.Table == "finance_autonomy_run_steps" &&
            column.Name == "business_idempotency_key" && !column.IsNullable && column.MaxLength == 200);
        Assert.Contains(columns, column => column.Table == "finance_autonomy_run_steps" &&
            column.Name == "reconciliation_reference" && column.IsNullable && column.MaxLength == 240);
        var backfill = Assert.Single(operations.OfType<SqlOperation>());
        Assert.Contains("company_id", backfill.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run_id", backfill.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[id]", backfill.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropColumnOperation);
    }

    [Fact]
    public void Down_removes_only_prompt_five_columns()
    {
        var operations = Operations("Down");
        Assert.Equal(new HashSet<string> { "business_idempotency_key", "reconciliation_reference" },
            operations.OfType<DropColumnOperation>().Select(x => x.Name).ToHashSet());
        Assert.All(operations.OfType<DropColumnOperation>(), column =>
            Assert.Equal("finance_autonomy_run_steps", column.Table));
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation);
    }

    private static IReadOnlyList<MigrationOperation> Operations(string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new ImplementLeasedFinanceAutonomyExecutorAndRecovery();
        typeof(ImplementLeasedFinanceAutonomyExecutorAndRecovery)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
