using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

public partial class AddCustomerInvoiceEmailFallbackR2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "fallback_provider_key",
            table: "customer_invoice_email_deliveries",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "fallback_reason_code",
            table: "customer_invoice_email_deliveries",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "request_source",
            table: "customer_invoice_email_deliveries",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "direct");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "fallback_provider_key", table: "customer_invoice_email_deliveries");
        migrationBuilder.DropColumn(name: "fallback_reason_code", table: "customer_invoice_email_deliveries");
        migrationBuilder.DropColumn(name: "request_source", table: "customer_invoice_email_deliveries");
    }
}
