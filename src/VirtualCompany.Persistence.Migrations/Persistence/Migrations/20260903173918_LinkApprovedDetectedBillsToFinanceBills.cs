using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkApprovedDetectedBillsToFinanceBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_detected_bill_id",
                table: "finance_bills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_bills_company_id_source_detected_bill_id",
                table: "finance_bills",
                columns: new[] { "company_id", "source_detected_bill_id" },
                unique: true,
                filter: "source_detected_bill_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_finance_bills_detected_bills_company_id_source_detected_bill_id",
                table: "finance_bills",
                columns: new[] { "company_id", "source_detected_bill_id" },
                principalTable: "detected_bills",
                principalColumns: new[] { "company_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_finance_bills_detected_bills_company_id_source_detected_bill_id",
                table: "finance_bills");

            migrationBuilder.DropIndex(
                name: "IX_finance_bills_company_id_source_detected_bill_id",
                table: "finance_bills");

            migrationBuilder.DropColumn(
                name: "source_detected_bill_id",
                table: "finance_bills");
        }
    }
}
