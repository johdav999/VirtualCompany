using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260331173000_AddAgentOperatingProfiles")]
    public partial class AddAgentOperatingProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role_brief",
                table: "agents",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trigger_logic_json",
                table: "agents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'{}'");

            migrationBuilder.AddColumn<string>(
                name: "working_hours_json",
                table: "agents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'{}'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role_brief",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "trigger_logic_json",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "working_hours_json",
                table: "agents");
        }
    }
}
