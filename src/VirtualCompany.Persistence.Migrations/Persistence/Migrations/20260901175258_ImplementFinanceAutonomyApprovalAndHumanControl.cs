using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementFinanceAutonomyApprovalAndHumanControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "revision_number",
                table: "finance_autonomy_runs",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "revision_of_run_id",
                table: "finance_autonomy_runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_runs_revision_of_run_id",
                table: "finance_autonomy_runs",
                column: "revision_of_run_id");

            migrationBuilder.AddForeignKey(
                name: "FK_finance_autonomy_runs_finance_autonomy_runs_revision_of_run_id",
                table: "finance_autonomy_runs",
                column: "revision_of_run_id",
                principalTable: "finance_autonomy_runs",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_finance_autonomy_runs_finance_autonomy_runs_revision_of_run_id",
                table: "finance_autonomy_runs");

            migrationBuilder.DropIndex(
                name: "IX_finance_autonomy_runs_revision_of_run_id",
                table: "finance_autonomy_runs");

            migrationBuilder.DropColumn(
                name: "revision_number",
                table: "finance_autonomy_runs");

            migrationBuilder.DropColumn(
                name: "revision_of_run_id",
                table: "finance_autonomy_runs");
        }
    }
}
