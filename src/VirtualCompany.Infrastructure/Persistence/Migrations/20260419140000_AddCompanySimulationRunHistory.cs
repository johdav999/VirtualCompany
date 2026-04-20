using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCompanySimulationRunHistory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_simulation_run_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    start_simulated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    current_simulated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    generation_enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    seed = table.Column<int>(type: "int", nullable: false),
                    deterministic_configuration_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    injected_anomalies_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    warnings_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    errors_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_simulation_run_histories", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_simulation_run_histories_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_simulation_run_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_history_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    transitioned_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_simulation_run_transitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_simulation_run_transitions_company_simulation_run_histories_run_history_id",
                        column: x => x.run_history_id,
                        principalTable: "company_simulation_run_histories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_simulation_run_day_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_history_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    simulated_date_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    transactions_generated = table.Column<int>(type: "int", nullable: false),
                    invoices_generated = table.Column<int>(type: "int", nullable: false),
                    bills_generated = table.Column<int>(type: "int", nullable: false),
                    recurring_expense_instances_generated = table.Column<int>(type: "int", nullable: false),
                    alerts_generated = table.Column<int>(type: "int", nullable: false),
                    injected_anomalies_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    warnings_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    errors_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_simulation_run_day_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_simulation_run_day_logs_company_simulation_run_histories_run_history_id",
                        column: x => x.run_history_id,
                        principalTable: "company_simulation_run_histories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_company_simulation_run_histories_company_id_session_id", table: "company_simulation_run_histories", columns: new[] { "company_id", "session_id" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_company_simulation_run_histories_company_id_started_at", table: "company_simulation_run_histories", columns: new[] { "company_id", "started_at" });
            migrationBuilder.CreateIndex(name: "IX_company_simulation_run_transitions_company_id_session_id_transitioned_at", table: "company_simulation_run_transitions", columns: new[] { "company_id", "session_id", "transitioned_at" });
            migrationBuilder.CreateIndex(name: "IX_company_simulation_run_transitions_run_history_id", table: "company_simulation_run_transitions", column: "run_history_id");
            migrationBuilder.CreateIndex(name: "IX_company_simulation_run_day_logs_company_id_created_at", table: "company_simulation_run_day_logs", columns: new[] { "company_id", "created_at" });
            migrationBuilder.CreateIndex(name: "IX_company_simulation_run_day_logs_company_id_session_id_simulated_date_at", table: "company_simulation_run_day_logs", columns: new[] { "company_id", "session_id", "simulated_date_at" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_company_simulation_run_day_logs_run_history_id", table: "company_simulation_run_day_logs", column: "run_history_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "company_simulation_run_day_logs");
            migrationBuilder.DropTable(name: "company_simulation_run_transitions");
            migrationBuilder.DropTable(name: "company_simulation_run_histories");
        }
    }
}