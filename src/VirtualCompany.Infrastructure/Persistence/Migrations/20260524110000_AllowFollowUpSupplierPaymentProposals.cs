using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524110000_AllowFollowUpSupplierPaymentProposals")]
    public partial class AllowFollowUpSupplierPaymentProposals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_bill_id",
                table: "supplier_invoice_payment_proposals");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_bill_id",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "bill_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_bill_id",
                table: "supplier_invoice_payment_proposals");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_payment_proposals_company_id_bill_id",
                table: "supplier_invoice_payment_proposals",
                columns: new[] { "company_id", "bill_id" },
                unique: true);
        }
    }
}
