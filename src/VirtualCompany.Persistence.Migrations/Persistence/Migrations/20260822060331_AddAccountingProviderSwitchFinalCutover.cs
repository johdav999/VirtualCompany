using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchFinalCutover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "command_type",
                table: "accounting_provider_switch_target_transfer_items",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "http_method",
                table: "accounting_provider_switch_target_transfer_items",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "accounting_provider_switch_target_transfer_items",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_payload_type",
                table: "accounting_provider_switch_target_transfer_items",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sanitized_payload_json",
                table: "accounting_provider_switch_target_transfer_items",
                type: "nvarchar(max)",
                maxLength: 64000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_cutover_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_version = table.Column<int>(type: "int", nullable: false),
                    plan_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    preparation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    target_transfer_batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    final_snapshot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    authority_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    current_step = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    target_activity_recorded = table.Column<bool>(type: "bit", nullable: false),
                    retry_is_safe = table.Column<bool>(type: "bit", nullable: false),
                    provider_reconciliation_required = table.Column<bool>(type: "bit", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    next_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    freeze_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reconciled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    activated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_cutover_executions", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_cutover_executions_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("ck_accounting_provider_switch_cutover_executions_attempt_count", "[attempt_count] >= 0");
                    table.CheckConstraint("ck_accounting_provider_switch_cutover_executions_status", "[status] IN ('queued','freezing','transferring','reconciling','awaiting_activation_approval','activating','activated','blocked','cancelled','recovered','corrective_cutover_required')");
                    table.CheckConstraint("ck_accounting_provider_switch_cutover_executions_version", "[version] >= 1");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_cutover_executions_accounting_authority_periods_authority_period_id",
                        column: x => x.authority_period_id,
                        principalTable: "accounting_authority_periods",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_cutover_executions_accounting_provider_switch_cutover_plans_company_id_switch_id_plan_id",
                        columns: x => new { x.company_id, x.switch_id, x.plan_id },
                        principalTable: "accounting_provider_switch_cutover_plans",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_cutover_executions_accounting_provider_switch_preparations_preparation_id",
                        column: x => x.preparation_id,
                        principalTable: "accounting_provider_switch_preparations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_cutover_executions_accounting_provider_switch_target_transfer_batches_target_transfer_batch_id",
                        column: x => x.target_transfer_batch_id,
                        principalTable: "accounting_provider_switch_target_transfer_batches",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_cutover_executions_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_activation_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    final_snapshot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    final_snapshot_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    reconciliation_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    switch_version = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_activation_approvals", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_activation_approvals_accounting_provider_switch_cutover_executions_company_id_switch_id_execution~",
                        columns: x => new { x.company_id, x.switch_id, x.execution_id },
                        principalTable: "accounting_provider_switch_cutover_executions",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_activation_approvals_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_final_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    check_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    result = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    calculated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_final_checks", x => x.id);
                    table.CheckConstraint("ck_accounting_provider_switch_final_checks_result", "[result] IN ('passed','failed')");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_final_checks_accounting_provider_switch_cutover_executions_company_id_switch_id_execution_id",
                        columns: x => new { x.company_id, x.switch_id, x.execution_id },
                        principalTable: "accounting_provider_switch_cutover_executions",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_final_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approved_source_snapshot_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    final_source_snapshot_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    staging_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    mapping_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    gap_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    record_count = table.Column<long>(type: "bigint", nullable: false),
                    financial_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    delta_record_count = table.Column<long>(type: "bigint", nullable: false),
                    delta_financial_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 64000, nullable: false),
                    extraction_started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    extraction_completed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_final_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_final_snapshots_accounting_provider_switch_cutover_executions_company_id_switch_id_execution_id",
                        columns: x => new { x.company_id, x.switch_id, x.execution_id },
                        principalTable: "accounting_provider_switch_cutover_executions",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_native_materializations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    candidate_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    target_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_record_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    materialized_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_native_materializations", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_native_materializations_accounting_provider_switch_cutover_executions_company_id_switch_id_execut~",
                        columns: x => new { x.company_id, x.switch_id, x.execution_id },
                        principalTable: "accounting_provider_switch_cutover_executions",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_native_materializations_accounting_provider_switch_native_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "accounting_provider_switch_native_candidates",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_activation_approvals_approval_request_id",
                table: "accounting_provider_switch_activation_approvals",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_activation_approvals_company_id_approval_request_id",
                table: "accounting_provider_switch_activation_approvals",
                columns: new[] { "company_id", "approval_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_activation_approvals_company_id_execution_id",
                table: "accounting_provider_switch_activation_approvals",
                columns: new[] { "company_id", "execution_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_activation_approvals_company_id_switch_id_execution_id",
                table: "accounting_provider_switch_activation_approvals",
                columns: new[] { "company_id", "switch_id", "execution_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_executions_authority_period_id",
                table: "accounting_provider_switch_cutover_executions",
                column: "authority_period_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_executions_company_id_switch_id",
                table: "accounting_provider_switch_cutover_executions",
                columns: new[] { "company_id", "switch_id" },
                unique: true,
                filter: "[status] <> 'activated' AND [status] <> 'cancelled' AND [status] <> 'recovered' AND [status] <> 'corrective_cutover_required'");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_executions_company_id_switch_id_idempotency_key",
                table: "accounting_provider_switch_cutover_executions",
                columns: new[] { "company_id", "switch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_executions_company_id_switch_id_plan_id",
                table: "accounting_provider_switch_cutover_executions",
                columns: new[] { "company_id", "switch_id", "plan_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_executions_preparation_id",
                table: "accounting_provider_switch_cutover_executions",
                column: "preparation_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_executions_status_next_attempt_at_lease_expires_at",
                table: "accounting_provider_switch_cutover_executions",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_executions_target_transfer_batch_id",
                table: "accounting_provider_switch_cutover_executions",
                column: "target_transfer_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_final_checks_company_id_switch_id_execution_id_check_key",
                table: "accounting_provider_switch_final_checks",
                columns: new[] { "company_id", "switch_id", "execution_id", "check_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_final_snapshots_company_id_switch_id_execution_id",
                table: "accounting_provider_switch_final_snapshots",
                columns: new[] { "company_id", "switch_id", "execution_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_native_materializations_candidate_id",
                table: "accounting_provider_switch_native_materializations",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_native_materializations_company_id_switch_id_candidate_id",
                table: "accounting_provider_switch_native_materializations",
                columns: new[] { "company_id", "switch_id", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_native_materializations_company_id_switch_id_execution_id",
                table: "accounting_provider_switch_native_materializations",
                columns: new[] { "company_id", "switch_id", "execution_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switch_activation_approvals");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_final_checks");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_final_snapshots");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_native_materializations");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_cutover_executions");

            migrationBuilder.DropColumn(
                name: "command_type",
                table: "accounting_provider_switch_target_transfer_items");

            migrationBuilder.DropColumn(
                name: "http_method",
                table: "accounting_provider_switch_target_transfer_items");

            migrationBuilder.DropColumn(
                name: "path",
                table: "accounting_provider_switch_target_transfer_items");

            migrationBuilder.DropColumn(
                name: "provider_payload_type",
                table: "accounting_provider_switch_target_transfer_items");

            migrationBuilder.DropColumn(
                name: "sanitized_payload_json",
                table: "accounting_provider_switch_target_transfer_items");
        }
    }
}
