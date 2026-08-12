using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleSupplierSubscriptionSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierSubscriptionBillMatches_CompanyId_BillId",
                table: "SupplierSubscriptionBillMatches");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionBillMatches_CompanyId_BillId",
                table: "SupplierSubscriptionBillMatches",
                columns: new[] { "CompanyId", "BillId" },
                unique: true,
                filter: "[Status] = 'confirmed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierSubscriptionBillMatches_CompanyId_BillId",
                table: "SupplierSubscriptionBillMatches");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierSubscriptionBillMatches_CompanyId_BillId",
                table: "SupplierSubscriptionBillMatches",
                columns: new[] { "CompanyId", "BillId" },
                unique: true);
        }
    }
}
