using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableFinanceConversationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_conversation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiating_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    delegation_authority_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    request_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    effective_authority_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    effective_authority_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    planning_context_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    planning_context_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    safe_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    final_outcome_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    superseded_by_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    max_attempts = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    retain_until_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    redacted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_conversation_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_conversation_runs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_conversation_run_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    revision_no = table.Column<int>(type: "int", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    planning_context_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    evidence_references_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_conversation_run_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_conversation_run_revisions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_finance_conversation_run_revisions_finance_conversation_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "finance_conversation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_conversation_run_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    step_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    sequence_no = table.Column<int>(type: "int", nullable: false),
                    dependencies_json = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    tool_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tool_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    scope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    normalized_arguments_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    normalized_arguments_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expected_effect = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_references_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    result_summary_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: true),
                    policy_decision_summary_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: true),
                    business_idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tool_execution_attempt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    confirmation_payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    confirmation_target_snapshot_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    confirmation_authority_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    confirmation_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    max_attempts = table.Column<int>(type: "int", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_failure_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    redacted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_conversation_run_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_conversation_run_steps_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_finance_conversation_run_steps_finance_conversation_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "finance_conversation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_conversation_run_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attempt_no = table.Column<int>(type: "int", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tool_execution_attempt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_conversation_run_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_conversation_run_attempts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_finance_conversation_run_attempts_finance_conversation_run_steps_run_step_id",
                        column: x => x.run_step_id,
                        principalTable: "finance_conversation_run_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_attempts_company_id_started_at",
                table: "finance_conversation_run_attempts",
                columns: new[] { "company_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_attempts_run_step_id_attempt_no",
                table: "finance_conversation_run_attempts",
                columns: new[] { "run_step_id", "attempt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_revisions_company_id_created_at",
                table: "finance_conversation_run_revisions",
                columns: new[] { "company_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_revisions_run_id_revision_no",
                table: "finance_conversation_run_revisions",
                columns: new[] { "run_id", "revision_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_steps_approval_request_id",
                table: "finance_conversation_run_steps",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_steps_company_id_business_idempotency_key",
                table: "finance_conversation_run_steps",
                columns: new[] { "company_id", "business_idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_steps_run_id_step_key",
                table: "finance_conversation_run_steps",
                columns: new[] { "run_id", "step_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_steps_status_next_attempt_at_lease_expires_at",
                table: "finance_conversation_run_steps",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_run_steps_tool_execution_attempt_id",
                table: "finance_conversation_run_steps",
                column: "tool_execution_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_runs_company_id_agent_id_created_at",
                table: "finance_conversation_runs",
                columns: new[] { "company_id", "agent_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_runs_company_id_agent_id_idempotency_key",
                table: "finance_conversation_runs",
                columns: new[] { "company_id", "agent_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_runs_correlation_id",
                table: "finance_conversation_runs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_runs_retain_until_at_redacted_at",
                table: "finance_conversation_runs",
                columns: new[] { "retain_until_at", "redacted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_conversation_runs_status_next_attempt_at_lease_expires_at",
                table: "finance_conversation_runs",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_conversation_run_attempts");

            migrationBuilder.DropTable(
                name: "finance_conversation_run_revisions");

            migrationBuilder.DropTable(
                name: "finance_conversation_run_steps");

            migrationBuilder.DropTable(
                name: "finance_conversation_runs");
        }
    }
}
