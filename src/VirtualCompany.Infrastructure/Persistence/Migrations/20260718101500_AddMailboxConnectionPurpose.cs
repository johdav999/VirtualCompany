using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260718101500_AddMailboxConnectionPurpose")]
public partial class AddMailboxConnectionPurpose : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "purpose",
            table: "mailbox_connections",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "finance");

        migrationBuilder.AddCheckConstraint(
            name: "CK_mailbox_connections_purpose",
            table: "mailbox_connections",
            sql: "purpose IN ('finance', 'sales', 'support')");

        migrationBuilder.CreateIndex(
            name: "IX_mailbox_connections_company_id_user_id_purpose_updated_at",
            table: "mailbox_connections",
            columns: new[] { "company_id", "user_id", "purpose", "updated_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_mailbox_connections_company_id_user_id_purpose_updated_at",
            table: "mailbox_connections");

        migrationBuilder.DropCheckConstraint(
            name: "CK_mailbox_connections_purpose",
            table: "mailbox_connections");

        migrationBuilder.DropColumn(
            name: "purpose",
            table: "mailbox_connections");
    }
}
