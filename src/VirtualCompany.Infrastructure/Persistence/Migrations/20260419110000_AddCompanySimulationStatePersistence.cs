using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCompanySimulationStatePersistence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_simulation_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    current_simulated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_progressed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    generation_enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    seed = table.Column<int>(type: "int", nullable: false),
                    active_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    start_simulated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    deterministic_configuration_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    paused_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    stopped_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_simulation_states", x => x.id);
                    table.CheckConstraint("CK_company_simulation_states_active_session", "(status = 'stopped' AND active_session_id IS NULL) OR (status IN ('running', 'paused') AND active_session_id IS NOT NULL)");
                    table.CheckConstraint("CK_company_simulation_states_status", "status IN ('running', 'paused', 'stopped')");
                    table.ForeignKey(
                        name: "FK_company_simulation_states_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_simulation_states_company_id",
                table: "company_simulation_states",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_simulation_states_company_id_active_session_id",
                table: "company_simulation_states",
                columns: new[] { "company_id", "active_session_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "company_simulation_states");
        }
    }
}
