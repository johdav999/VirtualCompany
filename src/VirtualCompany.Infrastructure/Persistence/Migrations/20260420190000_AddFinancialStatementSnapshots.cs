using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinancialStatementSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string160Type = isPostgres ? "character varying(160)" : "nvarchar(160)";
            var string3Type = isPostgres ? "character varying(3)" : "nvarchar(3)";

            migrationBuilder.CreateTable(
                name: "financial_statement_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: guidType, nullable: false),
                    statement_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    source_period_start_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    source_period_end_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    balances_checksum = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    generated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement_snapshots", x => x.id);
                    table.CheckConstraint("CK_financial_statement_snapshots_statement_type", "statement_type IN ('balance_sheet', 'cash_flow', 'profit_and_loss')");
                    table.ForeignKey(
                        name: "FK_financial_statement_snapshots_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_financial_statement_snapshots_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_statement_snapshot_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    snapshot_id = table.Column<Guid>(type: guidType, nullable: false),
                    finance_account_id = table.Column<Guid>(type: guidType, nullable: true),
                    line_code = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    line_name = table.Column<string>(type: string160Type, maxLength: 160, nullable: false),
                    line_order = table.Column<int>(type: "int", nullable: false),
                    report_section = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    line_classification = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement_snapshot_lines", x => x.id);
                    table.CheckConstraint("CK_financial_statement_snapshot_lines_report_section", "report_section IN ('balance_sheet_assets', 'balance_sheet_equity', 'balance_sheet_liabilities', 'cash_flow_financing_activities', 'cash_flow_investing_activities', 'cash_flow_operating_activities', 'cash_flow_supplemental_disclosures', 'profit_and_loss_cost_of_sales', 'profit_and_loss_operating_expenses', 'profit_and_loss_other_income_expense', 'profit_and_loss_revenue', 'profit_and_loss_taxes')");
                    table.CheckConstraint("CK_financial_statement_snapshot_lines_line_classification", "line_classification IN ('cash_disbursement', 'cash_receipt', 'contra_revenue', 'cost_of_sales', 'current_asset', 'current_liability', 'depreciation_and_amortization', 'equity', 'financing_cash_inflow', 'financing_cash_outflow', 'income_tax', 'investing_cash_inflow', 'investing_cash_outflow', 'non_cash_adjustment', 'non_current_asset', 'non_current_liability', 'non_operating_expense', 'non_operating_income', 'operating_expense', 'revenue', 'supplemental_disclosure', 'working_capital')");
                    table.ForeignKey(
                        name: "FK_financial_statement_snapshot_lines_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_financial_statement_snapshot_lines_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_statement_snapshot_lines_financial_statement_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "financial_statement_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_version_number",
                table: "financial_statement_snapshots",
                columns: new[] { "company_id", "statement_type", "fiscal_period_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_generated_at",
                table: "financial_statement_snapshots",
                columns: new[] { "company_id", "statement_type", "fiscal_period_id", "generated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_snapshot_lines_company_id_snapshot_id_line_order",
                table: "financial_statement_snapshot_lines",
                columns: new[] { "company_id", "snapshot_id", "line_order" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_snapshot_lines_company_id_finance_account_id",
                table: "financial_statement_snapshot_lines",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_snapshot_lines_snapshot_id_line_code",
                table: "financial_statement_snapshot_lines",
                columns: new[] { "snapshot_id", "line_code" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_statement_snapshot_lines");

            migrationBuilder.DropTable(
                name: "financial_statement_snapshots");
        }
    }
}