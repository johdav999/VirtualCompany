using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddToolExecutionVersionAndDenialReason : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tool_version",
                table: "tool_executions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.0.0");

            migrationBuilder.AddColumn<string>(
                name: "denial_reason",
                table: "tool_executions",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tool_version",
                table: "tool_executions");

            migrationBuilder.DropColumn(
                name: "denial_reason",
                table: "tool_executions");
        }
    }
}