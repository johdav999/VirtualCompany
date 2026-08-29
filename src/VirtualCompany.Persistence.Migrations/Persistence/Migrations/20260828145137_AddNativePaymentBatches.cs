using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNativePaymentBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    planned_execution_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    instruction_set_version = table.Column<int>(type: "int", nullable: false),
                    current_validation_result_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    current_export_artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rejected_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decision_comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    create_idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    create_payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batches", x => x.id);
                    table.UniqueConstraint("AK_payment_batches_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_payment_batches_status", "status IN ('draft', 'validated', 'awaiting_approval', 'approved', 'rejected', 'cancelled')");
                    table.ForeignKey(
                        name: "FK_payment_batches_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_beneficiary_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    party_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    party_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    rail = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    destination = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    masked_destination = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    is_current = table.Column<bool>(type: "bit", nullable: false),
                    verification_evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    verification_evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    verified_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    verified_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_beneficiary_profiles", x => x.id);
                    table.UniqueConstraint("AK_payment_beneficiary_profiles_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_payment_beneficiary_profiles_status", "status IN ('verified', 'superseded', 'revoked')");
                    table.ForeignKey(
                        name: "FK_payment_beneficiary_profiles_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_approval_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instruction_set_version = table.Column<int>(type: "int", nullable: false),
                    source_set_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decision_comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_approval_bindings", x => x.id);
                    table.CheckConstraint("CK_payment_batch_approval_bindings_status", "status IN ('pending', 'approved', 'rejected', 'cancelled', 'stale')");
                    table.ForeignKey(
                        name: "FK_payment_batch_approval_bindings_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_payment_batch_approval_bindings_payment_batches_company_id_batch_id",
                        columns: x => new { x.company_id, x.batch_id },
                        principalTable: "payment_batches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_export_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instruction_set_version = table.Column<int>(type: "int", nullable: false),
                    format = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    mime_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    is_current = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_export_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_batch_export_artifacts_payment_batches_company_id_batch_id",
                        columns: x => new { x.company_id, x.batch_id },
                        principalTable: "payment_batches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_obligations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    obligation_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    removed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    removed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_obligations", x => x.id);
                    table.UniqueConstraint("AK_payment_batch_obligations_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_payment_batch_obligations_type", "obligation_type IN ('supplier_payment_proposal', 'customer_refund')");
                    table.ForeignKey(
                        name: "FK_payment_batch_obligations_payment_batches_company_id_batch_id",
                        columns: x => new { x.company_id, x.batch_id },
                        principalTable: "payment_batches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operation_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    result_batch_version = table.Column<long>(type: "bigint", nullable: false),
                    result_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_batch_operations_payment_batches_company_id_batch_id",
                        columns: x => new { x.company_id, x.batch_id },
                        principalTable: "payment_batches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_validation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    evaluated_batch_version = table.Column<long>(type: "bigint", nullable: false),
                    instruction_set_version = table.Column<int>(type: "int", nullable: false),
                    is_valid = table.Column<bool>(type: "bit", nullable: false),
                    source_set_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    totals_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    cash_availability_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    validated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_validation_results", x => x.id);
                    table.UniqueConstraint("AK_payment_batch_validation_results_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_payment_batch_validation_results_payment_batches_company_id_batch_id",
                        columns: x => new { x.company_id, x.batch_id },
                        principalTable: "payment_batches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_beneficiary_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    obligation_link_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    profile_version = table.Column<int>(type: "int", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    rail = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    destination = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    masked_destination = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    verification_evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    verification_evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    verified_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_beneficiary_snapshots", x => x.id);
                    table.UniqueConstraint("AK_payment_beneficiary_snapshots_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_payment_beneficiary_snapshots_payment_batch_obligations_company_id_obligation_link_id",
                        columns: x => new { x.company_id, x.obligation_link_id },
                        principalTable: "payment_batch_obligations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_instructions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    obligation_link_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instruction_set_version = table.Column<int>(type: "int", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    execution_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    payment_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    beneficiary_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    rail = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    destination = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    masked_destination = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    is_current = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    superseded_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_instructions", x => x.id);
                    table.UniqueConstraint("AK_payment_instructions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_payment_instructions_status", "status IN ('draft', 'approved', 'superseded')");
                    table.ForeignKey(
                        name: "FK_payment_instructions_payment_batch_obligations_company_id_obligation_link_id",
                        columns: x => new { x.company_id, x.obligation_link_id },
                        principalTable: "payment_batch_obligations",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_payment_instructions_payment_batches_company_id_batch_id",
                        columns: x => new { x.company_id, x.batch_id },
                        principalTable: "payment_batches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_batch_validation_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    validation_result_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    obligation_link_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_batch_validation_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_batch_validation_issues_payment_batch_validation_results_company_id_validation_result_id",
                        columns: x => new { x.company_id, x.validation_result_id },
                        principalTable: "payment_batch_validation_results",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_approval_bindings_company_id_approval_request_id",
                table: "payment_batch_approval_bindings",
                columns: new[] { "company_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_approval_bindings_company_id_batch_id_approval_request_id",
                table: "payment_batch_approval_bindings",
                columns: new[] { "company_id", "batch_id", "approval_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_approval_bindings_company_id_batch_id_status",
                table: "payment_batch_approval_bindings",
                columns: new[] { "company_id", "batch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_export_artifacts_company_id_batch_id_instruction_set_version",
                table: "payment_batch_export_artifacts",
                columns: new[] { "company_id", "batch_id", "instruction_set_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_export_artifacts_company_id_batch_id_is_current",
                table: "payment_batch_export_artifacts",
                columns: new[] { "company_id", "batch_id", "is_current" },
                unique: true,
                filter: "[is_current] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_obligations_company_id_batch_id_obligation_type_source_id",
                table: "payment_batch_obligations",
                columns: new[] { "company_id", "batch_id", "obligation_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_obligations_company_id_obligation_type_source_id_removed_at",
                table: "payment_batch_obligations",
                columns: new[] { "company_id", "obligation_type", "source_id", "removed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_operations_company_id_batch_id_created_at",
                table: "payment_batch_operations",
                columns: new[] { "company_id", "batch_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_operations_company_id_operation_type_idempotency_key",
                table: "payment_batch_operations",
                columns: new[] { "company_id", "operation_type", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_validation_issues_company_id_validation_result_id_reason_code",
                table: "payment_batch_validation_issues",
                columns: new[] { "company_id", "validation_result_id", "reason_code" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batch_validation_results_company_id_batch_id_created_at",
                table: "payment_batch_validation_results",
                columns: new[] { "company_id", "batch_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_batches_company_id_create_idempotency_key",
                table: "payment_batches",
                columns: new[] { "company_id", "create_idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batches_company_id_reference",
                table: "payment_batches",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_batches_company_id_status_updated_at",
                table: "payment_batches",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_beneficiary_profiles_company_id_party_type_party_id_is_current",
                table: "payment_beneficiary_profiles",
                columns: new[] { "company_id", "party_type", "party_id", "is_current" },
                unique: true,
                filter: "[is_current] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_payment_beneficiary_profiles_company_id_party_type_party_id_version",
                table: "payment_beneficiary_profiles",
                columns: new[] { "company_id", "party_type", "party_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_beneficiary_snapshots_company_id_obligation_link_id",
                table: "payment_beneficiary_snapshots",
                columns: new[] { "company_id", "obligation_link_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_beneficiary_snapshots_company_id_profile_id_profile_version",
                table: "payment_beneficiary_snapshots",
                columns: new[] { "company_id", "profile_id", "profile_version" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_instructions_company_id_batch_id_instruction_set_version_sequence",
                table: "payment_instructions",
                columns: new[] { "company_id", "batch_id", "instruction_set_version", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_instructions_company_id_batch_id_obligation_link_id_is_current",
                table: "payment_instructions",
                columns: new[] { "company_id", "batch_id", "obligation_link_id", "is_current" },
                unique: true,
                filter: "[is_current] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_payment_instructions_company_id_obligation_link_id",
                table: "payment_instructions",
                columns: new[] { "company_id", "obligation_link_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_batch_approval_bindings");

            migrationBuilder.DropTable(
                name: "payment_batch_export_artifacts");

            migrationBuilder.DropTable(
                name: "payment_batch_operations");

            migrationBuilder.DropTable(
                name: "payment_batch_validation_issues");

            migrationBuilder.DropTable(
                name: "payment_beneficiary_profiles");

            migrationBuilder.DropTable(
                name: "payment_beneficiary_snapshots");

            migrationBuilder.DropTable(
                name: "payment_instructions");

            migrationBuilder.DropTable(
                name: "payment_batch_validation_results");

            migrationBuilder.DropTable(
                name: "payment_batch_obligations");

            migrationBuilder.DropTable(
                name: "payment_batches");
        }
    }
}
