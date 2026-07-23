using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddAgentManagement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PersonaSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultSeniority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Personality = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "personality_json"),
                    Objectives = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "objectives_json"),
                    Kpis = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "kpis_json"),
                    Tools = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "tool_permissions_json"),
                    Scopes = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "data_scopes_json"),
                    Thresholds = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "approval_thresholds_json"),
                    EscalationRules = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "escalation_rules_json"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Seniority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "active"),
                    Personality = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "personality_json"),
                    Objectives = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "objectives_json"),
                    Kpis = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "kpis_json"),
                    Tools = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "tool_permissions_json"),
                    Scopes = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "data_scopes_json"),
                    Thresholds = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "approval_thresholds_json"),
                    EscalationRules = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "escalation_rules_json"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agents_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_templates_IsActive_SortOrder",
                table: "agent_templates",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_templates_TemplateId",
                table: "agent_templates",
                column: "TemplateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agents_CompanyId_Department",
                table: "agents",
                columns: new[] { "CompanyId", "Department" });

            migrationBuilder.CreateIndex(
                name: "IX_agents_CompanyId_DisplayName",
                table: "agents",
                columns: new[] { "CompanyId", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_agents_CompanyId_Status",
                table: "agents",
                columns: new[] { "CompanyId", "Status" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "agent_templates");
        }
    }
}