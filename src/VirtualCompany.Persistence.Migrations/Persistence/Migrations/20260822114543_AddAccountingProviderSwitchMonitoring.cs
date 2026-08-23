using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_monitoring_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    activation_execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    window_days = table.Column<int>(type: "int", nullable: false),
                    assigned_owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_owner_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    check_sequence = table.Column<int>(type: "int", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    consecutive_failure_count = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    window_ends_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_check_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_successful_check_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_run_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    closure_approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    closure_evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    closure_decision = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    closure_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    corrective_switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    closed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_monitoring_runs", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_monitoring_runs_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_provider_switch_monitoring_status", "[status] IN ('active','attention_required','closure_awaiting_approval','closed','failed')");
                    table.CheckConstraint("CK_provider_switch_monitoring_window", "[window_days] BETWEEN 7 AND 30");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_monitoring_runs_accounting_provider_switch_cutover_executions_company_id_switch_id_activation_exe~",
                        columns: x => new { x.company_id, x.switch_id, x.activation_execution_id },
                        principalTable: "accounting_provider_switch_cutover_executions",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_monitoring_runs_accounting_provider_switches_company_id_corrective_switch_id",
                        columns: x => new { x.company_id, x.corrective_switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_monitoring_runs_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_monitoring_runs_approval_requests_company_id_closure_approval_request_id",
                        columns: x => new { x.company_id, x.closure_approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_monitoring_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    monitoring_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    check_sequence = table.Column<int>(type: "int", nullable: false),
                    check_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    is_blocking = table.Column<bool>(type: "bit", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_monitoring_checks", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_monitoring_checks_accounting_provider_switch_monitoring_runs_company_id_monitoring_run_id",
                        columns: x => new { x.company_id, x.monitoring_run_id },
                        principalTable: "accounting_provider_switch_monitoring_runs",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_monitoring_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    monitoring_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    check_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    is_blocking = table.Column<bool>(type: "bit", nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurrence_count = table.Column<int>(type: "int", nullable: false),
                    first_observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    exception_explanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    exception_scope = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    financial_impact = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    evidence_reference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    accepted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_monitoring_incidents", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_monitoring_incidents_accounting_provider_switch_monitoring_runs_company_id_monitoring_run_id",
                        columns: x => new { x.company_id, x.monitoring_run_id },
                        principalTable: "accounting_provider_switch_monitoring_runs",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_monitoring_incidents_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_checks_company_id_monitoring_run_id_check_sequence_check_key",
                table: "accounting_provider_switch_monitoring_checks",
                columns: new[] { "company_id", "monitoring_run_id", "check_sequence", "check_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_incidents_company_id_monitoring_run_id_fingerprint",
                table: "accounting_provider_switch_monitoring_incidents",
                columns: new[] { "company_id", "monitoring_run_id", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_incidents_company_id_status_is_blocking",
                table: "accounting_provider_switch_monitoring_incidents",
                columns: new[] { "company_id", "status", "is_blocking" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_incidents_task_id",
                table: "accounting_provider_switch_monitoring_incidents",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_runs_company_id_closure_approval_request_id",
                table: "accounting_provider_switch_monitoring_runs",
                columns: new[] { "company_id", "closure_approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_runs_company_id_corrective_switch_id",
                table: "accounting_provider_switch_monitoring_runs",
                columns: new[] { "company_id", "corrective_switch_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_runs_company_id_switch_id",
                table: "accounting_provider_switch_monitoring_runs",
                columns: new[] { "company_id", "switch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_runs_company_id_switch_id_activation_execution_id",
                table: "accounting_provider_switch_monitoring_runs",
                columns: new[] { "company_id", "switch_id", "activation_execution_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_monitoring_runs_status_next_run_at_lease_expires_at",
                table: "accounting_provider_switch_monitoring_runs",
                columns: new[] { "status", "next_run_at", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switch_monitoring_checks");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_monitoring_incidents");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_monitoring_runs");
        }
    }
}
