using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationRuntimeMigrationTests
{
    [Fact]
    public void Migration_adds_durable_command_identity_and_sequence_to_the_authoritative_session()
    {
        var migration = new AddSalesPresentationRuntimeSynchronization();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Migration Up method was not found.");
        up.Invoke(migration, [builder]);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToArray();
        Assert.Contains(columns, x => x.Table == "sales_meeting_sessions" &&
            x.Name == "last_presentation_sequence" && !x.IsNullable && Equals(x.DefaultValue, 0L));
        Assert.Contains(columns, x => x.Table == "sales_meeting_sessions" &&
            x.Name == "last_presentation_command_id" && x.IsNullable);
    }
}
