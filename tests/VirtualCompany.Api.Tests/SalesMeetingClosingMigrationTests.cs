using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingClosingMigrationTests
{
    [Fact]
    public void Migration_creates_physically_separate_versioned_customer_and_internal_artifacts()
    {
        var migration = new AddSalesMeetingClosingArtifacts(); var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Migration Up method was not found."); up.Invoke(migration, [builder]);
        var tables = builder.Operations.OfType<CreateTableOperation>().ToArray();
        Assert.Contains(tables, x => x.Name == "sales_meeting_minutes"); Assert.Contains(tables, x => x.Name == "sales_meeting_minutes_items");
        Assert.Contains(tables, x => x.Name == "sales_meeting_internal_intelligence"); Assert.Contains(tables, x => x.Name == "sales_meeting_internal_intelligence_items");
        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Table == "sales_meeting_minutes" && x.IsUnique && x.Columns.SequenceEqual(["company_id", "session_id", "generation_request_id"]));
        Assert.Contains(indexes, x => x.Table == "sales_meeting_minutes" && x.IsUnique && x.Columns.SequenceEqual(["company_id", "session_id", "artifact_version"]));
        Assert.Contains(indexes, x => x.Table == "sales_meeting_internal_intelligence" && x.IsUnique && x.Columns.SequenceEqual(["company_id", "session_id", "artifact_version"]));
    }
}
