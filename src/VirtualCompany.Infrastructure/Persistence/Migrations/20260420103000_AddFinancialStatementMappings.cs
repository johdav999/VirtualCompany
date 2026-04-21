using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Enums;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinancialStatementMappings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var boolType = isPostgres ? "boolean" : "bit";
            var trueDefault = isPostgres ? "TRUE" : "CAST(1 AS bit)";
            var activeFilter = isPostgres ? "is_active = TRUE" : "[is_active] = CAST(1 AS bit)";

            migrationBuilder.CreateTable(
                name: "financial_statement_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    finance_account_id = table.Column<Guid>(type: guidType, nullable: false),
                    statement_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    report_section = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    line_classification = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: boolType, nullable: false, defaultValueSql: trueDefault),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement_mappings", x => x.id);
                    table.CheckConstraint("CK_financial_statement_mappings_statement_type", FinancialStatementTypeValues.BuildCheckConstraintSql("statement_type"));
                    table.CheckConstraint("CK_financial_statement_mappings_report_section", FinancialStatementReportSectionValues.BuildCheckConstraintSql("report_section"));
                    table.CheckConstraint("CK_financial_statement_mappings_line_classification", FinancialStatementLineClassificationValues.BuildCheckConstraintSql("line_classification"));
                    table.ForeignKey(
                        name: "FK_financial_statement_mappings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_financial_statement_mappings_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_mappings_company_id",
                table: "financial_statement_mappings",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_mappings_finance_account_id",
                table: "financial_statement_mappings",
                column: "finance_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_mappings_company_id_finance_account_id",
                table: "financial_statement_mappings",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_mappings_company_id_statement_type_is_active",
                table: "financial_statement_mappings",
                columns: new[] { "company_id", "statement_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_mappings_company_id_finance_account_id_statement_type",
                table: "financial_statement_mappings",
                columns: new[] { "company_id", "finance_account_id", "statement_type" },
                unique: true,
                filter: activeFilter);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_statement_mappings");
        }
    }
}
