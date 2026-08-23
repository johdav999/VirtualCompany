using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchTargetTransferMigrationTests
{
    [Fact]
    public void Migration_adds_additive_tenant_scoped_transfer_batch_item_attempt_and_acknowledgement_tables()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new AddAccountingProviderSwitchTargetTransfer();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("accounting_provider_switch_target_transfer_batches", tables.Keys);
        Assert.Contains("accounting_provider_switch_target_transfer_items", tables.Keys);
        Assert.Contains("accounting_provider_switch_target_transfer_attempts", tables.Keys);
        Assert.Contains("accounting_provider_switch_target_acknowledgements", tables.Keys);
        Assert.All(tables.Values, table => Assert.Contains(table.Columns,
            column => column.Name == "company_id" && !column.IsNullable));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_target_transfer_batches" && index.IsUnique &&
            index.Columns.Contains("idempotency_key"));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_target_transfer_items" && index.IsUnique &&
            index.Columns.Contains("stable_identity"));
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "accounting_provider_switch_target_transfer_items" && index.IsUnique &&
            index.Columns.Contains("write_request_id") && index.Filter is not null);
        Assert.DoesNotContain(builder.Operations, operation => operation is DropTableOperation or DropColumnOperation);
    }
}
