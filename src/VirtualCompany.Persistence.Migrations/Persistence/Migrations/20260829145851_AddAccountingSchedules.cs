using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_schedule_approval_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    payload_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bound_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_approval_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_schedule_approval_bindings_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedule_evidence_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    content_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    linked_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_evidence_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_schedule_evidence_links_knowledge_documents_company_id_document_id",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedule_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    safe_next_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_exceptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedule_line_dimensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_line_dimensions", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounting_schedule_line_dimensions_accounting_dimension_members_company_id_dimension_member_id",
                        columns: x => new { x.company_id, x.dimension_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedule_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    debit_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_lines", x => x.id);
                    table.UniqueConstraint("AK_accounting_schedule_lines_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_schedule_lines_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedule_occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_version_number = table.Column<int>(type: "int", nullable: false),
                    schedule_version_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scheduled_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    released_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    reversed_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    reversal_rule = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    reversal_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reversal_ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    lease_expires_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    posted_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reversed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_occurrences", x => x.id);
                    table.UniqueConstraint("AK_accounting_schedule_occurrences_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedule_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    result_version = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedule_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    payload_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedule_versions", x => x.id);
                    table.UniqueConstraint("AK_accounting_schedule_versions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "accounting_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    schedule_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    cadence = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    amount_basis = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    proration_rule = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    occurrence_day = table.Column<int>(type: "int", nullable: false),
                    time_zone_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    voucher_series_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    reversal_rule = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    next_occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    current_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    current_version_number = table.Column<int>(type: "int", nullable: false),
                    current_version_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_version_number = table.Column<int>(type: "int", nullable: true),
                    approval_payload_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_schedules", x => x.id);
                    table.UniqueConstraint("AK_accounting_schedules_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_schedules_accounting_schedule_versions_company_id_current_version_id",
                        columns: x => new { x.company_id, x.current_version_id },
                        principalTable: "accounting_schedule_versions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_schedules_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_accounting_schedules_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_approval_bindings_company_id_approval_request_id",
                table: "accounting_schedule_approval_bindings",
                columns: new[] { "company_id", "approval_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_approval_bindings_company_id_schedule_id",
                table: "accounting_schedule_approval_bindings",
                columns: new[] { "company_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_approval_bindings_company_id_schedule_version_id",
                table: "accounting_schedule_approval_bindings",
                columns: new[] { "company_id", "schedule_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_evidence_links_company_id_document_id",
                table: "accounting_schedule_evidence_links",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_evidence_links_company_id_schedule_version_id_document_id",
                table: "accounting_schedule_evidence_links",
                columns: new[] { "company_id", "schedule_version_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_exceptions_company_id_occurrence_id",
                table: "accounting_schedule_exceptions",
                columns: new[] { "company_id", "occurrence_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_exceptions_company_id_schedule_id_status",
                table: "accounting_schedule_exceptions",
                columns: new[] { "company_id", "schedule_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_line_dimensions_company_id_dimension_member_id",
                table: "accounting_schedule_line_dimensions",
                columns: new[] { "company_id", "dimension_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_line_dimensions_company_id_schedule_line_id_dimension_member_id",
                table: "accounting_schedule_line_dimensions",
                columns: new[] { "company_id", "schedule_line_id", "dimension_member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_lines_company_id_finance_account_id",
                table: "accounting_schedule_lines",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_lines_company_id_schedule_version_id_sequence",
                table: "accounting_schedule_lines",
                columns: new[] { "company_id", "schedule_version_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_occurrences_company_id_ledger_entry_id",
                table: "accounting_schedule_occurrences",
                columns: new[] { "company_id", "ledger_entry_id" },
                unique: true,
                filter: "ledger_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_occurrences_company_id_schedule_id_occurrence_date",
                table: "accounting_schedule_occurrences",
                columns: new[] { "company_id", "schedule_id", "occurrence_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_occurrences_company_id_schedule_version_id",
                table: "accounting_schedule_occurrences",
                columns: new[] { "company_id", "schedule_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_occurrences_status_next_attempt_utc_lease_expires_utc",
                table: "accounting_schedule_occurrences",
                columns: new[] { "status", "next_attempt_utc", "lease_expires_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_occurrences_status_reversal_due_date_reversal_ledger_entry_id",
                table: "accounting_schedule_occurrences",
                columns: new[] { "status", "reversal_due_date", "reversal_ledger_entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_operations_company_id_idempotency_key",
                table: "accounting_schedule_operations",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_operations_company_id_schedule_id",
                table: "accounting_schedule_operations",
                columns: new[] { "company_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedule_versions_company_id_schedule_id_version_number",
                table: "accounting_schedule_versions",
                columns: new[] { "company_id", "schedule_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedules_company_id_approval_request_id",
                table: "accounting_schedules",
                columns: new[] { "company_id", "approval_request_id" },
                unique: true,
                filter: "approval_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedules_company_id_code",
                table: "accounting_schedules",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedules_company_id_current_version_id",
                table: "accounting_schedules",
                columns: new[] { "company_id", "current_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_schedules_company_id_status_next_occurrence_date",
                table: "accounting_schedules",
                columns: new[] { "company_id", "status", "next_occurrence_date" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_approval_bindings_accounting_schedule_versions_company_id_schedule_version_id",
                table: "accounting_schedule_approval_bindings",
                columns: new[] { "company_id", "schedule_version_id" },
                principalTable: "accounting_schedule_versions",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_approval_bindings_accounting_schedules_company_id_schedule_id",
                table: "accounting_schedule_approval_bindings",
                columns: new[] { "company_id", "schedule_id" },
                principalTable: "accounting_schedules",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_evidence_links_accounting_schedule_versions_company_id_schedule_version_id",
                table: "accounting_schedule_evidence_links",
                columns: new[] { "company_id", "schedule_version_id" },
                principalTable: "accounting_schedule_versions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_exceptions_accounting_schedule_occurrences_company_id_occurrence_id",
                table: "accounting_schedule_exceptions",
                columns: new[] { "company_id", "occurrence_id" },
                principalTable: "accounting_schedule_occurrences",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_line_dimensions_accounting_schedule_lines_company_id_schedule_line_id",
                table: "accounting_schedule_line_dimensions",
                columns: new[] { "company_id", "schedule_line_id" },
                principalTable: "accounting_schedule_lines",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_lines_accounting_schedule_versions_company_id_schedule_version_id",
                table: "accounting_schedule_lines",
                columns: new[] { "company_id", "schedule_version_id" },
                principalTable: "accounting_schedule_versions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_occurrences_accounting_schedule_versions_company_id_schedule_version_id",
                table: "accounting_schedule_occurrences",
                columns: new[] { "company_id", "schedule_version_id" },
                principalTable: "accounting_schedule_versions",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_occurrences_accounting_schedules_company_id_schedule_id",
                table: "accounting_schedule_occurrences",
                columns: new[] { "company_id", "schedule_id" },
                principalTable: "accounting_schedules",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_operations_accounting_schedules_company_id_schedule_id",
                table: "accounting_schedule_operations",
                columns: new[] { "company_id", "schedule_id" },
                principalTable: "accounting_schedules",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_accounting_schedule_versions_accounting_schedules_company_id_schedule_id",
                table: "accounting_schedule_versions",
                columns: new[] { "company_id", "schedule_id" },
                principalTable: "accounting_schedules",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accounting_schedules_accounting_schedule_versions_company_id_current_version_id",
                table: "accounting_schedules");

            migrationBuilder.DropTable(
                name: "accounting_schedule_approval_bindings");

            migrationBuilder.DropTable(
                name: "accounting_schedule_evidence_links");

            migrationBuilder.DropTable(
                name: "accounting_schedule_exceptions");

            migrationBuilder.DropTable(
                name: "accounting_schedule_line_dimensions");

            migrationBuilder.DropTable(
                name: "accounting_schedule_operations");

            migrationBuilder.DropTable(
                name: "accounting_schedule_occurrences");

            migrationBuilder.DropTable(
                name: "accounting_schedule_lines");

            migrationBuilder.DropTable(
                name: "accounting_schedule_versions");

            migrationBuilder.DropTable(
                name: "accounting_schedules");
        }
    }
}
