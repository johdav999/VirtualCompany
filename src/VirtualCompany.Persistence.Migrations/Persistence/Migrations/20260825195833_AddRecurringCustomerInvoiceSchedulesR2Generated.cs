using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringCustomerInvoiceSchedulesR2Generated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_invoice_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    cadence = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    billing_day = table.Column<int>(type: "int", nullable: false),
                    time_zone_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    business_day_convention = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    proration_rule = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    due_date_offset_days = table.Column<int>(type: "int", nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    payment_term_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    payment_term_days = table.Column<int>(type: "int", nullable: false),
                    buyer_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    seller_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    delivery_intent = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    auto_issue_enabled = table.Column<bool>(type: "bit", nullable: false),
                    next_occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_schedules", x => x.id);
                    table.UniqueConstraint("AK_customer_invoice_schedules_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_invoice_schedules_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_invoice_schedules_finance_counterparties_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_schedule_evidence_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_schedule_evidence_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_schedule_evidence_links_customer_invoice_schedules_company_id_schedule_id",
                        columns: x => new { x.company_id, x.schedule_id },
                        principalTable: "customer_invoice_schedules",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_invoice_schedule_evidence_links_knowledge_documents_company_id_document_id",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_schedule_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    discount_percent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    tax_rule_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tax_classification = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tax_evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    dimension_facts_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    revenue_account_role_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_schedule_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_schedule_lines_customer_invoice_schedules_company_id_schedule_id",
                        columns: x => new { x.company_id, x.schedule_id },
                        principalTable: "customer_invoice_schedules",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_schedule_occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    schedule_version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_schedule_occurrences", x => x.id);
                    table.UniqueConstraint("AK_customer_invoice_schedule_occurrences_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_invoice_schedule_occurrences_customer_invoice_schedules_company_id_schedule_id",
                        columns: x => new { x.company_id, x.schedule_id },
                        principalTable: "customer_invoice_schedules",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_schedule_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    result_version = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_schedule_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_schedule_operations_customer_invoice_schedules_company_id_schedule_id",
                        columns: x => new { x.company_id, x.schedule_id },
                        principalTable: "customer_invoice_schedules",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_evidence_links_company_id_document_id",
                table: "customer_invoice_schedule_evidence_links",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_evidence_links_company_id_schedule_id_document_id",
                table: "customer_invoice_schedule_evidence_links",
                columns: new[] { "company_id", "schedule_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_lines_company_id_schedule_id_sequence",
                table: "customer_invoice_schedule_lines",
                columns: new[] { "company_id", "schedule_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_occurrences_company_id_schedule_id_occurrence_date",
                table: "customer_invoice_schedule_occurrences",
                columns: new[] { "company_id", "schedule_id", "occurrence_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_occurrences_company_id_status_lease_expires_utc",
                table: "customer_invoice_schedule_occurrences",
                columns: new[] { "company_id", "status", "lease_expires_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_operations_company_id_idempotency_key",
                table: "customer_invoice_schedule_operations",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_operations_company_id_schedule_id",
                table: "customer_invoice_schedule_operations",
                columns: new[] { "company_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedules_company_id_customer_id_updated_utc",
                table: "customer_invoice_schedules",
                columns: new[] { "company_id", "customer_id", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedules_company_id_status_next_occurrence_date",
                table: "customer_invoice_schedules",
                columns: new[] { "company_id", "status", "next_occurrence_date" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_invoice_schedule_evidence_links");

            migrationBuilder.DropTable(
                name: "customer_invoice_schedule_lines");

            migrationBuilder.DropTable(
                name: "customer_invoice_schedule_occurrences");

            migrationBuilder.DropTable(
                name: "customer_invoice_schedule_operations");

            migrationBuilder.DropTable(
                name: "customer_invoice_schedules");

        }
    }
}
