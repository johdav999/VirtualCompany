using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchMonitoringMigrationTests
{
    [Fact]
    public void Migration_adds_tenant_scoped_monitoring_evidence_with_worker_and_incident_indexes()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchMonitoring();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("accounting_provider_switch_monitoring_runs", tables.Keys);
        Assert.Contains("accounting_provider_switch_monitoring_checks", tables.Keys);
        Assert.Contains("accounting_provider_switch_monitoring_incidents", tables.Keys);
        Assert.All(tables.Values, table => Assert.Contains(table.Columns,
            column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(tables["accounting_provider_switch_monitoring_runs"].ForeignKeys,
            key => key.Columns.SequenceEqual(["company_id", "switch_id", "activation_execution_id"]));
        Assert.Contains(tables["accounting_provider_switch_monitoring_runs"].ForeignKeys,
            key => key.Columns.SequenceEqual(["company_id", "closure_approval_request_id"]));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_monitoring_incidents" && index.IsUnique &&
            index.Columns.SequenceEqual(["company_id", "monitoring_run_id", "fingerprint"]));
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
