using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingCaptureMigrationTests
{
    [Fact]
    public void Migration_adds_tenant_scoped_capture_questions_evidence_and_idempotency_state()
    {
        var migration = new AddSalesMeetingGroundedCapture();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);

        var tables = builder.Operations.OfType<CreateTableOperation>().ToArray();
        Assert.Contains(tables, x => x.Name == "sales_meeting_transcript_segments");
        Assert.Contains(tables, x => x.Name == "sales_meeting_observations");
        Assert.Contains(tables, x => x.Name == "sales_meeting_action_items");
        Assert.Contains(tables, x => x.Name == "sales_meeting_questions");
        Assert.Contains(tables, x => x.Name == "sales_meeting_question_evidence");

        var columns = builder.Operations.OfType<AddColumnOperation>().ToArray();
        Assert.Contains(columns, x => x.Table == "sales_meeting_sessions" &&
            x.Name == "capture_version" && !x.IsNullable && Equals(x.DefaultValue, 0L));
        Assert.Contains(columns, x => x.Table == "sales_meeting_sessions" &&
            x.Name == "last_capture_batch_id" && x.IsNullable);

        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Table == "sales_meeting_transcript_segments" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "session_id", "client_item_id"]));
        Assert.Contains(indexes, x => x.Table == "sales_meeting_questions" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "session_id", "client_question_id"]));
        Assert.Contains(indexes, x => x.Table == "sales_meeting_question_evidence" && x.IsUnique &&
            x.Columns.SequenceEqual(["company_id", "question_id", "claim_order", "source_id"]));
    }
}
