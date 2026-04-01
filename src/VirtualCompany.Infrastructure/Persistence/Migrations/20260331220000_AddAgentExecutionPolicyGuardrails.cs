using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddAgentExecutionPolicyGuardrails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "autonomy_level",
            table: "agents",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "level_0");

        migrationBuilder.CreateTable(
            name: "tool_execution_attempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ToolName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ActionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Scope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "request_payload_json"),
                PolicyDecision = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "policy_decision_json"),
                ResultPayload = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "result_payload_json"),
                CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExecutedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tool_execution_attempts", x => x.Id);
                table.ForeignKey(
                    name: "FK_tool_execution_attempts_companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "approval_requests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ToolExecutionAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ToolName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ActionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ApprovalTarget = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ThresholdContext = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "threshold_context_json"),
                PolicyDecision = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'", name: "policy_decision_json"),
                CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_approval_requests", x => x.Id);
                table.ForeignKey(
                    name: "FK_approval_requests_companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_approval_requests_tool_execution_attempts_ToolExecutionAttemptId",
                    column: x => x.ToolExecutionAttemptId,
                    principalTable: "tool_execution_attempts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tool_execution_attempts_CompanyId_AgentId_CreatedUtc",
            table: "tool_execution_attempts",
            columns: new[] { "CompanyId", "AgentId", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_tool_execution_attempts_CompanyId_Status_CreatedUtc",
            table: "tool_execution_attempts",
            columns: new[] { "CompanyId", "Status", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_requests_CompanyId_Status_CreatedUtc",
            table: "approval_requests",
            columns: new[] { "CompanyId", "Status", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_requests_ToolExecutionAttemptId",
            table: "approval_requests",
            column: "ToolExecutionAttemptId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "approval_requests");

        migrationBuilder.DropTable(
            name: "tool_execution_attempts");

        migrationBuilder.DropColumn(
            name: "autonomy_level",
            table: "agents");
    }
}