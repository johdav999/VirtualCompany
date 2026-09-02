using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyRunMigrationTests
{
    [Fact]
    public void Migration_is_additive_company_scoped_and_indexed_for_coalescing_and_claims()
    {
        var operations = Operations("Up");
        var tables = operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Equal(5, tables.Count);
        Assert.Equal(new HashSet<string>
        {
            "finance_autonomy_runs", "finance_autonomy_run_steps", "finance_autonomy_step_attempts",
            "finance_autonomy_run_history", "finance_autonomy_run_sources"
        }, tables.Keys.ToHashSet());
        Assert.All(tables.Values, table => Assert.Contains(table.Columns,
            column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(tables["finance_autonomy_runs"].Columns,
            column => column.Name == "row_version" && !column.IsNullable && column.MaxLength == 16);
        Assert.Contains(tables["finance_autonomy_run_steps"].Columns,
            column => column.Name == "row_version" && !column.IsNullable && column.MaxLength == 16);
        Assert.Contains(tables["finance_autonomy_runs"].Columns, column => column.Name == "evidence_hash");
        Assert.Contains(tables["finance_autonomy_runs"].Columns, column => column.Name == "plan_hash");
        Assert.Contains(tables["finance_autonomy_run_steps"].Columns, column => column.Name == "approval_request_id");
        Assert.Contains(tables["finance_autonomy_run_steps"].Columns, column => column.Name == "tool_execution_attempt_id");
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_autonomy_runs" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "logical_key"]));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "finance_autonomy_run_steps" &&
            index.Columns.SequenceEqual(["company_id", "status", "lease_expires_utc", "sequence"]));
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropColumnOperation or SqlOperation);
    }

    [Fact]
    public void Down_path_removes_only_the_five_run_lifecycle_tables()
    {
        var operations = Operations("Down");
        Assert.Equal(5, operations.OfType<DropTableOperation>().Count());
        Assert.DoesNotContain(operations.OfType<DropTableOperation>(), x =>
            x.Name is "finance_autonomy_grants" or "finance_autonomy_grant_versions" or "finance_autonomy_controls");
    }

    private static IReadOnlyList<MigrationOperation> Operations(string methodName)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new ImplementDurableFinanceAutonomyRuns();
        typeof(ImplementDurableFinanceAutonomyRuns).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
        return builder.Operations;
    }
}
