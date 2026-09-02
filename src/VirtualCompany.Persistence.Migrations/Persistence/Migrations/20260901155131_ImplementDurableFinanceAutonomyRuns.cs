using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementDurableFinanceAutonomyRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_autonomy_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    capability_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    grant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grant_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grant_version_number = table.Column<int>(type: "int", nullable: false),
                    trigger = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    trigger_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    window_start_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    window_end_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    authoritative_event_id = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    authoritative_event_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    logical_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    evidence_snapshot_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    evidence_observed_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    plan_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    plan_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    plan_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    budget_snapshot_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    budget_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    policy_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    catalogue_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    authority_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    authority_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    originating_goal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    originating_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    orchestration_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    replay_of_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    replay_checkpoint_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    has_completed_effects = table.Column<bool>(type: "bit", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    terminal_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    sensitive_content_redacted_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    sensitive_content_redacted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_runs", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_runs_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_agent_orchestration_runs_orchestration_run_id",
                        column: x => x.orchestration_run_id,
                        principalTable: "agent_orchestration_runs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_agents_company_id_agent_id",
                        columns: x => new { x.company_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_company_goals_originating_goal_id",
                        column: x => x.originating_goal_id,
                        principalTable: "company_goals",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_finance_autonomy_grant_versions_company_id_grant_version_id",
                        columns: x => new { x.company_id, x.grant_version_id },
                        principalTable: "finance_autonomy_grant_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_finance_autonomy_grants_company_id_grant_id",
                        columns: x => new { x.company_id, x.grant_id },
                        principalTable: "finance_autonomy_grants",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_finance_autonomy_runs_replay_of_run_id",
                        column: x => x.replay_of_run_id,
                        principalTable: "finance_autonomy_runs",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_tasks_originating_task_id",
                        column: x => x.originating_task_id,
                        principalTable: "tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_runs_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_run_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    to_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    actor_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    occurred_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_run_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_run_history_finance_autonomy_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "finance_autonomy_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_run_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    safe_label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_run_sources", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_run_sources_finance_autonomy_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "finance_autonomy_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_run_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    step_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    action_class = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    tool_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    dependency_step_keys_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    maximum_attempts = table.Column<int>(type: "int", nullable: false),
                    tool_policy_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    authority_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    authority_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    requested_effect_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    requested_effect_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    actual_effect_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    actual_effect_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    actual_effect_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    work_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tool_execution_attempt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    lease_token = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    lease_expires_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_heartbeat_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    replay_permitted = table.Column<bool>(type: "bit", nullable: false),
                    replay_of_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_run_steps", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_run_steps_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_run_steps_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_run_steps_finance_autonomy_run_steps_replay_of_step_id",
                        column: x => x.replay_of_step_id,
                        principalTable: "finance_autonomy_run_steps",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_run_steps_finance_autonomy_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "finance_autonomy_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_run_steps_tasks_work_task_id",
                        column: x => x.work_task_id,
                        principalTable: "tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_finance_autonomy_run_steps_tool_executions_tool_execution_attempt_id",
                        column: x => x.tool_execution_attempt_id,
                        principalTable: "tool_executions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_step_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attempt_number = table.Column<int>(type: "int", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    lease_token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    policy_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    authority_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    authority_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    tool_execution_attempt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    started_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_step_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_step_attempts_finance_autonomy_run_steps_company_id_step_id",
                        columns: x => new { x.company_id, x.step_id },
                        principalTable: "finance_autonomy_run_steps",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_step_attempts_tool_executions_tool_execution_attempt_id",
                        column: x => x.tool_execution_attempt_id,
                        principalTable: "tool_executions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_history_company_id_run_id_occurred_utc",
                table: "finance_autonomy_run_history",
                columns: new[] { "company_id", "run_id", "occurred_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_sources_company_id_run_id_source_type_entity_type_entity_id_source_version",
                table: "finance_autonomy_run_sources",
                columns: new[] { "company_id", "run_id", "source_type", "entity_type", "entity_id", "source_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_steps_approval_request_id",
                table: "finance_autonomy_run_steps",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_steps_company_id_run_id_sequence",
                table: "finance_autonomy_run_steps",
                columns: new[] { "company_id", "run_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_steps_company_id_run_id_step_key",
                table: "finance_autonomy_run_steps",
                columns: new[] { "company_id", "run_id", "step_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_steps_company_id_status_lease_expires_utc_sequence",
                table: "finance_autonomy_run_steps",
                columns: new[] { "company_id", "status", "lease_expires_utc", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_steps_replay_of_step_id",
                table: "finance_autonomy_run_steps",
                column: "replay_of_step_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_steps_tool_execution_attempt_id",
                table: "finance_autonomy_run_steps",
                column: "tool_execution_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_run_steps_work_task_id",
                table: "finance_autonomy_run_steps",
                column: "work_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_company_id_agent_id_status_created_utc",
                table: "finance_autonomy_runs",
                columns: new[] { "company_id", "agent_id", "status", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_company_id_grant_id_grant_version_id",
                table: "finance_autonomy_runs",
                columns: new[] { "company_id", "grant_id", "grant_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_company_id_grant_version_id",
                table: "finance_autonomy_runs",
                columns: new[] { "company_id", "grant_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_company_id_logical_key",
                table: "finance_autonomy_runs",
                columns: new[] { "company_id", "logical_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_company_id_status_updated_utc",
                table: "finance_autonomy_runs",
                columns: new[] { "company_id", "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_company_id_window_start_utc_window_end_utc",
                table: "finance_autonomy_runs",
                columns: new[] { "company_id", "window_start_utc", "window_end_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_orchestration_run_id",
                table: "finance_autonomy_runs",
                column: "orchestration_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_originating_goal_id",
                table: "finance_autonomy_runs",
                column: "originating_goal_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_originating_task_id",
                table: "finance_autonomy_runs",
                column: "originating_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_replay_of_run_id",
                table: "finance_autonomy_runs",
                column: "replay_of_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_workflow_instance_id",
                table: "finance_autonomy_runs",
                column: "workflow_instance_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_step_attempts_company_id_step_id_attempt_number",
                table: "finance_autonomy_step_attempts",
                columns: new[] { "company_id", "step_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_step_attempts_tool_execution_attempt_id",
                table: "finance_autonomy_step_attempts",
                column: "tool_execution_attempt_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_autonomy_run_history");

            migrationBuilder.DropTable(
                name: "finance_autonomy_run_sources");

            migrationBuilder.DropTable(
                name: "finance_autonomy_step_attempts");

            migrationBuilder.DropTable(
                name: "finance_autonomy_run_steps");

            migrationBuilder.DropTable(
                name: "finance_autonomy_runs");
        }
    }
}
