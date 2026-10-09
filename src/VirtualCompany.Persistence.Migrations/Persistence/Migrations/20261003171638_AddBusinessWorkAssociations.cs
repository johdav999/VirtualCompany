using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessWorkAssociations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "business_bill_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_brief_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_campaign_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_case_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_deal_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "business_invoice_id",
                table: "tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE t SET business_deal_id = r.id
                FROM tasks t INNER JOIN [deals] r ON r.company_id = t.company_id AND r.id = TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.dealId'));
                """);
            migrationBuilder.Sql("""
                UPDATE t SET business_case_id = r.id
                FROM tasks t INNER JOIN [support_cases] r ON r.company_id = t.company_id AND r.id = COALESCE(TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.caseId')), TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.supportCaseId')));
                """);
            migrationBuilder.Sql("""
                UPDATE t SET business_invoice_id = r.id
                FROM tasks t INNER JOIN [finance_invoices] r ON r.company_id = t.company_id AND r.id = TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.invoiceId'));
                """);
            migrationBuilder.Sql("""
                UPDATE t SET business_bill_id = r.id
                FROM tasks t INNER JOIN [finance_bills] r ON r.company_id = t.company_id AND r.id = TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.billId'));
                UPDATE t SET business_bill_id = r.id
                FROM tasks t INNER JOIN [detected_bills] r ON r.company_id = t.company_id AND r.id = TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.billId'))
                WHERE t.business_bill_id IS NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE t SET business_campaign_id = r.id
                FROM tasks t INNER JOIN [sales_campaigns] r ON r.company_id = t.company_id AND r.id = TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.campaignId'));
                """);
            migrationBuilder.Sql("""
                UPDATE t SET business_brief_id = r.id
                FROM tasks t INNER JOIN [marketing_content_briefs] r ON r.company_id = t.company_id AND r.id = TRY_CONVERT(uniqueidentifier, JSON_VALUE(CASE WHEN ISJSON(t.input_payload) = 1 THEN t.input_payload ELSE N'{}' END, '$.briefId'));
                """);
            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_business_bill_id",
                table: "tasks",
                columns: new[] { "company_id", "business_bill_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_business_brief_id",
                table: "tasks",
                columns: new[] { "company_id", "business_brief_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_business_campaign_id",
                table: "tasks",
                columns: new[] { "company_id", "business_campaign_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_business_case_id",
                table: "tasks",
                columns: new[] { "company_id", "business_case_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_business_deal_id",
                table: "tasks",
                columns: new[] { "company_id", "business_deal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_business_invoice_id",
                table: "tasks",
                columns: new[] { "company_id", "business_invoice_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_company_id_business_bill_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_company_id_business_brief_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_company_id_business_campaign_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_company_id_business_case_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_company_id_business_deal_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_company_id_business_invoice_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "business_bill_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "business_brief_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "business_campaign_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "business_case_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "business_deal_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "business_invoice_id",
                table: "tasks");
        }
    }
}
