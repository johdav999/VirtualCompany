using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddLedgerLineCostCenterForVariance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";

            migrationBuilder.AddColumn<Guid>(
                name: "cost_center_id",
                table: "ledger_entry_lines",
                type: guidType,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_lines_company_id_finance_account_id_cost_center_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "finance_account_id", "cost_center_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ledger_entry_lines_company_id_finance_account_id_cost_center_id",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "cost_center_id",
                table: "ledger_entry_lines");
        }
    }
}