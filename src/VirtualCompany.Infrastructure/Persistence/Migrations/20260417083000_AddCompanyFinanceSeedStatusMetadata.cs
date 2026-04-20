using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCompanyFinanceSeedStatusMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "finance_seed_status",
                table: "companies",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "finance_seed_status_updated_at",
                table: "companies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "finance_seeded_at",
                table: "companies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE companies
                SET finance_seed_status = 'not_seeded',
                    finance_seed_status_updated_at = COALESCE(UpdatedUtc, CreatedUtc)
                WHERE finance_seed_status IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "finance_seed_status",
                table: "companies",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "not_seeded",
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "finance_seed_status_updated_at",
                table: "companies",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_companies_finance_seed_status",
                table: "companies",
                sql: "finance_seed_status IN ('not_seeded', 'partially_seeded', 'fully_seeded')");

            migrationBuilder.CreateIndex(
                name: "IX_companies_finance_seed_status",
                table: "companies",
                column: "finance_seed_status");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_companies_finance_seed_status",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "IX_companies_finance_seed_status",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "finance_seed_status",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "finance_seed_status_updated_at",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "finance_seeded_at",
                table: "companies");
        }
    }
}