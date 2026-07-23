using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260412203000_SeedPredefinedWorkflowDefinitions")]
public partial class SeedPredefinedWorkflowDefinitions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";

        migrationBuilder.InsertData(
            table: "workflow_definitions",
            columns: new[]
            {
                "id", "company_id", "code", "name", "department", "version", "trigger_type",
                "definition_json", "active", "created_at", "updated_at"
            },
            columnTypes: new[]
            {
                isPostgres ? "uuid" : "uniqueidentifier",
                isPostgres ? "uuid" : "uniqueidentifier",
                isPostgres ? "character varying(100)" : "nvarchar(100)",
                isPostgres ? "character varying(200)" : "nvarchar(200)",
                isPostgres ? "character varying(100)" : "nvarchar(100)",
                "int",
                isPostgres ? "character varying(32)" : "nvarchar(32)",
                jsonType,
                isPostgres ? "boolean" : "bit",
                isPostgres ? "timestamp with time zone" : "datetime2",
                isPostgres ? "timestamp with time zone" : "datetime2"
            },
            values: new object[,]
            {
                {
                    Guid.Parse("f33d253c-3d80-46d5-a43c-68f2a5a72f01"),
                    null,
                    "DAILY-EXECUTIVE-BRIEFING",
                    "Daily executive briefing",
                    "Executive",
                    1,
                    "schedule",
                    """{"schema":"predefined-workflow-v1","schemaVersion":1,"templateCode":"DAILY-EXECUTIVE-BRIEFING","description":"Prepare a concise cross-functional briefing from active company signals.","schedule":{"scheduleKey":"daily-executive-briefing","timezone":"company-default"},"steps":[{"id":"collect-signals","name":"Collect company signals","handler":"collect_signals"},{"id":"summarize-briefing","name":"Summarize briefing","handler":"summarize_briefing"},{"id":"publish-briefing","name":"Publish briefing","handler":"publish_briefing"}]}""",
                    true,
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime(),
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime()
                },
                {
                    Guid.Parse("4810f5bb-2eaa-42d0-a650-754f9616cc02"),
                    null,
                    "INVOICE-APPROVAL-REVIEW",
                    "Invoice approval review",
                    "Finance",
                    1,
                    "event",
                    """{"schema":"predefined-workflow-v1","schemaVersion":1,"templateCode":"INVOICE-APPROVAL-REVIEW","description":"Route a new invoice signal into a review-ready approval workflow.","event":{"eventName":"invoice.received"},"steps":[{"id":"capture-invoice","name":"Capture invoice context","handler":"capture_invoice"},{"id":"prepare-review","name":"Prepare review packet","handler":"prepare_review"},{"id":"request-approval","name":"Request approval","handler":"request_approval"}]}""",
                    true,
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime(),
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime()
                },
                {
                    Guid.Parse("af11bff4-01dc-4a85-91cf-68a35bfa5ce3"),
                    null,
                    "SUPPORT-ESCALATION-TRIAGE",
                    "Support escalation triage",
                    "Support",
                    1,
                    "event",
                    """{"schema":"predefined-workflow-v1","schemaVersion":1,"templateCode":"SUPPORT-ESCALATION-TRIAGE","description":"Triage an escalated support case and prepare the next owner handoff.","event":{"eventName":"support.case.escalated"},"steps":[{"id":"capture-case","name":"Capture case context","handler":"capture_case"},{"id":"classify-escalation","name":"Classify escalation","handler":"classify_escalation"},{"id":"assign-owner","name":"Assign owner","handler":"assign_owner"}]}""",
                    true,
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime(),
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime()
                },
                {
                    Guid.Parse("ec1f7bb3-1f3f-4f56-bba6-f403bd02ea04"),
                    null,
                    "LEAD-FOLLOW-UP",
                    "Lead follow-up",
                    "Sales",
                    1,
                    "manual",
                    """{"schema":"predefined-workflow-v1","schemaVersion":1,"templateCode":"LEAD-FOLLOW-UP","description":"Start a focused lead follow-up sequence for sales teams.","steps":[{"id":"qualify-lead","name":"Qualify lead","handler":"qualify_lead"},{"id":"draft-follow-up","name":"Draft follow-up","handler":"draft_follow_up"},{"id":"schedule-next-action","name":"Schedule next action","handler":"schedule_next_action"}]}""",
                    true,
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime(),
                    DateTime.Parse("2026-04-12T20:30:00Z").ToUniversalTime()
                }
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(table: "workflow_definitions", keyColumn: "id", keyValue: Guid.Parse("f33d253c-3d80-46d5-a43c-68f2a5a72f01"));
        migrationBuilder.DeleteData(table: "workflow_definitions", keyColumn: "id", keyValue: Guid.Parse("4810f5bb-2eaa-42d0-a650-754f9616cc02"));
        migrationBuilder.DeleteData(table: "workflow_definitions", keyColumn: "id", keyValue: Guid.Parse("af11bff4-01dc-4a85-91cf-68a35bfa5ce3"));
        migrationBuilder.DeleteData(table: "workflow_definitions", keyColumn: "id", keyValue: Guid.Parse("ec1f7bb3-1f3f-4f56-bba6-f403bd02ea04"));
    }
}
