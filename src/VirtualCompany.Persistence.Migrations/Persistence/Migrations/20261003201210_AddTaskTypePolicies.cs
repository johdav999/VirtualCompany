using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTypePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_type_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ActiveRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UsageVersion = table.Column<int>(type: "int", nullable: false),
                    UsageDayUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActionsUsed = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_type_policies", x => x.Id);
                    table.UniqueConstraint("AK_task_type_policies_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_task_type_policies_agents_CompanyId_AgentId",
                        columns: x => new { x.CompanyId, x.AgentId },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "task_type_policy_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MaximumActionsPerDay = table.Column<int>(type: "int", nullable: false),
                    ActivatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PreviewHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PreviewInputs = table.Column<string>(type: "nvarchar(max)", maxLength: 12000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_type_policy_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_type_policy_revisions_task_type_policies_CompanyId_PolicyId",
                        columns: x => new { x.CompanyId, x.PolicyId },
                        principalTable: "task_type_policies",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_type_policies_CompanyId_AgentId_TaskType",
                table: "task_type_policies",
                columns: new[] { "CompanyId", "AgentId", "TaskType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_type_policy_revisions_CompanyId_PolicyId_Version",
                table: "task_type_policy_revisions",
                columns: new[] { "CompanyId", "PolicyId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_type_policy_revisions");

            migrationBuilder.DropTable(
                name: "task_type_policies");
        }
    }
}
