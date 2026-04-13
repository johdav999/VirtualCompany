using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413190000_AddAuditEventStructuredDataSources")]
public partial class AddAuditEventStructuredDataSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "data_sources_used_json",
            table: "audit_events",
            type: "nvarchar(max)",
            nullable: false,
            defaultValueSql: "N'[]'");

        migrationBuilder.AddColumn<Guid>(
            name: "RelatedAgentId",
            table: "audit_events",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "RelatedTaskId",
            table: "audit_events",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "RelatedWorkflowInstanceId",
            table: "audit_events",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "RelatedApprovalRequestId",
            table: "audit_events",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "RelatedToolExecutionAttemptId",
            table: "audit_events",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE audit_events
            SET RelatedAgentId = COALESCE(
                CASE WHEN ActorType = N'agent' THEN ActorId ELSE NULL END,
                CASE WHEN TargetType = N'agent' THEN TRY_CONVERT(uniqueidentifier, TargetId) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE(metadata_json, '$.agentId')))
            """);

        migrationBuilder.Sql("""
            UPDATE audit_events
            SET RelatedTaskId = COALESCE(
                CASE WHEN TargetType = N'work_task' THEN TRY_CONVERT(uniqueidentifier, TargetId) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE(metadata_json, '$.taskId')),
                TRY_CONVERT(uniqueidentifier, JSON_VALUE(metadata_json, '$.workTaskId')))
            """);

        migrationBuilder.Sql("""
            UPDATE audit_events
            SET RelatedWorkflowInstanceId = COALESCE(
                CASE WHEN TargetType = N'workflow_instance' THEN TRY_CONVERT(uniqueidentifier, TargetId) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE(metadata_json, '$.workflowInstanceId')))
            """);

        migrationBuilder.Sql("""
            UPDATE audit_events
            SET RelatedApprovalRequestId = COALESCE(
                CASE WHEN TargetType = N'approval_request' THEN TRY_CONVERT(uniqueidentifier, TargetId) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE(metadata_json, '$.approvalRequestId')))
            """);

        migrationBuilder.Sql("""
            UPDATE audit_events
            SET RelatedToolExecutionAttemptId = COALESCE(
                CASE WHEN TargetType = N'agent_tool_execution' THEN TRY_CONVERT(uniqueidentifier, TargetId) ELSE NULL END,
                TRY_CONVERT(uniqueidentifier, JSON_VALUE(metadata_json, '$.toolExecutionId')),
                TRY_CONVERT(uniqueidentifier, JSON_VALUE(metadata_json, '$.toolExecutionAttemptId')))
            """);

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_ActorType_ActorId",
            table: "audit_events",
            columns: new[] { "CompanyId", "ActorType", "ActorId" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_RelatedAgentId_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "RelatedAgentId", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_RelatedTaskId_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "RelatedTaskId", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_RelatedWorkflowInstanceId_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "RelatedWorkflowInstanceId", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_RelatedApprovalRequestId_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "RelatedApprovalRequestId", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_RelatedToolExecutionAttemptId_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "RelatedToolExecutionAttemptId", "OccurredUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_ActorType_ActorId",
            table: "audit_events");

        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_RelatedAgentId_OccurredUtc",
            table: "audit_events");

        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_RelatedTaskId_OccurredUtc",
            table: "audit_events");

        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_RelatedWorkflowInstanceId_OccurredUtc",
            table: "audit_events");

        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_RelatedApprovalRequestId_OccurredUtc",
            table: "audit_events");

        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_RelatedToolExecutionAttemptId_OccurredUtc",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "data_sources_used_json",
            table: "audit_events");

        migrationBuilder.DropColumn(name: "RelatedAgentId", table: "audit_events");

        migrationBuilder.DropColumn(name: "RelatedTaskId", table: "audit_events");

        migrationBuilder.DropColumn(name: "RelatedWorkflowInstanceId", table: "audit_events");

        migrationBuilder.DropColumn(name: "RelatedApprovalRequestId", table: "audit_events");

        migrationBuilder.DropColumn(name: "RelatedToolExecutionAttemptId", table: "audit_events");
    }
}
