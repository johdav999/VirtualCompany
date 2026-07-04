using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524203000_AddImageMailboxBillSourceType")]
    public partial class AddImageMailboxBillSourceType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_email_message_snapshots_source_type",
                table: "email_message_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_email_attachment_snapshots_source_type",
                table: "email_attachment_snapshots");

            migrationBuilder.AddCheckConstraint(
                name: "CK_email_message_snapshots_source_type",
                table: "email_message_snapshots",
                sql: "source_type IN ('pdf_attachment', 'docx_attachment', 'email_body_only', 'image_attachment')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_email_attachment_snapshots_source_type",
                table: "email_attachment_snapshots",
                sql: "source_type IN ('pdf_attachment', 'docx_attachment', 'email_body_only', 'image_attachment')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_email_message_snapshots_source_type",
                table: "email_message_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_email_attachment_snapshots_source_type",
                table: "email_attachment_snapshots");

            migrationBuilder.AddCheckConstraint(
                name: "CK_email_message_snapshots_source_type",
                table: "email_message_snapshots",
                sql: "source_type IN ('pdf_attachment', 'docx_attachment', 'email_body_only')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_email_attachment_snapshots_source_type",
                table: "email_attachment_snapshots",
                sql: "source_type IN ('pdf_attachment', 'docx_attachment', 'email_body_only')");
        }
    }
}
