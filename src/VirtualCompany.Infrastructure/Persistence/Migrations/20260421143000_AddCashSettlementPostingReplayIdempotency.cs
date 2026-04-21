using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCashSettlementPostingReplayIdempotency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var filteredLedgerIndex = isPostgres
                ? "source_type IS NOT NULL AND source_id IS NOT NULL AND posted_at IS NOT NULL"
                : "[source_type] IS NOT NULL AND [source_id] IS NOT NULL AND [posted_at] IS NOT NULL";

            migrationBuilder.Sql(
                @"
UPDATE ledger_entries
SET posted_at = COALESCE(posted_at, entry_at)
WHERE source_type IS NOT NULL
  AND source_id IS NOT NULL
  AND posted_at IS NULL;");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entry_source_mappings_company_id_source_type_source_id",
                table: "ledger_entry_source_mappings");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id_posted_at",
                table: "ledger_entries",
                columns: new[] { "company_id", "source_type", "source_id", "posted_at" },
                unique: true,
                filter: filteredLedgerIndex);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_source_mappings_company_id_source_type_source_id_posted_at",
                table: "ledger_entry_source_mappings",
                columns: new[] { "company_id", "source_type", "source_id", "posted_at" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var filteredLedgerIndex = isPostgres
                ? "source_type IS NOT NULL AND source_id IS NOT NULL"
                : "[source_type] IS NOT NULL AND [source_id] IS NOT NULL";

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id_posted_at",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entry_source_mappings_company_id_source_type_source_id_posted_at",
                table: "ledger_entry_source_mappings");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id",
                table: "ledger_entries",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true,
                filter: filteredLedgerIndex);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_source_mappings_company_id_source_type_source_id",
                table: "ledger_entry_source_mappings",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);
        }
    }
}
