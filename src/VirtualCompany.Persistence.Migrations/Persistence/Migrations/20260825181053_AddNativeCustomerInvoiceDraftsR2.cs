using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeCustomerInvoiceDraftsR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_invoice_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    supply_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    payment_term_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    payment_term_days = table.Column<int>(type: "int", nullable: false),
                    buyer_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    seller_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    delivery_intent = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    input_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    result_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    policy_pack_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    policy_pack_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    policy_definition_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    rounding_precision = table.Column<int>(type: "int", nullable: false),
                    rounding_mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    net_total = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    discount_total = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    tax_total = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    gross_total = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    rounding_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    warnings_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    blockers_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_draft_version = table.Column<long>(type: "bigint", nullable: true),
                    approval_result_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    discarded_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_drafts", x => x.id);
                    table.UniqueConstraint("AK_customer_invoice_drafts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_invoice_drafts_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_drafts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_invoice_drafts_finance_counterparties_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_draft_evidence_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_draft_evidence_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_draft_evidence_links_customer_invoice_drafts_company_id_draft_id",
                        columns: x => new { x.company_id, x.draft_id },
                        principalTable: "customer_invoice_drafts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_invoice_draft_evidence_links_knowledge_documents_company_id_document_id",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_draft_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    discount_percent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    discount_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    tax_rule_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tax_rule_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    tax_classification = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tax_rate = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    gross_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    revenue_account_role_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    tax_account_role_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    vat_box_mappings_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    tax_evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    dimension_facts_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    order_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_draft_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_draft_lines_customer_invoice_drafts_company_id_draft_id",
                        columns: x => new { x.company_id, x.draft_id },
                        principalTable: "customer_invoice_drafts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_draft_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    result_version = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_draft_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_draft_operations_customer_invoice_drafts_company_id_draft_id",
                        columns: x => new { x.company_id, x.draft_id },
                        principalTable: "customer_invoice_drafts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_draft_evidence_links_company_id_document_id",
                table: "customer_invoice_draft_evidence_links",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_draft_evidence_links_company_id_draft_id_document_id",
                table: "customer_invoice_draft_evidence_links",
                columns: new[] { "company_id", "draft_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_draft_lines_company_id_draft_id_sequence",
                table: "customer_invoice_draft_lines",
                columns: new[] { "company_id", "draft_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_draft_operations_company_id_draft_id_action",
                table: "customer_invoice_draft_operations",
                columns: new[] { "company_id", "draft_id", "action" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_draft_operations_company_id_idempotency_key",
                table: "customer_invoice_draft_operations",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_drafts_company_id_approval_request_id",
                table: "customer_invoice_drafts",
                columns: new[] { "company_id", "approval_request_id" },
                unique: true,
                filter: "approval_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_drafts_company_id_customer_id_updated_utc",
                table: "customer_invoice_drafts",
                columns: new[] { "company_id", "customer_id", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_drafts_company_id_status_updated_utc",
                table: "customer_invoice_drafts",
                columns: new[] { "company_id", "status", "updated_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_invoice_draft_evidence_links");

            migrationBuilder.DropTable(
                name: "customer_invoice_draft_lines");

            migrationBuilder.DropTable(
                name: "customer_invoice_draft_operations");

            migrationBuilder.DropTable(
                name: "customer_invoice_drafts");
        }
    }
}
