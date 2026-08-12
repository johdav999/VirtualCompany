using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingOperatingLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_operating_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_goal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    operating_initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    work_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    trigger_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    trigger_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    effective_authority = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    configuration_version = table.Column<int>(type: "int", nullable: false),
                    evidence_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    selected_work_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    missing_evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    outcome_summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    recovery_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    budget_limit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    budget_used = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_operating_runs", x => x.id);
                    table.UniqueConstraint("AK_marketing_operating_runs_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_operating_runs_company_id",
                table: "marketing_operating_runs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_operating_runs_company_id_agent_id_status_created_at",
                table: "marketing_operating_runs",
                columns: new[] { "company_id", "agent_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_operating_runs_company_id_idempotency_key",
                table: "marketing_operating_runs",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_operating_runs");
        }
    }
}
