using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceSeedBackfillCampaign : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_seed_backfill_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    scanned_count = table.Column<int>(type: "int", nullable: false),
                    queued_count = table.Column<int>(type: "int", nullable: false),
                    succeeded_count = table.Column<int>(type: "int", nullable: false),
                    skipped_count = table.Column<int>(type: "int", nullable: false),
                    failed_count = table.Column<int>(type: "int", nullable: false),
                    configuration_snapshot_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    error_details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_seed_backfill_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "finance_seed_backfill_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    background_execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    skip_reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    error_details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    seed_state_before = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    seed_state_after = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_seed_backfill_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_seed_backfill_attempts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_seed_backfill_attempts_finance_seed_backfill_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "finance_seed_backfill_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_seed_backfill_attempts_background_execution_id",
                table: "finance_seed_backfill_attempts",
                column: "background_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_seed_backfill_attempts_company_id",
                table: "finance_seed_backfill_attempts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_seed_backfill_attempts_run_id_company_id",
                table: "finance_seed_backfill_attempts",
                columns: new[] { "run_id", "company_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_seed_backfill_runs_started_at",
                table: "finance_seed_backfill_runs",
                column: "started_at");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "finance_seed_backfill_attempts");
            migrationBuilder.DropTable(name: "finance_seed_backfill_runs");
        }
    }
}