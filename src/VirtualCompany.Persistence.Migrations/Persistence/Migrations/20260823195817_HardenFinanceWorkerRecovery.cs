using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenFinanceWorkerRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "acknowledged_at",
                table: "background_executions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "acknowledged_by_user_id",
                table: "background_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acknowledgement",
                table: "background_executions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "background_executions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "background_executions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_user_id",
                table: "background_executions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lease_expires_at",
                table: "background_executions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "background_executions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "background_executions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "background_execution_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    background_execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    worker_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    attempt_number = table.Column<int>(type: "int", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    failure_category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_background_execution_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_background_execution_attempts_background_executions_background_execution_id",
                        column: x => x.background_execution_id,
                        principalTable: "background_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_background_execution_attempts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_background_executions_status_lease_expires_at",
                table: "background_executions",
                columns: new[] { "status", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_background_execution_attempts_background_execution_id_attempt_number",
                table: "background_execution_attempts",
                columns: new[] { "background_execution_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_background_execution_attempts_company_id_started_at",
                table: "background_execution_attempts",
                columns: new[] { "company_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_background_execution_attempts_outcome_lease_expires_at",
                table: "background_execution_attempts",
                columns: new[] { "outcome", "lease_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_execution_attempts");

            migrationBuilder.DropIndex(
                name: "IX_background_executions_status_lease_expires_at",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "acknowledged_at",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "acknowledged_by_user_id",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "acknowledgement",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "cancelled_by_user_id",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "background_executions");

            migrationBuilder.DropColumn(
                name: "version",
                table: "background_executions");
        }
    }
}
