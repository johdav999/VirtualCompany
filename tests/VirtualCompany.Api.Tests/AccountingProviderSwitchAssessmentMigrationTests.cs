using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchAssessmentMigrationTests
{
    [Fact]
    public void Migration_adds_durable_tenant_scoped_assessment_evidence_without_destructive_operations()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchAssessment();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("accounting_provider_switch_assessments", tables.Keys);
        Assert.Contains("accounting_provider_switch_capabilities", tables.Keys);
        Assert.Contains("accounting_provider_switch_datasets", tables.Keys);
        Assert.Contains("accounting_provider_switch_gaps", tables.Keys);
        Assert.All(tables.Values, table => Assert.Contains(table.Columns, x => x.Name == "company_id" && !x.IsNullable));
        Assert.Contains(tables["accounting_provider_switch_assessments"].ForeignKeys, foreignKey =>
            foreignKey.PrincipalTable == "accounting_provider_switches" &&
            foreignKey.Columns.SequenceEqual(["company_id", "switch_id"]));
        Assert.Contains(tables["accounting_provider_switch_datasets"].CheckConstraints,
            x => x.Name == "CK_accounting_provider_switch_datasets_availability");
        Assert.DoesNotContain(builder.Operations, x => x is DropTableOperation or DropColumnOperation);
    }
}
