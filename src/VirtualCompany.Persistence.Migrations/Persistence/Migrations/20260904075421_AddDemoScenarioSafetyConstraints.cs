using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoScenarioSafetyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_demo_scenario_runs_status",
                table: "demo_scenario_runs",
                sql: "status IN ('ready', 'running', 'completed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_companies_demo_marker",
                table: "companies",
                sql: "is_demo_tenant = 0 OR (demo_scenario_key IS NOT NULL AND demo_scenario_version >= 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_demo_scenario_runs_companies_company_id",
                table: "demo_scenario_runs",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_demo_scenario_runs_companies_company_id",
                table: "demo_scenario_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_demo_scenario_runs_status",
                table: "demo_scenario_runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_companies_demo_marker",
                table: "companies");
        }
    }
}
