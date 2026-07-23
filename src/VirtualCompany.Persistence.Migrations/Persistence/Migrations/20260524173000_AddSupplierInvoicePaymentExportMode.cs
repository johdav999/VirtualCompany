using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524173000_AddSupplierInvoicePaymentExportMode")]
    public partial class AddSupplierInvoicePaymentExportMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "export_mode",
                table: "supplier_invoice_payment_proposals",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "register_payment");

            migrationBuilder.AddCheckConstraint(
                name: "CK_supplier_invoice_payment_proposals_export_mode",
                table: "supplier_invoice_payment_proposals",
                sql: "export_mode IN ('register_payment', 'prepare_payment_file', 'manual_export')");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_export_mode_export_status_due_at",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "export_mode", "export_status", "due_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_export_mode_export_status_due_at",
                table: "supplier_invoice_payment_proposals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_supplier_invoice_payment_proposals_export_mode",
                table: "supplier_invoice_payment_proposals");

            migrationBuilder.DropColumn(
                name: "export_mode",
                table: "supplier_invoice_payment_proposals");
        }
    }
}
