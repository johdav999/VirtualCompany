using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerInvoiceCorrectionsR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "original_invoice_id",
                table: "customer_invoice_drafts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_tasks_company_id_id",
                table: "tasks",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_payment_allocations_company_id_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "customer_invoice_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correction_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    beneficiary_reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    payment_evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    credit_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correcting_invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    original_vat_return_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correction_vat_return_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    expense_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    executed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    executed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_corrections", x => x.id);
                    table.UniqueConstraint("AK_customer_invoice_corrections_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_invoice_corrections_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_corrections_customer_invoice_drafts_company_id_credit_draft_id",
                        columns: x => new { x.company_id, x.credit_draft_id },
                        principalTable: "customer_invoice_drafts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_corrections_finance_invoices_company_id_correcting_invoice_id",
                        columns: x => new { x.company_id, x.correcting_invoice_id },
                        principalTable: "finance_invoices",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_corrections_finance_invoices_company_id_invoice_id",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "finance_invoices",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_corrections_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_corrections_tasks_company_id_task_id",
                        columns: x => new { x.company_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_correction_allocation_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payment_allocation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    released_amount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_correction_allocation_adjustments", x => x.id);
                    table.UniqueConstraint("AK_customer_invoice_correction_allocation_adjustments_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_invoice_correction_allocation_adjustments_customer_invoice_corrections_company_id_correction_id",
                        columns: x => new { x.company_id, x.correction_id },
                        principalTable: "customer_invoice_corrections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_invoice_correction_allocation_adjustments_payment_allocations_company_id_payment_allocation_id",
                        columns: x => new { x.company_id, x.payment_allocation_id },
                        principalTable: "payment_allocations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_refund_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    beneficiary_reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    payment_evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    available_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    claimed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    claim_token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    provider_reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    failure_category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    safe_failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_refund_executions", x => x.id);
                    table.UniqueConstraint("AK_customer_invoice_refund_executions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_invoice_refund_executions_customer_invoice_corrections_company_id_correction_id",
                        columns: x => new { x.company_id, x.correction_id },
                        principalTable: "customer_invoice_corrections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_drafts_company_id_original_invoice_id",
                table: "customer_invoice_drafts",
                columns: new[] { "company_id", "original_invoice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_correction_allocation_adjustments_company_id_correction_id_payment_allocation_id",
                table: "customer_invoice_correction_allocation_adjustments",
                columns: new[] { "company_id", "correction_id", "payment_allocation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_correction_allocation_adjustments_company_id_payment_allocation_id",
                table: "customer_invoice_correction_allocation_adjustments",
                columns: new[] { "company_id", "payment_allocation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_corrections_company_id_approval_request_id",
                table: "customer_invoice_corrections",
                columns: new[] { "company_id", "approval_request_id" },
                unique: true,
                filter: "approval_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_corrections_company_id_correcting_invoice_id",
                table: "customer_invoice_corrections",
                columns: new[] { "company_id", "correcting_invoice_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_corrections_company_id_credit_draft_id",
                table: "customer_invoice_corrections",
                columns: new[] { "company_id", "credit_draft_id" },
                unique: true,
                filter: "credit_draft_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_corrections_company_id_idempotency_key",
                table: "customer_invoice_corrections",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_corrections_company_id_invoice_id_status",
                table: "customer_invoice_corrections",
                columns: new[] { "company_id", "invoice_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_corrections_company_id_ledger_entry_id",
                table: "customer_invoice_corrections",
                columns: new[] { "company_id", "ledger_entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_corrections_company_id_task_id",
                table: "customer_invoice_corrections",
                columns: new[] { "company_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_refund_executions_company_id_correction_id",
                table: "customer_invoice_refund_executions",
                columns: new[] { "company_id", "correction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_refund_executions_company_id_idempotency_key",
                table: "customer_invoice_refund_executions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_refund_executions_status_available_utc_company_id",
                table: "customer_invoice_refund_executions",
                columns: new[] { "status", "available_utc", "company_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_customer_invoice_drafts_finance_invoices_company_id_original_invoice_id",
                table: "customer_invoice_drafts",
                columns: new[] { "company_id", "original_invoice_id" },
                principalTable: "finance_invoices",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customer_invoice_drafts_finance_invoices_company_id_original_invoice_id",
                table: "customer_invoice_drafts");

            migrationBuilder.DropTable(
                name: "customer_invoice_correction_allocation_adjustments");

            migrationBuilder.DropTable(
                name: "customer_invoice_refund_executions");

            migrationBuilder.DropTable(
                name: "customer_invoice_corrections");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_tasks_company_id_id",
                table: "tasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_payment_allocations_company_id_id",
                table: "payment_allocations");

            migrationBuilder.DropIndex(
                name: "IX_customer_invoice_drafts_company_id_original_invoice_id",
                table: "customer_invoice_drafts");

            migrationBuilder.DropColumn(
                name: "original_invoice_id",
                table: "customer_invoice_drafts");
        }
    }
}
