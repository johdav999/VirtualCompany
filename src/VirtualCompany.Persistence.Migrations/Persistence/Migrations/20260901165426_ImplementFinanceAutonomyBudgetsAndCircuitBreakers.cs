using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementFinanceAutonomyBudgetsAndCircuitBreakers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_autonomy_budget_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    capability_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    scope_key = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    timezone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    window_minutes = table.Column<int>(type: "int", nullable: false),
                    per_run_records_evaluated = table.Column<int>(type: "int", nullable: true),
                    per_run_drafts_tasks_created = table.Column<int>(type: "int", nullable: true),
                    per_run_execute_attempts = table.Column<int>(type: "int", nullable: true),
                    per_run_amount_exposure = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    per_run_object_bytes = table.Column<long>(type: "bigint", nullable: true),
                    per_run_exports_created = table.Column<int>(type: "int", nullable: true),
                    per_run_model_calls = table.Column<int>(type: "int", nullable: true),
                    per_run_tool_calls = table.Column<int>(type: "int", nullable: true),
                    per_run_estimated_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    per_run_retries = table.Column<int>(type: "int", nullable: true),
                    per_run_runtime_seconds = table.Column<int>(type: "int", nullable: true),
                    window_limit_records_evaluated = table.Column<int>(type: "int", nullable: true),
                    window_limit_drafts_tasks_created = table.Column<int>(type: "int", nullable: true),
                    window_limit_execute_attempts = table.Column<int>(type: "int", nullable: true),
                    window_limit_amount_exposure = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    window_limit_object_bytes = table.Column<long>(type: "bigint", nullable: true),
                    window_limit_exports_created = table.Column<int>(type: "int", nullable: true),
                    window_limit_model_calls = table.Column<int>(type: "int", nullable: true),
                    window_limit_tool_calls = table.Column<int>(type: "int", nullable: true),
                    window_limit_estimated_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    window_limit_retries = table.Column<int>(type: "int", nullable: true),
                    window_limit_runtime_seconds = table.Column<int>(type: "int", nullable: true),
                    policy_denial_threshold = table.Column<int>(type: "int", nullable: false),
                    invalid_plan_threshold = table.Column<int>(type: "int", nullable: false),
                    provider_ambiguity_threshold = table.Column<int>(type: "int", nullable: false),
                    error_burst_threshold = table.Column<int>(type: "int", nullable: false),
                    stale_evidence_threshold = table.Column<int>(type: "int", nullable: false),
                    audit_outbox_failure_threshold = table.Column<int>(type: "int", nullable: false),
                    circuit_window_minutes = table.Column<int>(type: "int", nullable: false),
                    circuit_cooldown_minutes = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_budget_policies", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_budget_policies_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_budget_policies_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_circuit_breakers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    capability_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    scope_key = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    window_start_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    window_end_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    policy_denials = table.Column<int>(type: "int", nullable: false),
                    invalid_plans = table.Column<int>(type: "int", nullable: false),
                    provider_ambiguities = table.Column<int>(type: "int", nullable: false),
                    errors = table.Column<int>(type: "int", nullable: false),
                    stale_evidence = table.Column<int>(type: "int", nullable: false),
                    audit_outbox_failures = table.Column<int>(type: "int", nullable: false),
                    open_reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    opened_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cooldown_until_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_circuit_breakers", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_circuit_breakers_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_circuit_breakers_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_budget_windows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    window_start_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    window_end_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    reserved_records_evaluated = table.Column<int>(type: "int", nullable: false),
                    reserved_drafts_tasks_created = table.Column<int>(type: "int", nullable: false),
                    reserved_execute_attempts = table.Column<int>(type: "int", nullable: false),
                    reserved_amount_exposure = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    reserved_object_bytes = table.Column<long>(type: "bigint", nullable: false),
                    reserved_exports_created = table.Column<int>(type: "int", nullable: false),
                    reserved_model_calls = table.Column<int>(type: "int", nullable: false),
                    reserved_tool_calls = table.Column<int>(type: "int", nullable: false),
                    reserved_estimated_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    reserved_retries = table.Column<int>(type: "int", nullable: false),
                    reserved_runtime_seconds = table.Column<int>(type: "int", nullable: false),
                    consumed_records_evaluated = table.Column<int>(type: "int", nullable: false),
                    consumed_drafts_tasks_created = table.Column<int>(type: "int", nullable: false),
                    consumed_execute_attempts = table.Column<int>(type: "int", nullable: false),
                    consumed_amount_exposure = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    consumed_object_bytes = table.Column<long>(type: "bigint", nullable: false),
                    consumed_exports_created = table.Column<int>(type: "int", nullable: false),
                    consumed_model_calls = table.Column<int>(type: "int", nullable: false),
                    consumed_tool_calls = table.Column<int>(type: "int", nullable: false),
                    consumed_estimated_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    consumed_retries = table.Column<int>(type: "int", nullable: false),
                    consumed_runtime_seconds = table.Column<int>(type: "int", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_budget_windows", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_budget_windows_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_budget_windows_finance_autonomy_budget_policies_company_id_policy_id",
                        columns: x => new { x.company_id, x.policy_id },
                        principalTable: "finance_autonomy_budget_policies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_budget_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    circuit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_budget_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_budget_alerts_finance_autonomy_circuit_breakers_company_id_circuit_id",
                        columns: x => new { x.company_id, x.circuit_id },
                        principalTable: "finance_autonomy_circuit_breakers",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_budget_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    window_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attempt_number = table.Column<int>(type: "int", nullable: false),
                    reservation_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    planned_records_evaluated = table.Column<int>(type: "int", nullable: false),
                    planned_drafts_tasks_created = table.Column<int>(type: "int", nullable: false),
                    planned_execute_attempts = table.Column<int>(type: "int", nullable: false),
                    planned_amount_exposure = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    planned_object_bytes = table.Column<long>(type: "bigint", nullable: false),
                    planned_exports_created = table.Column<int>(type: "int", nullable: false),
                    planned_model_calls = table.Column<int>(type: "int", nullable: false),
                    planned_tool_calls = table.Column<int>(type: "int", nullable: false),
                    planned_estimated_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    planned_retries = table.Column<int>(type: "int", nullable: false),
                    planned_runtime_seconds = table.Column<int>(type: "int", nullable: false),
                    actual_records_evaluated = table.Column<int>(type: "int", nullable: false),
                    actual_drafts_tasks_created = table.Column<int>(type: "int", nullable: false),
                    actual_execute_attempts = table.Column<int>(type: "int", nullable: false),
                    actual_amount_exposure = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    actual_object_bytes = table.Column<long>(type: "bigint", nullable: false),
                    actual_exports_created = table.Column<int>(type: "int", nullable: false),
                    actual_model_calls = table.Column<int>(type: "int", nullable: false),
                    actual_tool_calls = table.Column<int>(type: "int", nullable: false),
                    actual_estimated_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    actual_retries = table.Column<int>(type: "int", nullable: false),
                    actual_runtime_seconds = table.Column<int>(type: "int", nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    reconciled_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_budget_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_budget_reservations_finance_autonomy_budget_policies_company_id_policy_id",
                        columns: x => new { x.company_id, x.policy_id },
                        principalTable: "finance_autonomy_budget_policies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_budget_reservations_finance_autonomy_budget_windows_company_id_window_id",
                        columns: x => new { x.company_id, x.window_id },
                        principalTable: "finance_autonomy_budget_windows",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_budget_reservations_finance_autonomy_run_steps_company_id_step_id",
                        columns: x => new { x.company_id, x.step_id },
                        principalTable: "finance_autonomy_run_steps",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_budget_reservations_finance_autonomy_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "finance_autonomy_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_alerts_company_id_circuit_id_status",
                table: "finance_autonomy_budget_alerts",
                columns: new[] { "company_id", "circuit_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_alerts_company_id_status_created_utc",
                table: "finance_autonomy_budget_alerts",
                columns: new[] { "company_id", "status", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_policies_company_id_is_active_agent_id_capability_id",
                table: "finance_autonomy_budget_policies",
                columns: new[] { "company_id", "is_active", "agent_id", "capability_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_policies_company_id_scope_key",
                table: "finance_autonomy_budget_policies",
                columns: new[] { "company_id", "scope_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_reservations_company_id_policy_id",
                table: "finance_autonomy_budget_reservations",
                columns: new[] { "company_id", "policy_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_reservations_company_id_reservation_key_policy_id",
                table: "finance_autonomy_budget_reservations",
                columns: new[] { "company_id", "reservation_key", "policy_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_reservations_company_id_run_id",
                table: "finance_autonomy_budget_reservations",
                columns: new[] { "company_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_reservations_company_id_status_created_utc",
                table: "finance_autonomy_budget_reservations",
                columns: new[] { "company_id", "status", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_reservations_company_id_step_id",
                table: "finance_autonomy_budget_reservations",
                columns: new[] { "company_id", "step_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_reservations_company_id_window_id",
                table: "finance_autonomy_budget_reservations",
                columns: new[] { "company_id", "window_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_windows_company_id_policy_id_window_start_utc_window_end_utc",
                table: "finance_autonomy_budget_windows",
                columns: new[] { "company_id", "policy_id", "window_start_utc", "window_end_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_budget_windows_company_id_window_end_utc_updated_utc",
                table: "finance_autonomy_budget_windows",
                columns: new[] { "company_id", "window_end_utc", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_circuit_breakers_company_id_scope_key",
                table: "finance_autonomy_circuit_breakers",
                columns: new[] { "company_id", "scope_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_circuit_breakers_company_id_status_updated_utc",
                table: "finance_autonomy_circuit_breakers",
                columns: new[] { "company_id", "status", "updated_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_autonomy_budget_alerts");

            migrationBuilder.DropTable(
                name: "finance_autonomy_budget_reservations");

            migrationBuilder.DropTable(
                name: "finance_autonomy_circuit_breakers");

            migrationBuilder.DropTable(
                name: "finance_autonomy_budget_windows");

            migrationBuilder.DropTable(
                name: "finance_autonomy_budget_policies");
        }
    }
}
