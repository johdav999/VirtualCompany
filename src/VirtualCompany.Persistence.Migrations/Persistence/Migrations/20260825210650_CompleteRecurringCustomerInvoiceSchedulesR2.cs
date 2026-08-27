using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteRecurringCustomerInvoiceSchedulesR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customer_invoice_schedule_occurrences_company_id_status_lease_expires_utc",
                table: "customer_invoice_schedule_occurrences");

            migrationBuilder.AddColumn<Guid>(
                name: "approval_request_id",
                table: "customer_invoice_schedules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_template_hash",
                table: "customer_invoice_schedules",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approval_template_version",
                table: "customer_invoice_schedules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_hash",
                table: "customer_invoice_schedules",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "0000000000000000000000000000000000000000000000000000000000000000");

            migrationBuilder.AddColumn<long>(
                name: "template_version",
                table: "customer_invoice_schedules",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_utc",
                table: "customer_invoice_schedule_occurrences",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_hash",
                table: "customer_invoice_schedule_occurrences",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "0000000000000000000000000000000000000000000000000000000000000000");

            migrationBuilder.AddColumn<long>(
                name: "template_version",
                table: "customer_invoice_schedule_occurrences",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "customer_invoice_schedule_occurrences",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql("""
                UPDATE occurrence
                SET occurrence.template_hash = schedule.template_hash,
                    occurrence.template_version = schedule.template_version
                FROM customer_invoice_schedule_occurrences AS occurrence
                INNER JOIN customer_invoice_schedules AS schedule
                    ON schedule.company_id = occurrence.company_id
                    AND schedule.id = occurrence.schedule_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedules_company_id_approval_request_id",
                table: "customer_invoice_schedules",
                columns: new[] { "company_id", "approval_request_id" },
                unique: true,
                filter: "approval_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_occurrences_company_id_status_next_attempt_utc_lease_expires_utc",
                table: "customer_invoice_schedule_occurrences",
                columns: new[] { "company_id", "status", "next_attempt_utc", "lease_expires_utc" });

            migrationBuilder.AddForeignKey(
                name: "FK_customer_invoice_schedules_approval_requests_company_id_approval_request_id",
                table: "customer_invoice_schedules",
                columns: new[] { "company_id", "approval_request_id" },
                principalTable: "approval_requests",
                principalColumns: new[] { "CompanyId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customer_invoice_schedules_approval_requests_company_id_approval_request_id",
                table: "customer_invoice_schedules");

            migrationBuilder.DropIndex(
                name: "IX_customer_invoice_schedules_company_id_approval_request_id",
                table: "customer_invoice_schedules");

            migrationBuilder.DropIndex(
                name: "IX_customer_invoice_schedule_occurrences_company_id_status_next_attempt_utc_lease_expires_utc",
                table: "customer_invoice_schedule_occurrences");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                table: "customer_invoice_schedules");

            migrationBuilder.DropColumn(
                name: "approval_template_hash",
                table: "customer_invoice_schedules");

            migrationBuilder.DropColumn(
                name: "approval_template_version",
                table: "customer_invoice_schedules");

            migrationBuilder.DropColumn(
                name: "template_hash",
                table: "customer_invoice_schedules");

            migrationBuilder.DropColumn(
                name: "template_version",
                table: "customer_invoice_schedules");

            migrationBuilder.DropColumn(
                name: "next_attempt_utc",
                table: "customer_invoice_schedule_occurrences");

            migrationBuilder.DropColumn(
                name: "template_hash",
                table: "customer_invoice_schedule_occurrences");

            migrationBuilder.DropColumn(
                name: "template_version",
                table: "customer_invoice_schedule_occurrences");

            migrationBuilder.DropColumn(
                name: "version",
                table: "customer_invoice_schedule_occurrences");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_schedule_occurrences_company_id_status_lease_expires_utc",
                table: "customer_invoice_schedule_occurrences",
                columns: new[] { "company_id", "status", "lease_expires_utc" });
        }
    }
}
