using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchRehearsalMigrationTests
{
    [Fact]
    public void Migration_adds_only_additive_tenant_scoped_rehearsal_evidence_and_plan_tables()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchRehearsal();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("accounting_provider_switch_rehearsals", tables.Keys);
        Assert.Contains("accounting_provider_switch_reconciliation_checks", tables.Keys);
        Assert.Contains("accounting_provider_switch_cutover_plans", tables.Keys);
        Assert.Contains("accounting_provider_switch_plan_approvals", tables.Keys);
        Assert.All(tables.Values, table => Assert.Contains(table.Columns, x => x.Name == "company_id" && !x.IsNullable));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), x =>
            x.Table == "accounting_provider_switch_rehearsals" && x.IsUnique && x.Columns.Contains("idempotency_key"));
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
