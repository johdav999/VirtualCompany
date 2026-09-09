using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingChangeProposalMigrationTests
{
    [Fact]
    public void Migration_adds_relational_proposals_and_canonical_deal_fields()
    {
        var migration = new AddSalesMeetingChangeProposalReview(); var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
        var table = Assert.Single(builder.Operations.OfType<CreateTableOperation>(), x => x.Name == "sales_meeting_change_proposals");
        Assert.Contains(table.Columns, x => x.Name == "proposed_value_json"); Assert.Contains(table.Columns, x => x.Name == "before_value_json");
        Assert.Contains(table.Columns, x => x.Name == "approval_binding_hash"); Assert.Contains(table.Columns, x => x.Name == "concurrency_version");
        Assert.Contains(builder.Operations.OfType<CreateIndexOperation>(), x => x.Table == table.Name && x.IsUnique && x.Columns.SequenceEqual(["company_id", "idempotency_key"]));
        Assert.Contains(builder.Operations.OfType<AddColumnOperation>(), x => x.Table == "deals" && x.Name == "probability");
        Assert.Contains(builder.Operations.OfType<AddColumnOperation>(), x => x.Table == "deals" && x.Name == "next_step");
    }
}
