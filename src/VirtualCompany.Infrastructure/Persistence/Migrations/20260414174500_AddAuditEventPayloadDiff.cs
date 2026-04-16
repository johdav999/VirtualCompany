using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260414174500_AddAuditEventPayloadDiff")]
public partial class AddAuditEventPayloadDiff : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var type = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"
            ? "character varying(16000)"
            : "nvarchar(max)";

        migrationBuilder.AddColumn<string>(
            name: "payload_diff_json",
            table: "audit_events",
            type: type,
            maxLength: 16000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "payload_diff_json",
            table: "audit_events");
    }
}
