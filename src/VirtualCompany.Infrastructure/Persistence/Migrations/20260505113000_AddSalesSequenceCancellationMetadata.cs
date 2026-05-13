using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505113000_AddSalesSequenceCancellationMetadata")]
public partial class AddSalesSequenceCancellationMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "cancellation_reason",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "cancellation_source_reference",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_execution_steps_company_id_contact_id_cancellation_reason",
            table: "sales_sequence_execution_steps",
            columns: new[] { "company_id", "contact_id", "cancellation_reason" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_sales_sequence_execution_steps_company_id_contact_id_cancellation_reason",
            table: "sales_sequence_execution_steps");

        migrationBuilder.DropColumn(
            name: "cancellation_reason",
            table: "sales_sequence_execution_steps");

        migrationBuilder.DropColumn(
            name: "cancellation_source_reference",
            table: "sales_sequence_execution_steps");
    }
}
