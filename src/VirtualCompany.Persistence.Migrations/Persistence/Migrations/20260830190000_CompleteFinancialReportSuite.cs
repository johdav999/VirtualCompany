using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260830190000_CompleteFinancialReportSuite")]
public sealed class CompleteFinancialReportSuite : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(name: "effective_from", table: "financial_statement_mappings",
            type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1));
        migrationBuilder.AddColumn<DateOnly>(name: "effective_to", table: "financial_statement_mappings",
            type: "date", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "supersedes_mapping_id", table: "financial_statement_mappings",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<long>(name: "version_number", table: "financial_statement_mappings",
            type: "bigint", nullable: false, defaultValue: 1L);

        migrationBuilder.CreateTable(name: "financial_report_suite_snapshots", columns: table => new
        {
            id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            report_kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            calculation_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            mapping_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            parameters_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            report_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
            created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_financial_report_suite_snapshots", x => x.id);
            table.ForeignKey("FK_financial_report_suite_snapshots_companies_company_id", x => x.company_id,
                "companies", "id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_financial_report_suite_snapshots_finance_fiscal_periods_company_id_fiscal_period_id",
                x => new { x.company_id, x.fiscal_period_id }, "finance_fiscal_periods", new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateIndex("IX_financial_statement_mappings_company_id_finance_account_id_statement_type_effective_from",
            "financial_statement_mappings", new[] { "company_id", "finance_account_id", "statement_type", "effective_from" });
        migrationBuilder.CreateIndex("IX_financial_statement_mappings_supersedes_mapping_id",
            "financial_statement_mappings", "supersedes_mapping_id");
        migrationBuilder.AddForeignKey(
            name: "FK_financial_statement_mappings_financial_statement_mappings_supersedes_mapping_id",
            table: "financial_statement_mappings",
            column: "supersedes_mapping_id",
            principalTable: "financial_statement_mappings",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex("IX_financial_report_suite_snapshots_company_id_idempotency_key",
            "financial_report_suite_snapshots", new[] { "company_id", "idempotency_key" }, unique: true);
        migrationBuilder.CreateIndex("IX_financial_report_suite_snapshots_company_id_fiscal_period_id_report_kind_created_utc",
            "financial_report_suite_snapshots", new[] { "company_id", "fiscal_period_id", "report_kind", "created_utc" });
        migrationBuilder.CreateIndex("IX_financial_report_suite_snapshots_company_id_fiscal_period_id_report_kind_parameters_hash_checksum",
            "financial_report_suite_snapshots", new[] { "company_id", "fiscal_period_id", "report_kind", "parameters_hash", "checksum" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("financial_report_suite_snapshots");
        migrationBuilder.DropForeignKey("FK_financial_statement_mappings_financial_statement_mappings_supersedes_mapping_id",
            "financial_statement_mappings");
        migrationBuilder.DropIndex("IX_financial_statement_mappings_company_id_finance_account_id_statement_type_effective_from",
            "financial_statement_mappings");
        migrationBuilder.DropIndex("IX_financial_statement_mappings_supersedes_mapping_id", "financial_statement_mappings");
        migrationBuilder.DropColumn("effective_from", "financial_statement_mappings");
        migrationBuilder.DropColumn("effective_to", "financial_statement_mappings");
        migrationBuilder.DropColumn("supersedes_mapping_id", "financial_statement_mappings");
        migrationBuilder.DropColumn("version_number", "financial_statement_mappings");
    }
}
