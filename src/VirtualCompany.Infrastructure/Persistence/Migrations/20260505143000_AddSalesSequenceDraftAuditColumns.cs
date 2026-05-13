using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505143000_AddSalesSequenceDraftAuditColumns")]
public partial class AddSalesSequenceDraftAuditColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "original_generated_subject",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(300)",
            maxLength: 300,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "original_generated_body",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(max)",
            maxLength: 16000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "current_draft_subject",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(300)",
            maxLength: 300,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "current_draft_body",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(max)",
            maxLength: 16000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_sent_subject",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(300)",
            maxLength: 300,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "final_sent_body",
            table: "sales_sequence_execution_steps",
            type: "nvarchar(max)",
            maxLength: 16000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "generated_draft_at",
            table: "sales_sequence_execution_steps",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "draft_updated_at",
            table: "sales_sequence_execution_steps",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_execution_steps_company_id_contact_id_sent_at",
            table: "sales_sequence_execution_steps",
            columns: new[] { "company_id", "contact_id", "sent_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_sales_sequence_execution_steps_company_id_contact_id_sent_at", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "original_generated_subject", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "original_generated_body", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "current_draft_subject", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "current_draft_body", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "final_sent_subject", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "final_sent_body", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "generated_draft_at", table: "sales_sequence_execution_steps");
        migrationBuilder.DropColumn(name: "draft_updated_at", table: "sales_sequence_execution_steps");
    }
}
