using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260414090000_AddTriggerExecutionAttempts")]
public partial class AddTriggerExecutionAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "trigger_execution_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                trigger_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                trigger_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                occurrence_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                denial_reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                retry_attempt_count = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                failure_details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                dispatch_reference_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                dispatch_reference_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_trigger_execution_attempts", x => x.id);
                table.ForeignKey(
                    name: "FK_trigger_execution_attempts_agents_agent_id",
                    column: x => x.agent_id,
                    principalTable: "agents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_trigger_execution_attempts_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_trigger_execution_attempts_idempotency_key",
            table: "trigger_execution_attempts",
            column: "idempotency_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_trigger_execution_attempts_company_id_trigger_type_status_occurrence_at",
            table: "trigger_execution_attempts",
            columns: new[] { "company_id", "trigger_type", "status", "occurrence_at" });

        migrationBuilder.CreateIndex(
            name: "IX_trigger_execution_attempts_company_id_agent_id_occurrence_at",
            table: "trigger_execution_attempts",
            columns: new[] { "company_id", "agent_id", "occurrence_at" });

        migrationBuilder.CreateIndex(
            name: "IX_trigger_execution_attempts_company_id_correlation_id",
            table: "trigger_execution_attempts",
            columns: new[] { "company_id", "correlation_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "trigger_execution_attempts");
}
