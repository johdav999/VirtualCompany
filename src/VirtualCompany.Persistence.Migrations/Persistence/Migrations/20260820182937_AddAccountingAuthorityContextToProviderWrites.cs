using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260820182937_AddAccountingAuthorityContextToProviderWrites")]
public sealed class AddAccountingAuthorityContextToProviderWrites : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "accounting_date",
            table: "fortnox_write_commands",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "authority_operation",
            table: "fortnox_write_commands",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "authority_period_id",
            table: "fortnox_write_commands",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_fortnox_write_commands_company_id_authority_period_id_accounting_date",
            table: "fortnox_write_commands",
            columns: new[] { "company_id", "authority_period_id", "accounting_date" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_fortnox_write_commands_company_id_authority_period_id_accounting_date",
            table: "fortnox_write_commands");

        migrationBuilder.DropColumn(name: "accounting_date", table: "fortnox_write_commands");
        migrationBuilder.DropColumn(name: "authority_operation", table: "fortnox_write_commands");
        migrationBuilder.DropColumn(name: "authority_period_id", table: "fortnox_write_commands");
    }
}
