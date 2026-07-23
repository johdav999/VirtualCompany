using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260522103000_AddSupplierInvoicePaymentExportState")]
    public partial class AddSupplierInvoicePaymentExportState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "export_status",
                table: "supplier_invoice_payment_proposals",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "not_exported");

            migrationBuilder.AddColumn<string>(
                name: "export_provider_key",
                table: "supplier_invoice_payment_proposals",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "export_connection_id",
                table: "supplier_invoice_payment_proposals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "export_requested_by_user_id",
                table: "supplier_invoice_payment_proposals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "export_requested_at",
                table: "supplier_invoice_payment_proposals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "exported_at",
                table: "supplier_invoice_payment_proposals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "export_response_summary",
                table: "supplier_invoice_payment_proposals",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "export_provider_metadata_json",
                table: "supplier_invoice_payment_proposals",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'{}'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_supplier_invoice_payment_proposals_export_status",
                table: "supplier_invoice_payment_proposals",
                sql: "export_status IN ('not_exported', 'export_requested', 'exported', 'failed', 'cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_export_status_due_at",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "export_status", "due_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_export_status_due_at",
                table: "supplier_invoice_payment_proposals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_supplier_invoice_payment_proposals_export_status",
                table: "supplier_invoice_payment_proposals");

            migrationBuilder.DropColumn(name: "export_status", table: "supplier_invoice_payment_proposals");
            migrationBuilder.DropColumn(name: "export_provider_key", table: "supplier_invoice_payment_proposals");
            migrationBuilder.DropColumn(name: "export_connection_id", table: "supplier_invoice_payment_proposals");
            migrationBuilder.DropColumn(name: "export_requested_by_user_id", table: "supplier_invoice_payment_proposals");
            migrationBuilder.DropColumn(name: "export_requested_at", table: "supplier_invoice_payment_proposals");
            migrationBuilder.DropColumn(name: "exported_at", table: "supplier_invoice_payment_proposals");
            migrationBuilder.DropColumn(name: "export_response_summary", table: "supplier_invoice_payment_proposals");
            migrationBuilder.DropColumn(name: "export_provider_metadata_json", table: "supplier_invoice_payment_proposals");
        }
    }
}
