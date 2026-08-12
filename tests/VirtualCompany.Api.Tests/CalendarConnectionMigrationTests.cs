using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class CalendarConnectionMigrationTests
{
    [Fact]
    public void Calendar_separation_migration_contains_schema_tenant_keys_and_existing_row_backfill()
    {
        var operations = UpOperations(new SeparateCalendarConnectionsFromMailboxes());

        Assert.Contains(operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "external_account_connections");
        Assert.Contains(operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "calendar_connections");
        Assert.Contains(operations.OfType<AddColumnOperation>(),
            operation => operation.Table == "mailbox_connections" &&
                operation.Name == "is_primary_inbound");
        var calendarTable = Assert.Single(operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "calendar_connections");
        Assert.Contains(calendarTable.ForeignKeys,
            operation => operation.PrincipalTable == "external_account_connections" &&
                operation.Columns.SequenceEqual(["company_id", "external_account_connection_id"]) &&
                operation.PrincipalColumns is not null &&
                operation.PrincipalColumns.SequenceEqual(["company_id", "id"]));
        Assert.Contains(operations.OfType<AddForeignKeyOperation>(),
            operation => operation.Table == "sales_meeting_invitations" &&
                operation.PrincipalTable == "calendar_connections" &&
                operation.Columns.SequenceEqual(["company_id", "calendar_connection_id"]));

        var backfill = Assert.Single(operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("ROW_NUMBER() OVER", backfill, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO external_account_connections", backfill, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO calendar_connections", backfill, StringComparison.Ordinal);
        Assert.Contains("UPDATE invitation", backfill, StringComparison.Ordinal);
        Assert.DoesNotContain(operations.OfType<DropColumnOperation>(),
            operation => operation.Table == "sales_meeting_invitations" &&
                operation.Name == "confirmation_mailbox_connection_id");
    }

    [Fact]
    public void Threading_mode_migration_is_additive_and_constrained()
    {
        var operations = UpOperations(new RecordMeetingConfirmationThreadingMode());

        var column = Assert.Single(operations.OfType<AddColumnOperation>());
        Assert.Equal("sales_meeting_invitations", column.Table);
        Assert.Equal("confirmation_threading_mode", column.Name);
        Assert.Equal("unknown", column.DefaultValue);

        var constraint = Assert.Single(operations.OfType<AddCheckConstraintOperation>());
        Assert.Contains("header_based", constraint.Sql, StringComparison.Ordinal);
        Assert.Contains("native", constraint.Sql, StringComparison.Ordinal);
    }

    private static IReadOnlyList<MigrationOperation> UpOperations(Migration migration)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = migration.GetType().GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);
        return builder.Operations;
    }

}
