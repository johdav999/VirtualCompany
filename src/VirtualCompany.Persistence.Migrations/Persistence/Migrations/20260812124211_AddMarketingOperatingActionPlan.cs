using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingOperatingActionPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_operating_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_operating_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    capability = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    tool = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    target_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    goal_relevance = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    dependencies_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expected_completion_evidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    authority_decision = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    estimated_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    actual_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    maximum_attempts = table.Column<int>(type: "int", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    artifact_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actual_evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    recovery_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    recovery_guidance = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_operating_actions", x => x.id);
                    table.UniqueConstraint("AK_marketing_operating_actions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_operating_actions_marketing_operating_runs_company_id_marketing_operating_run_id",
                        columns: x => new { x.company_id, x.marketing_operating_run_id },
                        principalTable: "marketing_operating_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_operating_actions_company_id",
                table: "marketing_operating_actions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_operating_actions_company_id_idempotency_key",
                table: "marketing_operating_actions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_operating_actions_company_id_marketing_operating_run_id_sequence",
                table: "marketing_operating_actions",
                columns: new[] { "company_id", "marketing_operating_run_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_operating_actions_company_id_status_next_attempt_at_lease_expires_at",
                table: "marketing_operating_actions",
                columns: new[] { "company_id", "status", "next_attempt_at", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_operating_actions");
        }
    }
}
