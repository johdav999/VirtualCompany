using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413140000_RenameToolExecutionAttemptsToToolExecutions")]
public partial class RenameToolExecutionAttemptsToToolExecutions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(
            name: "tool_execution_attempts",
            newName: "tool_executions");

        migrationBuilder.RenameColumn(name: "Id", table: "tool_executions", newName: "id");
        migrationBuilder.RenameColumn(name: "CompanyId", table: "tool_executions", newName: "company_id");
        migrationBuilder.RenameColumn(name: "AgentId", table: "tool_executions", newName: "agent_id");
        migrationBuilder.RenameColumn(name: "TaskId", table: "tool_executions", newName: "task_id");
        migrationBuilder.RenameColumn(name: "WorkflowInstanceId", table: "tool_executions", newName: "workflow_instance_id");
        migrationBuilder.RenameColumn(name: "ToolName", table: "tool_executions", newName: "tool_name");
        migrationBuilder.RenameColumn(name: "ActionType", table: "tool_executions", newName: "action_type");
        migrationBuilder.RenameColumn(name: "Scope", table: "tool_executions", newName: "scope");
        migrationBuilder.RenameColumn(name: "Status", table: "tool_executions", newName: "status");
        migrationBuilder.RenameColumn(name: "ApprovalRequestId", table: "tool_executions", newName: "approval_request_id");
        migrationBuilder.RenameColumn(name: "CorrelationId", table: "tool_executions", newName: "correlation_id");
        migrationBuilder.RenameColumn(name: "request_payload_json", table: "tool_executions", newName: "request_json");
        migrationBuilder.RenameColumn(name: "result_payload_json", table: "tool_executions", newName: "response_json");
        migrationBuilder.RenameColumn(name: "StartedUtc", table: "tool_executions", newName: "started_at");
        migrationBuilder.RenameColumn(name: "CompletedUtc", table: "tool_executions", newName: "completed_at");
        migrationBuilder.RenameColumn(name: "CreatedUtc", table: "tool_executions", newName: "created_at");
        migrationBuilder.RenameColumn(name: "UpdatedUtc", table: "tool_executions", newName: "updated_at");
        migrationBuilder.RenameColumn(name: "ExecutedUtc", table: "tool_executions", newName: "executed_at");

        migrationBuilder.RenameIndex(
            name: "IX_tool_execution_attempts_CompanyId_CorrelationId",
            table: "tool_executions",
            newName: "IX_tool_executions_company_id_correlation_id");

        migrationBuilder.RenameIndex(
            name: "IX_tool_execution_attempts_CompanyId_AgentId_CreatedUtc",
            table: "tool_executions",
            newName: "IX_tool_executions_company_id_agent_id_started_at");

        migrationBuilder.RenameIndex(
            name: "IX_tool_execution_attempts_CompanyId_Status_CreatedUtc",
            table: "tool_executions",
            newName: "IX_tool_executions_company_id_status_started_at");

        migrationBuilder.CreateIndex(
            name: "IX_tool_executions_company_id_task_id",
            table: "tool_executions",
            columns: new[] { "company_id", "task_id" });

        migrationBuilder.CreateIndex(
            name: "IX_tool_executions_company_id_workflow_instance_id",
            table: "tool_executions",
            columns: new[] { "company_id", "workflow_instance_id" });

        migrationBuilder.CreateIndex(
            name: "IX_tool_executions_company_id_started_at",
            table: "tool_executions",
            columns: new[] { "company_id", "started_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tool_executions_company_id_task_id",
            table: "tool_executions");

        migrationBuilder.DropIndex(
            name: "IX_tool_executions_company_id_workflow_instance_id",
            table: "tool_executions");

        migrationBuilder.DropIndex(
            name: "IX_tool_executions_company_id_started_at",
            table: "tool_executions");

        migrationBuilder.RenameIndex(
            name: "IX_tool_executions_company_id_status_started_at",
            table: "tool_executions",
            newName: "IX_tool_execution_attempts_CompanyId_Status_CreatedUtc");

        migrationBuilder.RenameIndex(
            name: "IX_tool_executions_company_id_agent_id_started_at",
            table: "tool_executions",
            newName: "IX_tool_execution_attempts_CompanyId_AgentId_CreatedUtc");

        migrationBuilder.RenameIndex(
            name: "IX_tool_executions_company_id_correlation_id",
            table: "tool_executions",
            newName: "IX_tool_execution_attempts_CompanyId_CorrelationId");

        migrationBuilder.RenameColumn(name: "executed_at", table: "tool_executions", newName: "ExecutedUtc");
        migrationBuilder.RenameColumn(name: "updated_at", table: "tool_executions", newName: "UpdatedUtc");
        migrationBuilder.RenameColumn(name: "created_at", table: "tool_executions", newName: "CreatedUtc");
        migrationBuilder.RenameColumn(name: "completed_at", table: "tool_executions", newName: "CompletedUtc");
        migrationBuilder.RenameColumn(name: "started_at", table: "tool_executions", newName: "StartedUtc");
        migrationBuilder.RenameColumn(name: "response_json", table: "tool_executions", newName: "result_payload_json");
        migrationBuilder.RenameColumn(name: "request_json", table: "tool_executions", newName: "request_payload_json");
        migrationBuilder.RenameColumn(name: "correlation_id", table: "tool_executions", newName: "CorrelationId");
        migrationBuilder.RenameColumn(name: "approval_request_id", table: "tool_executions", newName: "ApprovalRequestId");
        migrationBuilder.RenameColumn(name: "status", table: "tool_executions", newName: "Status");
        migrationBuilder.RenameColumn(name: "scope", table: "tool_executions", newName: "Scope");
        migrationBuilder.RenameColumn(name: "action_type", table: "tool_executions", newName: "ActionType");
        migrationBuilder.RenameColumn(name: "tool_name", table: "tool_executions", newName: "ToolName");
        migrationBuilder.RenameColumn(name: "workflow_instance_id", table: "tool_executions", newName: "WorkflowInstanceId");
        migrationBuilder.RenameColumn(name: "task_id", table: "tool_executions", newName: "TaskId");
        migrationBuilder.RenameColumn(name: "agent_id", table: "tool_executions", newName: "AgentId");
        migrationBuilder.RenameColumn(name: "company_id", table: "tool_executions", newName: "CompanyId");
        migrationBuilder.RenameColumn(name: "id", table: "tool_executions", newName: "Id");

        migrationBuilder.RenameTable(
            name: "tool_executions",
            newName: "tool_execution_attempts");
    }
}
