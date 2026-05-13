using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505010000_AddSalesFinanceHandoffExecutionLinkage")]
public partial class AddSalesFinanceHandoffExecutionLinkage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("document_type", "sales_finance_handoffs", "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "invoice");
        migrationBuilder.AddColumn<string>("external_system", "sales_finance_handoffs", "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "Fortnox");
        migrationBuilder.AddColumn<string>("external_document_number", "sales_finance_handoffs", "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("idempotency_key", "sales_finance_handoffs", "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<Guid>("approval_id", "sales_finance_handoffs", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>("write_request_id", "sales_finance_handoffs", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>("last_error_code", "sales_finance_handoffs", "nvarchar(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<int>("execution_attempt_count", "sales_finance_handoffs", "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>("requested_at", "sales_finance_handoffs", "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()");
        migrationBuilder.AddColumn<DateTime>("approved_at", "sales_finance_handoffs", "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("execution_started_at", "sales_finance_handoffs", "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("executed_at", "sales_finance_handoffs", "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("failed_at", "sales_finance_handoffs", "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("retried_at", "sales_finance_handoffs", "datetime2", nullable: true);

        migrationBuilder.Sql("""
            UPDATE [sales_finance_handoffs]
            SET [idempotency_key] = CONCAT(N'sales-finance-handoff:', LOWER(REPLACE(CONVERT(nvarchar(36), [company_id]), N'-', N'')), N':', LOWER(REPLACE(CONVERT(nvarchar(36), [deal_id]), N'-', N'')), N':invoice')
            WHERE [idempotency_key] = N'';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_sales_finance_handoffs_company_id_idempotency_key",
            table: "sales_finance_handoffs",
            columns: new[] { "company_id", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_finance_handoffs_company_id_approval_id",
            table: "sales_finance_handoffs",
            columns: new[] { "company_id", "approval_id" },
            filter: "[approval_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_finance_handoffs_company_id_write_request_id",
            table: "sales_finance_handoffs",
            columns: new[] { "company_id", "write_request_id" },
            filter: "[write_request_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_finance_handoffs_company_id_external_system_external_document_id",
            table: "sales_finance_handoffs",
            columns: new[] { "company_id", "external_system", "external_document_id" },
            unique: true,
            filter: "[external_document_id] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_sales_finance_handoffs_company_id_idempotency_key", "sales_finance_handoffs");
        migrationBuilder.DropIndex("IX_sales_finance_handoffs_company_id_approval_id", "sales_finance_handoffs");
        migrationBuilder.DropIndex("IX_sales_finance_handoffs_company_id_write_request_id", "sales_finance_handoffs");
        migrationBuilder.DropIndex("IX_sales_finance_handoffs_company_id_external_system_external_document_id", "sales_finance_handoffs");
        migrationBuilder.DropColumn("document_type", "sales_finance_handoffs");
        migrationBuilder.DropColumn("external_system", "sales_finance_handoffs");
        migrationBuilder.DropColumn("external_document_number", "sales_finance_handoffs");
        migrationBuilder.DropColumn("idempotency_key", "sales_finance_handoffs");
        migrationBuilder.DropColumn("approval_id", "sales_finance_handoffs");
        migrationBuilder.DropColumn("write_request_id", "sales_finance_handoffs");
        migrationBuilder.DropColumn("last_error_code", "sales_finance_handoffs");
        migrationBuilder.DropColumn("execution_attempt_count", "sales_finance_handoffs");
        migrationBuilder.DropColumn("requested_at", "sales_finance_handoffs");
        migrationBuilder.DropColumn("approved_at", "sales_finance_handoffs");
        migrationBuilder.DropColumn("execution_started_at", "sales_finance_handoffs");
        migrationBuilder.DropColumn("executed_at", "sales_finance_handoffs");
        migrationBuilder.DropColumn("failed_at", "sales_finance_handoffs");
        migrationBuilder.DropColumn("retried_at", "sales_finance_handoffs");
    }
}
