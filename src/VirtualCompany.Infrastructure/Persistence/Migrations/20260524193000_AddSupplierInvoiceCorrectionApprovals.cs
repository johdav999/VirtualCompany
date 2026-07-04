using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524193000_AddSupplierInvoiceCorrectionApprovals")]
    public partial class AddSupplierInvoiceCorrectionApprovals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "approval_request_id",
                table: "supplier_invoice_correction_actions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_user_id",
                table: "supplier_invoice_correction_actions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                table: "supplier_invoice_correction_actions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "task_id",
                table: "supplier_invoice_correction_actions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_correction_actions_company_id_approval_request_id",
                table: "supplier_invoice_correction_actions",
                columns: new[] { "company_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_correction_actions_approval_request_id",
                table: "supplier_invoice_correction_actions",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_correction_actions_task_id",
                table: "supplier_invoice_correction_actions",
                column: "task_id");

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoice_correction_actions_approval_requests_approval_request_id",
                table: "supplier_invoice_correction_actions",
                column: "approval_request_id",
                principalTable: "approval_requests",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_invoice_correction_actions_work_tasks_task_id",
                table: "supplier_invoice_correction_actions",
                column: "task_id",
                principalTable: "tasks",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoice_correction_actions_approval_requests_approval_request_id",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_invoice_correction_actions_work_tasks_task_id",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_correction_actions_company_id_approval_request_id",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_correction_actions_approval_request_id",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropIndex(
                name: "IX_supplier_invoice_correction_actions_task_id",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "supplier_invoice_correction_actions");

            migrationBuilder.DropColumn(
                name: "task_id",
                table: "supplier_invoice_correction_actions");
        }
    }
}
