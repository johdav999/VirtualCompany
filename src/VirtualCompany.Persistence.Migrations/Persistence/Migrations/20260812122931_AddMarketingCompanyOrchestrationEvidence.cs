using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingCompanyOrchestrationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assignment_context_json",
                table: "marketing_operating_runs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "marketing_company_signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_operating_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    signal_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    cycle_evaluation_requested = table.Column<bool>(type: "bit", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_company_signals", x => x.id);
                    table.UniqueConstraint("AK_marketing_company_signals_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_company_signals_marketing_operating_runs_company_id_marketing_operating_run_id",
                        columns: x => new { x.company_id, x.marketing_operating_run_id },
                        principalTable: "marketing_operating_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "marketing_work_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_operating_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operating_initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    work_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    record_type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    evidence_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    completed_artifacts_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expected_results_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    actual_results_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    data_gaps_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    blockers_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    dependencies_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    changed_forecast_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    lessons = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    requested_next_action = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_work_evidence", x => x.id);
                    table.UniqueConstraint("AK_marketing_work_evidence_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_work_evidence_marketing_operating_runs_company_id_marketing_operating_run_id",
                        columns: x => new { x.company_id, x.marketing_operating_run_id },
                        principalTable: "marketing_operating_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_company_signals_company_id",
                table: "marketing_company_signals",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_company_signals_company_id_idempotency_key",
                table: "marketing_company_signals",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_company_signals_company_id_marketing_operating_run_id",
                table: "marketing_company_signals",
                columns: new[] { "company_id", "marketing_operating_run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_company_signals_company_id_status_severity_created_at",
                table: "marketing_company_signals",
                columns: new[] { "company_id", "status", "severity", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_work_evidence_company_id",
                table: "marketing_work_evidence",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_work_evidence_company_id_idempotency_key",
                table: "marketing_work_evidence",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_work_evidence_company_id_marketing_operating_run_id_record_type_version",
                table: "marketing_work_evidence",
                columns: new[] { "company_id", "marketing_operating_run_id", "record_type", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_company_signals");

            migrationBuilder.DropTable(
                name: "marketing_work_evidence");

            migrationBuilder.DropColumn(
                name: "assignment_context_json",
                table: "marketing_operating_runs");
        }
    }
}
