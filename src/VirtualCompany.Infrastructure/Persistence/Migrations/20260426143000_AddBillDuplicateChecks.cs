using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddBillDuplicateChecks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bill_duplicate_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    supplier_org_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    invoice_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    is_duplicate = table.Column<bool>(type: "bit", nullable: false),
                    matched_bill_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    criteria_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    source_email_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    source_attachment_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    checked_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_duplicate_checks", x => x.id);
                    table.ForeignKey(
                        name: "FK_bill_duplicate_checks_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bill_duplicate_checks_company_id_checked_at",
                table: "bill_duplicate_checks",
                columns: new[] { "company_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bill_duplicate_checks_company_id_invoice_number_total_amount",
                table: "bill_duplicate_checks",
                columns: new[] { "company_id", "invoice_number", "total_amount" });

            migrationBuilder.CreateIndex(
                name: "IX_bill_duplicate_checks_company_id_supplier_name_invoice_number_total_amount",
                table: "bill_duplicate_checks",
                columns: new[] { "company_id", "supplier_name", "invoice_number", "total_amount" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "bill_duplicate_checks");
        }
    }
}
