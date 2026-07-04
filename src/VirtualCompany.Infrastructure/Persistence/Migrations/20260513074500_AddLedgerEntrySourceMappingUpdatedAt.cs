using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260513074500_AddLedgerEntrySourceMappingUpdatedAt")]
    public partial class AddLedgerEntrySourceMappingUpdatedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "ledger_entry_source_mappings",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "timestamp with time zone" : "datetime2",
                nullable: false,
                defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "NOW()" : "SYSUTCDATETIME()");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "ledger_entry_source_mappings");
        }
    }
}
