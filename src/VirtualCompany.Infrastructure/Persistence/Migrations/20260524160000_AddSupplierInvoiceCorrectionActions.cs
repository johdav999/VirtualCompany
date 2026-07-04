using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524160000_AddSupplierInvoiceCorrectionActions")]
    public partial class AddSupplierInvoiceCorrectionActions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_invoice_correction_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    credit_note_bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider_credit_note_number = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    response_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    provider_metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'{}'"),
                    audit_trail_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_invoice_correction_actions", x => x.id);
                    table.UniqueConstraint("AK_supplier_invoice_correction_actions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_supplier_invoice_correction_actions_action_type", "action_type IN ('cancellation', 'credit_note')");
                    table.CheckConstraint("CK_supplier_invoice_correction_actions_status", "status IN ('cancellation_requested', 'cancelled', 'cancellation_failed', 'credit_note_requested', 'credit_note_created', 'credit_note_failed')");
                    table.ForeignKey(
                        name: "FK_supplier_invoice_correction_actions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_correction_actions_finance_bills_company_id_bill_id",
                        columns: x => new { x.company_id, x.bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_supplier_invoice_correction_actions_finance_bills_company_id_credit_note_bill_id",
                        columns: x => new { x.company_id, x.credit_note_bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_correction_actions_company_id_bill_id_action_type",
                table: "supplier_invoice_correction_actions",
                columns: new[] { "company_id", "bill_id", "action_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_correction_actions_company_id_credit_note_bill_id",
                table: "supplier_invoice_correction_actions",
                columns: new[] { "company_id", "credit_note_bill_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_correction_actions_company_id_status_updated_at",
                table: "supplier_invoice_correction_actions",
                columns: new[] { "company_id", "status", "updated_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "supplier_invoice_correction_actions");
        }
    }
}
