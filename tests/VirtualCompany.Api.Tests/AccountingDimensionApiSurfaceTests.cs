using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingDimensionApiSurfaceTests
{
    [Fact]
    public void Dimension_reads_require_accounting_view_and_mutations_require_accounting_admin()
    {
        var type = typeof(InternalFinanceController);
        foreach (var methodName in new[]
                 {
                     nameof(InternalFinanceController.GetAccountingDimensionWorkspaceAsync),
                     nameof(InternalFinanceController.PreviewAccountingDimensionAllocationAsync),
                     nameof(InternalFinanceController.GetAccountingDimensionReportAsync)
                 })
        {
            var method = Assert.Single(type.GetMethods(), candidate => candidate.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                attribute => attribute.Policy == CompanyPolicies.AccountingView);
        }

        foreach (var methodName in new[]
                 {
                     nameof(InternalFinanceController.SaveAccountingDimensionTypeAsync),
                     nameof(InternalFinanceController.SaveAccountingDimensionMemberAsync),
                     nameof(InternalFinanceController.SaveAccountingDimensionAccountPolicyAsync),
                     nameof(InternalFinanceController.SaveAccountingDimensionCombinationRuleAsync),
                     nameof(InternalFinanceController.SaveAccountingDimensionExternalMappingAsync),
                     nameof(InternalFinanceController.SaveAccountingAllocationTemplateAsync),
                     nameof(InternalFinanceController.ApplyAccountingDimensionAllocationAsync)
                 })
        {
            var method = Assert.Single(type.GetMethods(), candidate => candidate.Name == methodName);
            Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(),
                attribute => attribute.Policy == CompanyPolicies.AccountingAdmin);
        }
    }

    [Fact]
    public void Migration_is_additive_tenant_scoped_and_records_ambiguous_legacy_values()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddGovernedAccountingDimensions();
        var up = migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);

        var types = Assert.Single(builder.Operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "accounting_dimension_types");
        Assert.Contains(types.Columns, column => column.Name == "company_id" && !column.IsNullable);
        Assert.Contains(types.ForeignKeys, foreignKey => foreignKey.PrincipalTable == "companies" &&
            foreignKey.Columns.SequenceEqual(["company_id"]));

        var postedAssignments = Assert.Single(builder.Operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "ledger_entry_line_dimensions");
        Assert.Contains(postedAssignments.Columns, column => column.Name == "member_name_snapshot" && !column.IsNullable);
        Assert.Contains(postedAssignments.Columns, column => column.Name == "hierarchy_path_snapshot" && !column.IsNullable);

        var sql = string.Join("\n", builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        Assert.Contains("legacy_cost_center_unmapped", sql, StringComparison.Ordinal);
        Assert.Contains("accounting_dimension_mapping_conflicts", sql, StringComparison.Ordinal);
        Assert.Contains("FROM budgets", sql, StringComparison.Ordinal);
        Assert.Contains("FROM forecasts", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
