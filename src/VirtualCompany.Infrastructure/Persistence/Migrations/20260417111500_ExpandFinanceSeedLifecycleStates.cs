using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class ExpandFinanceSeedLifecycleStates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_companies_finance_seed_status",
                table: "companies");

            migrationBuilder.Sql(
                """
                UPDATE companies
                SET finance_seed_status = CASE finance_seed_status
                    WHEN 'partially_seeded' THEN 'seeding'
                    WHEN 'fully_seeded' THEN 'seeded'
                    ELSE finance_seed_status
                END
                WHERE finance_seed_status IN ('partially_seeded', 'fully_seeded');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_companies_finance_seed_status",
                table: "companies",
                sql: "finance_seed_status IN ('not_seeded', 'seeding', 'seeded', 'failed')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_companies_finance_seed_status",
                table: "companies");

            migrationBuilder.AddCheckConstraint(
                name: "CK_companies_finance_seed_status",
                table: "companies",
                sql: "finance_seed_status IN ('not_seeded', 'partially_seeded', 'fully_seeded')");
        }
    }
}