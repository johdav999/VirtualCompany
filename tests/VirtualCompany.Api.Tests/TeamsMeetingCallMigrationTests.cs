using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsMeetingCallMigrationTests
{
    [Fact]
    public void Migration_persists_tenant_safe_call_and_deduplicated_receipts()
    {
        var migration = new AddTeamsMeetingCallControl();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
        var calls = Assert.Single(builder.Operations.OfType<CreateTableOperation>(), x => x.Name == "teams_meeting_calls");
        var receipts = Assert.Single(builder.Operations.OfType<CreateTableOperation>(), x => x.Name == "teams_call_notification_receipts");
        Assert.Contains(calls.Columns, x => x.Name == "concurrency_version" && !x.IsNullable);
        Assert.Contains(calls.ForeignKeys, x => x.Columns.SequenceEqual(["company_id", "meeting_session_id"]));
        Assert.Contains(receipts.ForeignKeys, x => x.Columns.SequenceEqual(["company_id", "call_id"]));
        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Table == calls.Name && x.IsUnique && x.Columns.SequenceEqual(["company_id", "meeting_session_id"]));
        Assert.Contains(indexes, x => x.Table == receipts.Name && x.IsUnique && x.Columns.SequenceEqual(["company_id", "event_key"]));
        Assert.DoesNotContain(calls.Columns, x => x.Name.Contains("join_url", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Organizer_control_migration_persists_explicit_media_authority_without_sensitive_data()
    {
        var migration = new AddTeamsPresenterOrganizerControls();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
        var columns = builder.Operations.OfType<AddColumnOperation>().ToArray();

        Assert.Contains(columns, column => column.Table == "teams_meeting_calls" && column.Name == "media_start_authorized_at" && column.IsNullable);
        Assert.Contains(columns, column => column.Table == "teams_meeting_calls" && column.Name == "media_start_authorized_by_user_id" && column.IsNullable);
        Assert.DoesNotContain(columns, column => column.Name.Contains("audio", StringComparison.OrdinalIgnoreCase) || column.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
    }
}
