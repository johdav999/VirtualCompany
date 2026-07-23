using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524190000_AddSupplierInvoiceEnrichmentActions")]
    public partial class AddSupplierInvoiceEnrichmentActions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_invoice_enrichment_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "not_suggested"),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    synced_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    response_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    suggestion_payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "JSON_QUERY('{}')"),
                    reconciliation_warnings_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    provider_metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "JSON_QUERY('{}')"),
                    audit_trail_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "JSON_QUERY('{}')"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_invoice_enrichment_actions", x => x.id);
                    table.UniqueConstraint("AK_supplier_invoice_enrichment_actions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint(
                        "CK_supplier_invoice_enrichment_actions_status",
                        "status IN ('not_suggested', 'awaiting_approval', 'approved', 'sync_requested', 'synced', 'failed', 'reconciliation_warning')");
                    table.ForeignKey(
                        name: "FK_supplier_invoice_enrichment_actions_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_supplier_invoice_enrichment_actions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_enrichment_actions_finance_bills_company_id_bill_id",
                        columns: x => new { x.company_id, x.bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_supplier_invoice_enrichment_actions_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_enrichment_actions_approval_request_id",
                table: "supplier_invoice_enrichment_actions",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_enrichment_actions_company_id_approval_request_id",
                table: "supplier_invoice_enrichment_actions",
                columns: new[] { "company_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_enrichment_actions_company_id_bill_id",
                table: "supplier_invoice_enrichment_actions",
                columns: new[] { "company_id", "bill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_enrichment_actions_company_id_status_updated_at",
                table: "supplier_invoice_enrichment_actions",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_enrichment_actions_task_id",
                table: "supplier_invoice_enrichment_actions",
                column: "task_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_invoice_enrichment_actions");
        }
    }
}
