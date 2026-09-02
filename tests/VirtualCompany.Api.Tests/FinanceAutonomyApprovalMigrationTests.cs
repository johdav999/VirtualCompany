using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyApprovalMigrationTests
{
    [Fact]
    public void Migration_retains_plan_revision_lineage_without_destructive_changes()
    {
        var operations = Operations("Up");
        Assert.Contains(operations.OfType<AddColumnOperation>(), column =>
            column.Table == "finance_autonomy_runs" && column.Name == "revision_of_run_id" && column.IsNullable);
        Assert.Contains(operations.OfType<AddColumnOperation>(), column =>
            column.Table == "finance_autonomy_runs" && column.Name == "revision_number" && !column.IsNullable);
        Assert.Contains(operations.OfType<AddForeignKeyOperation>(), foreignKey =>
            foreignKey.Table == "finance_autonomy_runs" && foreignKey.PrincipalTable == "finance_autonomy_runs");
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropColumnOperation);
    }

    private static IReadOnlyList<MigrationOperation> Operations(string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new ImplementFinanceAutonomyApprovalAndHumanControl();
        typeof(ImplementFinanceAutonomyApprovalAndHumanControl)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }
}
