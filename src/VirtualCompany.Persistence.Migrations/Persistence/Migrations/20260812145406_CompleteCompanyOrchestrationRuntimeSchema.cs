using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCompanyOrchestrationRuntimeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actual_evidence",
                table: "operating_reviews",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_evidence",
                table: "operating_reviews",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "next_action",
                table: "operating_reviews",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "plan_id",
                table: "operating_reviews",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "plan_version",
                table: "operating_reviews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewer_run_id",
                table: "operating_reviews",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uncertainty_json",
                table: "operating_reviews",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'{}'");

            migrationBuilder.AddColumn<string>(
                name: "emergency_stop_reason",
                table: "company_operating_configurations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "emergency_stopped",
                table: "company_operating_configurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "emergency_stopped_at",
                table: "company_operating_configurations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maximum_model_calls_per_day",
                table: "company_operating_configurations",
                type: "int",
                nullable: false,
                defaultValue: 16);

            migrationBuilder.AddColumn<decimal>(
                name: "maximum_monetary_budget_per_day",
                table: "company_operating_configurations",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maximum_tasks_per_day",
                table: "company_operating_configurations",
                type: "int",
                nullable: false,
                defaultValue: 48);

            migrationBuilder.AddColumn<int>(
                name: "maximum_tool_calls_per_day",
                table: "company_operating_configurations",
                type: "int",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.CreateTable(
                name: "company_operating_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_operating_leases", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_operating_leases_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operating_dispatches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    max_attempts = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    orchestration_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    collaboration_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_dispatches", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_dispatches_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operating_dispatches_operating_initiatives_initiative_id",
                        column: x => x.initiative_id,
                        principalTable: "operating_initiatives",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_dispatches_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "operating_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    materiality = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    deduplication_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    affected_goal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    suppression_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_events_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operating_events_company_goals_affected_goal_id",
                        column: x => x.affected_goal_id,
                        principalTable: "company_goals",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "operating_initiative_collaborators",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiative_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    pattern = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    objective = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    expected_artifact = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_initiative_collaborators", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_initiative_collaborators_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_operating_initiative_collaborators_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operating_initiative_collaborators_operating_initiatives_initiative_id",
                        column: x => x.initiative_id,
                        principalTable: "operating_initiatives",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "operating_cycle_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operating_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    operating_cycle_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    trigger_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    trigger_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    deduplication_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    not_before_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    max_attempts = table.Column<int>(type: "int", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_cycle_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_cycle_requests_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_operating_cycle_requests_operating_cycles_operating_cycle_id",
                        column: x => x.operating_cycle_id,
                        principalTable: "operating_cycles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_operating_cycle_requests_operating_events_operating_event_id",
                        column: x => x.operating_event_id,
                        principalTable: "operating_events",
                        principalColumn: "id");
                });

            migrationBuilder.Sql(
                """
                UPDATE review
                SET review.plan_id = initiative.plan_id,
                    review.plan_version = plan_row.version,
                    review.expected_evidence = COALESCE(NULLIF(initiative.completion_evidence, N''), N'Existing operating review evidence.'),
                    review.next_action = N'Review the existing outcome evidence.'
                FROM operating_reviews AS review
                INNER JOIN operating_initiatives AS initiative ON initiative.id = review.initiative_id
                INNER JOIN operating_plans AS plan_row ON plan_row.id = initiative.plan_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_operating_reviews_plan_id",
                table: "operating_reviews",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_operating_leases_company_id",
                table: "company_operating_leases",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_operating_leases_lease_expires_at",
                table: "company_operating_leases",
                column: "lease_expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_operating_cycle_requests_company_id_deduplication_key",
                table: "operating_cycle_requests",
                columns: new[] { "company_id", "deduplication_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_cycle_requests_operating_cycle_id",
                table: "operating_cycle_requests",
                column: "operating_cycle_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_cycle_requests_operating_event_id",
                table: "operating_cycle_requests",
                column: "operating_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_cycle_requests_status_not_before_at_lease_expires_at",
                table: "operating_cycle_requests",
                columns: new[] { "status", "not_before_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_dispatches_company_id_initiative_id",
                table: "operating_dispatches",
                columns: new[] { "company_id", "initiative_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_dispatches_company_id_status_next_attempt_at",
                table: "operating_dispatches",
                columns: new[] { "company_id", "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_dispatches_initiative_id",
                table: "operating_dispatches",
                column: "initiative_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_dispatches_status_lease_expires_at",
                table: "operating_dispatches",
                columns: new[] { "status", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_dispatches_task_id",
                table: "operating_dispatches",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_events_affected_goal_id",
                table: "operating_events",
                column: "affected_goal_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_events_company_id_deduplication_key",
                table: "operating_events",
                columns: new[] { "company_id", "deduplication_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_events_company_id_status_materiality_observed_at",
                table: "operating_events",
                columns: new[] { "company_id", "status", "materiality", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiative_collaborators_agent_id",
                table: "operating_initiative_collaborators",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiative_collaborators_company_id_agent_id",
                table: "operating_initiative_collaborators",
                columns: new[] { "company_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiative_collaborators_company_id_initiative_id_agent_id_role",
                table: "operating_initiative_collaborators",
                columns: new[] { "company_id", "initiative_id", "agent_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operating_initiative_collaborators_initiative_id",
                table: "operating_initiative_collaborators",
                column: "initiative_id");

            migrationBuilder.AddForeignKey(
                name: "FK_operating_reviews_operating_plans_plan_id",
                table: "operating_reviews",
                column: "plan_id",
                principalTable: "operating_plans",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_operating_reviews_operating_plans_plan_id",
                table: "operating_reviews");

            migrationBuilder.DropTable(
                name: "company_operating_leases");

            migrationBuilder.DropTable(
                name: "operating_cycle_requests");

            migrationBuilder.DropTable(
                name: "operating_dispatches");

            migrationBuilder.DropTable(
                name: "operating_initiative_collaborators");

            migrationBuilder.DropTable(
                name: "operating_events");

            migrationBuilder.DropIndex(
                name: "IX_operating_reviews_plan_id",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "actual_evidence",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "expected_evidence",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "next_action",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "plan_id",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "plan_version",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "reviewer_run_id",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "uncertainty_json",
                table: "operating_reviews");

            migrationBuilder.DropColumn(
                name: "emergency_stop_reason",
                table: "company_operating_configurations");

            migrationBuilder.DropColumn(
                name: "emergency_stopped",
                table: "company_operating_configurations");

            migrationBuilder.DropColumn(
                name: "emergency_stopped_at",
                table: "company_operating_configurations");

            migrationBuilder.DropColumn(
                name: "maximum_model_calls_per_day",
                table: "company_operating_configurations");

            migrationBuilder.DropColumn(
                name: "maximum_monetary_budget_per_day",
                table: "company_operating_configurations");

            migrationBuilder.DropColumn(
                name: "maximum_tasks_per_day",
                table: "company_operating_configurations");

            migrationBuilder.DropColumn(
                name: "maximum_tool_calls_per_day",
                table: "company_operating_configurations");
        }
    }
}
