using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCompanySimulationProgressionIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_company_simulation_states_status_last_progressed_at_company_id",
                table: "company_simulation_states",
                columns: new[] { "status", "last_progressed_at", "company_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_company_simulation_states_status_last_progressed_at_company_id",
                table: "company_simulation_states");
        }
    }
}
