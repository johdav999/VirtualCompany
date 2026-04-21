using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddLedgerEntrySourceMappings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";

            migrationBuilder.CreateTable(
                name: "ledger_entry_source_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: guidType, nullable: false),
                    source_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    posted_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entry_source_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_ledger_entry_source_mappings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ledger_entry_source_mappings_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_source_mappings_company_id_ledger_entry_id",
                table: "ledger_entry_source_mappings",
                columns: new[] { "company_id", "ledger_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_source_mappings_company_id_source_type_source_id",
                table: "ledger_entry_source_mappings",
                columns: new[] { "company_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.Sql(
                @"
INSERT INTO ledger_entry_source_mappings (id, company_id, ledger_entry_id, source_type, source_id, posted_at, created_at)
SELECT
    id,
    company_id,
    id,
    source_type,
    source_id,
    COALESCE(posted_at, entry_at),
    COALESCE(posted_at, created_at)
FROM ledger_entries
WHERE source_type IS NOT NULL
  AND source_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM ledger_entry_source_mappings mapping
      WHERE mapping.company_id = ledger_entries.company_id
        AND mapping.source_type = ledger_entries.source_type
        AND mapping.source_id = ledger_entries.source_id
  );");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_entry_source_mappings");
        }
    }
}
