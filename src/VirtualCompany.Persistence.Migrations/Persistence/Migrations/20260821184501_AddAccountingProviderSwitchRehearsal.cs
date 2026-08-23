using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchRehearsal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_rehearsals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    simulation_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    provider_acceptance_proven = table.Column<bool>(type: "bit", nullable: false),
                    disclosure = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    completed_work_items = table.Column<int>(type: "int", nullable: false),
                    total_work_items = table.Column<int>(type: "int", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_rehearsals", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_rehearsals_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switch_rehearsals_status", "[status] IN ('queued','running','completed','failed')");
                    table.CheckConstraint("CK_accounting_provider_switch_rehearsals_version", "[version] > 0");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_rehearsals_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_rehearsals_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_cutover_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rehearsal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_version = table.Column<int>(type: "int", nullable: false),
                    plan_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    source_snapshot_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    strategy = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    freeze_starts_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    freeze_ends_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    recovery_boundary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    participants_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 32000, nullable: false),
                    generated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_cutover_plans", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_cutover_plans_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_cutover_plans_accounting_provider_switch_rehearsals_company_id_switch_id_rehearsal_id",
                        columns: x => new { x.company_id, x.switch_id, x.rehearsal_id },
                        principalTable: "accounting_provider_switch_rehearsals",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_cutover_plans_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_reconciliation_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rehearsal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    check_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    expected_value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    observed_value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    tolerance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    currency_key = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    result = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    data_sources_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    calculation_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    manual_evidence_allowed = table.Column<bool>(type: "bit", nullable: false),
                    calculated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_reconciliation_checks", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_reconciliation_checks_accounting_provider_switch_rehearsals_company_id_switch_id_rehearsal_id",
                        columns: x => new { x.company_id, x.switch_id, x.rehearsal_id },
                        principalTable: "accounting_provider_switch_rehearsals",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_rehearsal_datasets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rehearsal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dataset = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expected_count = table.Column<long>(type: "bigint", nullable: false),
                    observed_count = table.Column<long>(type: "bigint", nullable: false),
                    expected_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    observed_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    currency_key = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    result = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    calculated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_rehearsal_datasets", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_rehearsal_datasets_accounting_provider_switch_rehearsals_company_id_switch_id_rehearsal_id",
                        columns: x => new { x.company_id, x.switch_id, x.rehearsal_id },
                        principalTable: "accounting_provider_switch_rehearsals",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_rehearsal_inputs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rehearsal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_version = table.Column<long>(type: "bigint", nullable: false),
                    strategy = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    source_snapshot_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    staging_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    mapping_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    gap_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    staged_record_count = table.Column<long>(type: "bigint", nullable: false),
                    financial_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    dataset_summary_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_rehearsal_inputs", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_rehearsal_inputs_accounting_provider_switch_rehearsals_company_id_switch_id_rehearsal_id",
                        columns: x => new { x.company_id, x.switch_id, x.rehearsal_id },
                        principalTable: "accounting_provider_switch_rehearsals",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_plan_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_plan_approvals", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_plan_approvals_accounting_provider_switch_cutover_plans_company_id_switch_id_plan_id",
                        columns: x => new { x.company_id, x.switch_id, x.plan_id },
                        principalTable: "accounting_provider_switch_cutover_plans",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_plan_approvals_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_manual_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rehearsal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    check_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    input_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_reference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_manual_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_manual_evidence_accounting_provider_switch_reconciliation_checks_check_id",
                        column: x => x.check_id,
                        principalTable: "accounting_provider_switch_reconciliation_checks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_manual_evidence_accounting_provider_switch_rehearsals_company_id_switch_id_rehearsal_id",
                        columns: x => new { x.company_id, x.switch_id, x.rehearsal_id },
                        principalTable: "accounting_provider_switch_rehearsals",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_plans_company_id_switch_id_plan_hash",
                table: "accounting_provider_switch_cutover_plans",
                columns: new[] { "company_id", "switch_id", "plan_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_plans_company_id_switch_id_plan_version",
                table: "accounting_provider_switch_cutover_plans",
                columns: new[] { "company_id", "switch_id", "plan_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_cutover_plans_company_id_switch_id_rehearsal_id",
                table: "accounting_provider_switch_cutover_plans",
                columns: new[] { "company_id", "switch_id", "rehearsal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_manual_evidence_check_id",
                table: "accounting_provider_switch_manual_evidence",
                column: "check_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_manual_evidence_company_id_switch_id_rehearsal_id_check_id_input_hash",
                table: "accounting_provider_switch_manual_evidence",
                columns: new[] { "company_id", "switch_id", "rehearsal_id", "check_id", "input_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_plan_approvals_approval_request_id",
                table: "accounting_provider_switch_plan_approvals",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_plan_approvals_company_id_approval_request_id",
                table: "accounting_provider_switch_plan_approvals",
                columns: new[] { "company_id", "approval_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_plan_approvals_company_id_plan_id",
                table: "accounting_provider_switch_plan_approvals",
                columns: new[] { "company_id", "plan_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_plan_approvals_company_id_switch_id_plan_id",
                table: "accounting_provider_switch_plan_approvals",
                columns: new[] { "company_id", "switch_id", "plan_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_reconciliation_checks_company_id_switch_id_rehearsal_id_check_key_currency_key",
                table: "accounting_provider_switch_reconciliation_checks",
                columns: new[] { "company_id", "switch_id", "rehearsal_id", "check_key", "currency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_rehearsal_datasets_company_id_switch_id_rehearsal_id_dataset_currency_key",
                table: "accounting_provider_switch_rehearsal_datasets",
                columns: new[] { "company_id", "switch_id", "rehearsal_id", "dataset", "currency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_rehearsal_inputs_company_id_switch_id_rehearsal_id",
                table: "accounting_provider_switch_rehearsal_inputs",
                columns: new[] { "company_id", "switch_id", "rehearsal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_rehearsals_company_id_switch_id_idempotency_key",
                table: "accounting_provider_switch_rehearsals",
                columns: new[] { "company_id", "switch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_rehearsals_status_next_attempt_at_lease_expires_at",
                table: "accounting_provider_switch_rehearsals",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switch_manual_evidence");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_plan_approvals");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_rehearsal_datasets");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_rehearsal_inputs");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_reconciliation_checks");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_cutover_plans");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_rehearsals");
        }
    }
}
