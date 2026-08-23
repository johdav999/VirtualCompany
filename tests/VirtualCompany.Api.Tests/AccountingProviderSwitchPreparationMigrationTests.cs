using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchPreparationMigrationTests
{
    [Fact]
    public void Migration_adds_only_additive_tenant_scoped_preparation_candidate_and_evidence_tables()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchPreparation();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("accounting_provider_switch_preparations", tables.Keys);
        Assert.Contains("accounting_provider_switch_readiness_checks", tables.Keys);
        Assert.Contains("accounting_provider_switch_native_candidates", tables.Keys);
        Assert.Contains("accounting_provider_switch_candidate_validations", tables.Keys);
        Assert.Contains("accounting_provider_switch_archive_dependencies", tables.Keys);
        Assert.All(tables.Values, table =>
            Assert.Contains(table.Columns, column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_preparations" && index.IsUnique &&
            index.Columns.Contains("idempotency_key"));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_native_candidates" && index.IsUnique &&
            index.Columns.Contains("staged_record_id") && index.Columns.Contains("candidate_kind"));
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
