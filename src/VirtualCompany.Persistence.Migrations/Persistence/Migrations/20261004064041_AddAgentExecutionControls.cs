using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentExecutionControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_execution_admissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Boundary = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BusinessKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Confirmed = table.Column<bool>(type: "bit", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_execution_admissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_execution_admissions_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_execution_control_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Paused = table.Column<bool>(type: "bit", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_execution_control_commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_execution_control_commands_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_execution_controls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Paused = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_execution_controls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_execution_controls_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_execution_admissions_CompanyId_Boundary_BusinessKey",
                table: "agent_execution_admissions",
                columns: new[] { "CompanyId", "Boundary", "BusinessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_execution_control_commands_CompanyId_ScopeId_Version",
                table: "agent_execution_control_commands",
                columns: new[] { "CompanyId", "ScopeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_execution_controls_CompanyId_ScopeId",
                table: "agent_execution_controls",
                columns: new[] { "CompanyId", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_execution_admissions");

            migrationBuilder.DropTable(
                name: "agent_execution_control_commands");

            migrationBuilder.DropTable(
                name: "agent_execution_controls");
        }
    }
}
