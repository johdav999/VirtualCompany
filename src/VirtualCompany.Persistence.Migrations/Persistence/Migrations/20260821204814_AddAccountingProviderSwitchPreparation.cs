using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_preparations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    strategy = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    completed_work_items = table.Column<int>(type: "int", nullable: false),
                    total_work_items = table.Column<int>(type: "int", nullable: false),
                    candidate_count = table.Column<int>(type: "int", nullable: false),
                    valid_candidate_count = table.Column<int>(type: "int", nullable: false),
                    rejected_candidate_count = table.Column<int>(type: "int", nullable: false),
                    existing_reference_count = table.Column<int>(type: "int", nullable: false),
                    archive_dependency_count = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_accounting_provider_switch_preparations", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_preparations_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switch_preparations_counts", "[completed_work_items] >= 0 AND [total_work_items] >= 0 AND [candidate_count] >= 0 AND [valid_candidate_count] >= 0 AND [rejected_candidate_count] >= 0 AND [existing_reference_count] >= 0 AND [archive_dependency_count] >= 0");
                    table.CheckConstraint("CK_accounting_provider_switch_preparations_status", "[status] IN ('queued','running','completed','failed')");
                    table.CheckConstraint("CK_accounting_provider_switch_preparations_version", "[version] > 0");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_preparations_accounting_provider_switch_cutover_plans_company_id_switch_id_plan_id",
                        columns: x => new { x.company_id, x.switch_id, x.plan_id },
                        principalTable: "accounting_provider_switch_cutover_plans",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_preparations_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_preparations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_archive_dependencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    prepared_by_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staged_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dataset = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    approved_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approved_plan_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_archive_dependencies", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_archive_dependencies_accounting_provider_switch_preparations_company_id_switch_id_prepared_by_run~",
                        columns: x => new { x.company_id, x.switch_id, x.prepared_by_run_id },
                        principalTable: "accounting_provider_switch_preparations",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_native_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    prepared_by_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staged_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    candidate_kind = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    source_dataset = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    financial_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    external_reference_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_native_candidates", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_native_candidates_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switch_native_candidates_status", "[status] IN ('valid','rejected')");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_native_candidates_accounting_provider_switch_preparations_company_id_switch_id_prepared_by_run_id",
                        columns: x => new { x.company_id, x.switch_id, x.prepared_by_run_id },
                        principalTable: "accounting_provider_switch_preparations",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_native_candidates_accounting_provider_switch_staged_records_company_id_switch_id_staged_record_id",
                        columns: x => new { x.company_id, x.switch_id, x.staged_record_id },
                        principalTable: "accounting_provider_switch_staged_records",
                        principalColumns: new[] { "company_id", "switch_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_readiness_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preparation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    check_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    is_ready = table.Column<bool>(type: "bit", nullable: false),
                    is_blocking = table.Column<bool>(type: "bit", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    calculated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_readiness_checks", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_readiness_checks_accounting_provider_switch_preparations_company_id_switch_id_preparation_id",
                        columns: x => new { x.company_id, x.switch_id, x.preparation_id },
                        principalTable: "accounting_provider_switch_preparations",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_candidate_validations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_blocking = table.Column<bool>(type: "bit", nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    validated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_candidate_validations", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_candidate_validations_accounting_provider_switch_native_candidates_company_id_switch_id_candidate~",
                        columns: x => new { x.company_id, x.switch_id, x.candidate_id },
                        principalTable: "accounting_provider_switch_native_candidates",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_archive_dependencies_company_id_switch_id_dataset_source_identity_reason_code",
                table: "accounting_provider_switch_archive_dependencies",
                columns: new[] { "company_id", "switch_id", "dataset", "source_identity", "reason_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_archive_dependencies_company_id_switch_id_prepared_by_run_id",
                table: "accounting_provider_switch_archive_dependencies",
                columns: new[] { "company_id", "switch_id", "prepared_by_run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_candidate_validations_company_id_switch_id_candidate_id_reason_code",
                table: "accounting_provider_switch_candidate_validations",
                columns: new[] { "company_id", "switch_id", "candidate_id", "reason_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_native_candidates_company_id_switch_id_idempotency_key",
                table: "accounting_provider_switch_native_candidates",
                columns: new[] { "company_id", "switch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_native_candidates_company_id_switch_id_prepared_by_run_id",
                table: "accounting_provider_switch_native_candidates",
                columns: new[] { "company_id", "switch_id", "prepared_by_run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_native_candidates_company_id_switch_id_staged_record_id_candidate_kind",
                table: "accounting_provider_switch_native_candidates",
                columns: new[] { "company_id", "switch_id", "staged_record_id", "candidate_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_native_candidates_company_id_switch_id_status_candidate_kind",
                table: "accounting_provider_switch_native_candidates",
                columns: new[] { "company_id", "switch_id", "status", "candidate_kind" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_preparations_company_id_switch_id_idempotency_key",
                table: "accounting_provider_switch_preparations",
                columns: new[] { "company_id", "switch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_preparations_company_id_switch_id_plan_hash",
                table: "accounting_provider_switch_preparations",
                columns: new[] { "company_id", "switch_id", "plan_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_preparations_company_id_switch_id_plan_id",
                table: "accounting_provider_switch_preparations",
                columns: new[] { "company_id", "switch_id", "plan_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_preparations_status_next_attempt_at_lease_expires_at",
                table: "accounting_provider_switch_preparations",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_readiness_checks_company_id_switch_id_preparation_id_check_key",
                table: "accounting_provider_switch_readiness_checks",
                columns: new[] { "company_id", "switch_id", "preparation_id", "check_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switch_archive_dependencies");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_candidate_validations");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_readiness_checks");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_native_candidates");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_preparations");
        }
    }
}
