using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchStagingMigrationTests
{
    [Fact]
    public void Migration_adds_additive_relational_staging_mapping_and_replay_constraints()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchStaging();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("accounting_provider_switch_staged_records", tables.Keys);
        Assert.Contains("accounting_provider_switch_mapping_sets", tables.Keys);
        Assert.Contains("accounting_provider_switch_mapping_decisions", tables.Keys);
        Assert.Contains("accounting_provider_switch_mapping_records", tables.Keys);
        Assert.All(tables.Values, table => Assert.Contains(table.Columns, x => x.Name == "company_id" && !x.IsNullable));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_staged_records" && index.IsUnique &&
            index.Columns.Contains("stable_identity_hash"));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_staged_records" && index.IsUnique &&
            index.Filter is not null && index.Columns.Contains("source_record_key_hash"));
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
