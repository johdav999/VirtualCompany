using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingRealtimeVoicePilotAndControlledDemoScenario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "demo_scenario_key",
                table: "companies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "demo_scenario_version",
                table: "companies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_demo_tenant",
                table: "companies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "demo_scenario_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scenario_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    scenario_version = table.Column<int>(type: "int", nullable: false),
                    provisioned_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meeting_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    linked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    current_step = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    reset_generation = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_reset_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_scenario_runs", x => x.id);
                    table.UniqueConstraint("AK_demo_scenario_runs_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_demo_scenario_runs_generation", "reset_generation >= 1");
                    table.CheckConstraint("CK_demo_scenario_runs_step", "current_step >= 0");
                    table.CheckConstraint("CK_demo_scenario_runs_version", "scenario_version >= 1");
                    table.ForeignKey(
                        name: "FK_demo_scenario_runs_sales_meeting_sessions_company_id_meeting_session_id",
                        columns: x => new { x.company_id, x.meeting_session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "demo_scenario_command_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reset_generation = table.Column<int>(type: "int", nullable: false),
                    step_number = table.Column<int>(type: "int", nullable: false),
                    command_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disposition = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    result_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    executed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_scenario_command_executions", x => x.id);
                    table.CheckConstraint("CK_demo_scenario_command_executions_generation", "reset_generation >= 1");
                    table.CheckConstraint("CK_demo_scenario_command_executions_step", "step_number >= 1");
                    table.ForeignKey(
                        name: "FK_demo_scenario_command_executions_demo_scenario_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "demo_scenario_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_companies_is_demo_tenant_demo_scenario_key_demo_scenario_version",
                table: "companies",
                columns: new[] { "is_demo_tenant", "demo_scenario_key", "demo_scenario_version" });

            migrationBuilder.CreateIndex(
                name: "IX_demo_scenario_command_executions_company_id_run_id_reset_generation_idempotency_key",
                table: "demo_scenario_command_executions",
                columns: new[] { "company_id", "run_id", "reset_generation", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_scenario_command_executions_company_id_run_id_reset_generation_step_number",
                table: "demo_scenario_command_executions",
                columns: new[] { "company_id", "run_id", "reset_generation", "step_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_scenario_runs_company_id",
                table: "demo_scenario_runs",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_scenario_runs_company_id_meeting_session_id",
                table: "demo_scenario_runs",
                columns: new[] { "company_id", "meeting_session_id" },
                unique: true,
                filter: "[meeting_session_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_demo_scenario_runs_company_id_scenario_key_scenario_version",
                table: "demo_scenario_runs",
                columns: new[] { "company_id", "scenario_key", "scenario_version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demo_scenario_command_executions");

            migrationBuilder.DropTable(
                name: "demo_scenario_runs");

            migrationBuilder.DropIndex(
                name: "IX_companies_is_demo_tenant_demo_scenario_key_demo_scenario_version",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "demo_scenario_key",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "demo_scenario_version",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "is_demo_tenant",
                table: "companies");
        }
    }
}
