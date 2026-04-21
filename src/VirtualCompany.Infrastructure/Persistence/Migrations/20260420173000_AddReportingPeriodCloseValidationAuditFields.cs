using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddReportingPeriodCloseValidationAuditFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";

            migrationBuilder.AddColumn<DateTime>(
                name: "last_close_validated_at",
                table: "finance_fiscal_periods",
                type: dateTimeType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_close_validated_by_user_id",
                table: "finance_fiscal_periods",
                type: guidType,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_close_validated_at",
                table: "finance_fiscal_periods");

            migrationBuilder.DropColumn(
                name: "last_close_validated_by_user_id",
                table: "finance_fiscal_periods");
        }
    }
}
