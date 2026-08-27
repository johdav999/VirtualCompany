using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainDeterministicTaxFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tax_facts_json",
                table: "supplier_bill_accounting_lines",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "tax_facts_json",
                table: "customer_invoice_accounting_lines",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tax_facts_json",
                table: "supplier_bill_accounting_lines");

            migrationBuilder.DropColumn(
                name: "tax_facts_json",
                table: "customer_invoice_accounting_lines");
        }
    }
}
