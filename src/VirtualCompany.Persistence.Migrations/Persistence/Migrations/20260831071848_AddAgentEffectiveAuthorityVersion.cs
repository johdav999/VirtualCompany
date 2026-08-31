using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentEffectiveAuthorityVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EffectiveAuthorityHash",
                table: "agent_orchestration_runs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectiveAuthorityVersion",
                table: "agent_orchestration_runs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveAuthorityHash",
                table: "agent_orchestration_runs");

            migrationBuilder.DropColumn(
                name: "EffectiveAuthorityVersion",
                table: "agent_orchestration_runs");
        }
    }
}
