using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAccountingDraftMigrationTests
{
    [Fact]
    public void Migration_adds_source_provenance_and_tenant_scoped_reconciliation_idempotency_without_destructive_operations()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var migration = new ImplementFinanceAccountingDraftAgentTools();
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToArray();
        Assert.Contains(columns, x => x.Table == "manual_journal_drafts" &&
                                      x.Name == "source_references_json" &&
                                      Equals(x.DefaultValue, "[]") && !x.IsNullable);
        Assert.Contains(columns, x => x.Table == "finance_advanced_reconciliation_groups" &&
                                      x.Name == "idempotency_key" && x.MaxLength == 200);
        Assert.Contains(columns, x => x.Table == "finance_advanced_reconciliation_groups" &&
                                      x.Name == "proposal_hash" && x.MaxLength == 64);

        var index = Assert.Single(builder.Operations.OfType<CreateIndexOperation>(), x =>
            x.Table == "finance_advanced_reconciliation_groups" && x.IsUnique);
        Assert.Equal(["company_id", "idempotency_key"], index.Columns);
        Assert.Equal("idempotency_key IS NOT NULL", index.Filter);
        Assert.DoesNotContain(builder.Operations, x => x is DropTableOperation or DropColumnOperation);
    }
}
