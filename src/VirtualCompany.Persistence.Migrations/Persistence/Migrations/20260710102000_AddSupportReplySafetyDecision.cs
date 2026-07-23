using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260710102000_AddSupportReplySafetyDecision")]
    public partial class AddSupportReplySafetyDecision : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "safety_decision",
                table: "support_reply_drafts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "safety_evaluated_at",
                table: "support_reply_drafts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "safety_policy_version",
                table: "support_reply_drafts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "safety_reason_codes_json",
                table: "support_reply_drafts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "safety_decision", table: "support_reply_drafts");
            migrationBuilder.DropColumn(name: "safety_evaluated_at", table: "support_reply_drafts");
            migrationBuilder.DropColumn(name: "safety_policy_version", table: "support_reply_drafts");
            migrationBuilder.DropColumn(name: "safety_reason_codes_json", table: "support_reply_drafts");
        }
    }
}
