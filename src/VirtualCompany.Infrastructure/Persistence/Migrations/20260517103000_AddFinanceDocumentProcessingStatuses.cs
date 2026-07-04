using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260517103000_AddFinanceDocumentProcessingStatuses")]
    public partial class AddFinanceDocumentProcessingStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddProcessingStatus(migrationBuilder, "finance_invoices");
            AddProcessingStatus(migrationBuilder, "finance_bills");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropProcessingStatus(migrationBuilder, "finance_invoices");
            DropProcessingStatus(migrationBuilder, "finance_bills");
        }

        private static void AddProcessingStatus(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<string>(
                name: "processing_status",
                table: table,
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddCheckConstraint(
                name: $"CK_{table}_processing_status",
                table: table,
                sql: "processing_status IN ('none', 'payment_pending', 'authorization_pending')");

            migrationBuilder.CreateIndex(
                name: $"IX_{table}_company_id_processing_status_due_at",
                table: table,
                columns: new[] { "company_id", "processing_status", "due_at" });
        }

        private static void DropProcessingStatus(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.DropIndex(
                name: $"IX_{table}_company_id_processing_status_due_at",
                table: table);

            migrationBuilder.DropCheckConstraint(
                name: $"CK_{table}_processing_status",
                table: table);

            migrationBuilder.DropColumn(
                name: "processing_status",
                table: table);
        }
    }
}
