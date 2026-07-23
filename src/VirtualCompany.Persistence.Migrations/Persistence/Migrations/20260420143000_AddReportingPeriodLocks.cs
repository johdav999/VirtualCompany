using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260420143000_AddReportingPeriodLocks")]
    public partial class AddReportingPeriodLocks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var boolType = isPostgres ? "boolean" : "bit";
            var falseDefault = isPostgres ? "FALSE" : "CAST(0 AS bit)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var companyPrincipalColumn = isPostgres ? "id" : "Id";

            migrationBuilder.CreateTable(
                name: "finance_fiscal_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    name = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    start_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    end_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    is_closed = table.Column<bool>(type: boolType, nullable: false, defaultValueSql: falseDefault),
                    closed_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_fiscal_periods", x => x.id);
                    table.UniqueConstraint("AK_finance_fiscal_periods_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_fiscal_periods_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: companyPrincipalColumn,
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_fiscal_periods_company_id_start_at_end_at",
                table: "finance_fiscal_periods",
                columns: new[] { "company_id", "start_at", "end_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_fiscal_periods_company_id_name",
                table: "finance_fiscal_periods",
                columns: new[] { "company_id", "name" },
                unique: true);

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

            migrationBuilder.DropTable(
                name: "finance_fiscal_periods");
        }
    }
}
