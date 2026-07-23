using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260414100000_AddTriggerExecutionDeadLetterRetryState")]
public partial class AddTriggerExecutionDeadLetterRetryState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var dateTimeType = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"
            ? "timestamp with time zone"
            : "datetime2";

        migrationBuilder.AddColumn<DateTime>(
            name: "next_retry_at",
            table: "trigger_execution_attempts",
            type: dateTimeType,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "next_retry_at", table: "trigger_execution_attempts");
}
