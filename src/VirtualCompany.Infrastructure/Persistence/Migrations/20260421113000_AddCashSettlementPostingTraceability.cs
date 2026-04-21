using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCashSettlementPostingTraceability : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "posted_at",
                table: "ledger_entries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_id",
                table: "ledger_entries",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "ledger_entries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id",
                table: "ledger_entries",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true,
                filter: "[source_type] IS NOT NULL AND [source_id] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "posted_at",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "ledger_entries");
        }
    }
}
