using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddReportingPeriodLocks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var boolType = isPostgres ? "boolean" : "bit";
            var falseDefault = isPostgres ? "FALSE" : "CAST(0 AS bit)";

            migrationBuilder.AddColumn<bool>(
                name: "is_reporting_locked",
                table: "finance_fiscal_periods",
                type: boolType,
                nullable: false,
                defaultValueSql: falseDefault);

            migrationBuilder.AddColumn<DateTime>(
                name: "reporting_locked_at",
                table: "finance_fiscal_periods",
                type: dateTimeType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reporting_locked_by_user_id",
                table: "finance_fiscal_periods",
                type: guidType,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reporting_unlocked_at",
                table: "finance_fiscal_periods",
                type: dateTimeType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reporting_unlocked_by_user_id",
                table: "finance_fiscal_periods",
                type: guidType,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_fiscal_periods_company_id_is_closed_is_reporting_locked",
                table: "finance_fiscal_periods",
                columns: new[] { "company_id", "is_closed", "is_reporting_locked" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_finance_fiscal_periods_company_id_is_closed_is_reporting_locked",
                table: "finance_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "is_reporting_locked",
                table: "finance_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "reporting_locked_at",
                table: "finance_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "reporting_locked_by_user_id",
                table: "finance_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "reporting_unlocked_at",
                table: "finance_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "reporting_unlocked_by_user_id",
                table: "finance_fiscal_periods");
        }
    }
}