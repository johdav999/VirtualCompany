using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260519193000_AddFinanceDocumentPaidAmounts")]
    public partial class AddFinanceDocumentPaidAmounts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddPaidAmount(migrationBuilder, "finance_invoices");
            AddPaidAmount(migrationBuilder, "finance_bills");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropPaidAmount(migrationBuilder, "finance_invoices");
            DropPaidAmount(migrationBuilder, "finance_bills");
        }

        private static void AddPaidAmount(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "paid_amount",
                table: table,
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: $"CK_{table}_paid_amount_non_negative",
                table: table,
                sql: "paid_amount >= 0");
        }

        private static void DropPaidAmount(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.DropCheckConstraint(
                name: $"CK_{table}_paid_amount_non_negative",
                table: table);

            migrationBuilder.DropColumn(
                name: "paid_amount",
                table: table);
        }
    }
}
