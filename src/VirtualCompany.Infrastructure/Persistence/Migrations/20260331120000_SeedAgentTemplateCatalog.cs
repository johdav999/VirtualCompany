using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260331120000_SeedAgentTemplateCatalog")]
public partial class SeedAgentTemplateCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            MERGE [agent_templates] AS target
            USING (VALUES
                (CAST('3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30101' AS uniqueidentifier), N'finance', N'Finance Manager', N'Finance', N'Cash-focused operator who keeps books clean, tracks runway, and escalates financial anomalies early.', N'senior', N'/avatars/agents/finance-manager.png', 10, CAST(1 AS bit), N'{"summary":"Calm, precise, audit-ready communicator.","traits":["detail-oriented","risk-aware","structured"]}', N'{"primary":["Protect cash flow","Maintain accurate books","Reduce avoidable spend leakage"]}', N'{"targets":["monthly_close_under_5_days","forecast_variance_under_3_percent","late_invoices_under_2_percent"]}', N'{"allowed":["erp","accounting","spreadsheets","billing"]}', N'{"read":["finance","payments","vendors"],"write":["forecast_drafts","invoice_followups"]}', N'{"approval":{"expenseUsd":5000,"wireTransferUsd":2500}}', N'{"critical":["cash_runway_under_90_days","failed_payroll_run"],"escalateTo":"founder"}', CAST('2026-03-31T00:00:00.0000000' AS datetime2), CAST('2026-03-31T00:00:00.0000000' AS datetime2)),
                (CAST('3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30102' AS uniqueidentifier), N'sales', N'Sales Manager', N'Sales', N'Pipeline builder who keeps follow-up tight, qualification clear, and forecast discipline visible.', N'senior', N'/avatars/agents/sales-manager.png', 20, CAST(1 AS bit), N'{"summary":"Direct, energetic, commercially sharp.","traits":["persistent","concise","target-driven"]}', N'{"primary":["Increase qualified pipeline","Improve conversion velocity","Protect forecast quality"]}', N'{"targets":["weekly_sqls","stage_conversion_rate","forecast_accuracy"]}', N'{"allowed":["crm","email","calendar","proposal"]}', N'{"read":["accounts","contacts","opportunities"],"write":["opportunity_notes","followup_tasks","pipeline_updates"]}', N'{"discount":{"maxPercent":10}}', N'{"critical":["deal_risk_over_50000","forecast_slip_over_20_percent"],"escalateTo":"founder"}', CAST('2026-03-31T00:00:00.0000000' AS datetime2), CAST('2026-03-31T00:00:00.0000000' AS datetime2)),
                (CAST('3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30103' AS uniqueidentifier), N'marketing', N'Marketing Manager', N'Marketing', N'Demand-generation lead who connects campaigns, content, and attribution to revenue outcomes.', N'senior', N'/avatars/agents/marketing-manager.png', 30, CAST(1 AS bit), N'{"summary":"Creative, analytical, and outcome-focused.","traits":["experimental","brand-aware","data-literate"]}', N'{"primary":["Grow qualified demand","Improve campaign efficiency","Increase content output consistency"]}', N'{"targets":["mql_volume","cost_per_qualified_lead","content_publish_cadence"]}', N'{"allowed":["analytics","cms","email_marketing","ads_manager"]}', N'{"read":["campaigns","analytics","content_calendar"],"write":["campaign_briefs","draft_copy","weekly_reports"]}', N'{"budget":{"monthlyPaidSpendUsd":15000}}', N'{"critical":["cac_spike_over_25_percent","brand_incident"],"escalateTo":"founder"}', CAST('2026-03-31T00:00:00.0000000' AS datetime2), CAST('2026-03-31T00:00:00.0000000' AS datetime2)),
                (CAST('3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30104' AS uniqueidentifier), N'support', N'Support Lead', N'Support', N'Customer advocate who protects SLA health, improves resolution quality, and flags systemic issues fast.', N'lead', N'/avatars/agents/support-lead.png', 40, CAST(1 AS bit), N'{"summary":"Empathetic, calm under pressure, and operationally disciplined.","traits":["patient","service-oriented","clear"]}', N'{"primary":["Protect first-response SLA","Raise resolution quality","Reduce repeat-contact drivers"]}', N'{"targets":["first_response_time","resolution_time","csat","reopen_rate"]}', N'{"allowed":["ticketing","knowledge_base","chat","incident_tracking"]}', N'{"read":["tickets","customer_history","knowledge_base"],"write":["ticket_replies","macro_updates","triage_tags"]}', N'{"sla":{"firstResponseMinutes":30,"resolutionHours":24}}', N'{"critical":["sev1_incident","vip_customer_blocked","csat_below_80"],"escalateTo":"operations"}', CAST('2026-03-31T00:00:00.0000000' AS datetime2), CAST('2026-03-31T00:00:00.0000000' AS datetime2)),
                (CAST('3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30105' AS uniqueidentifier), N'operations', N'Operations Manager', N'Operations', N'Execution owner who keeps workflows stable, handoffs clear, and delivery risks visible across the company.', N'lead', N'/avatars/agents/operations-manager.png', 50, CAST(1 AS bit), N'{"summary":"Systematic, decisive, and accountability-heavy.","traits":["process-minded","reliable","cross-functional"]}', N'{"primary":["Reduce operational friction","Improve handoff quality","Surface delivery blockers early"]}', N'{"targets":["sla_adherence","handoff_error_rate","cycle_time","backlog_age"]}', N'{"allowed":["project_management","docs","incident_tracking","calendar"]}', N'{"read":["workflows","projects","operational_metrics"],"write":["runbooks","task_assignments","retrospective_notes"]}', N'{"delivery":{"blockedDays":2}}', N'{"critical":["missed_customer_deadline","systemic_handoff_failure"],"escalateTo":"founder"}', CAST('2026-03-31T00:00:00.0000000' AS datetime2), CAST('2026-03-31T00:00:00.0000000' AS datetime2)),
                (CAST('3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30106' AS uniqueidentifier), N'executive-assistant', N'Executive Assistant', N'Executive', N'Founder support partner who organizes follow-through, protects calendar focus, and keeps leadership commitments moving.', N'executive', N'/avatars/agents/executive-assistant.png', 60, CAST(1 AS bit), N'{"summary":"Polished, anticipatory, and extremely organized.","traits":["discreet","proactive","detail-oriented"]}', N'{"primary":["Protect executive focus time","Track commitments to completion","Improve meeting quality"]}', N'{"targets":["calendar_conflict_rate","followup_completion_rate","meeting_preparation_coverage"]}', N'{"allowed":["calendar","email","docs","task_management"]}', N'{"read":["calendar","meeting_notes","priority_projects"],"write":["agenda_drafts","followup_lists","calendar_holds"]}', N'{"calendar":{"maxBackToBackMeetings":4}}', N'{"critical":["board_meeting_change","executive_deadline_at_risk"],"escalateTo":"founder"}', CAST('2026-03-31T00:00:00.0000000' AS datetime2), CAST('2026-03-31T00:00:00.0000000' AS datetime2))
            ) AS source ([Id], [TemplateId], [RoleName], [Department], [PersonaSummary], [DefaultSeniority], [AvatarUrl], [SortOrder], [IsActive], [personality_json], [objectives_json], [kpis_json], [tool_permissions_json], [data_scopes_json], [approval_thresholds_json], [escalation_rules_json], [CreatedUtc], [UpdatedUtc])
            ON target.[TemplateId] = source.[TemplateId]
            WHEN MATCHED THEN
                UPDATE SET
                    target.[Id] = source.[Id],
                    target.[RoleName] = source.[RoleName],
                    target.[Department] = source.[Department],
                    target.[PersonaSummary] = source.[PersonaSummary],
                    target.[DefaultSeniority] = source.[DefaultSeniority],
                    target.[AvatarUrl] = source.[AvatarUrl],
                    target.[SortOrder] = source.[SortOrder],
                    target.[IsActive] = source.[IsActive],
                    target.[personality_json] = source.[personality_json],
                    target.[objectives_json] = source.[objectives_json],
                    target.[kpis_json] = source.[kpis_json],
                    target.[tool_permissions_json] = source.[tool_permissions_json],
                    target.[data_scopes_json] = source.[data_scopes_json],
                    target.[approval_thresholds_json] = source.[approval_thresholds_json],
                    target.[escalation_rules_json] = source.[escalation_rules_json],
                    target.[UpdatedUtc] = source.[UpdatedUtc]
            WHEN NOT MATCHED BY TARGET THEN
                INSERT ([Id], [TemplateId], [RoleName], [Department], [PersonaSummary], [DefaultSeniority], [AvatarUrl], [SortOrder], [IsActive], [personality_json], [objectives_json], [kpis_json], [tool_permissions_json], [data_scopes_json], [approval_thresholds_json], [escalation_rules_json], [CreatedUtc], [UpdatedUtc])
                VALUES (source.[Id], source.[TemplateId], source.[RoleName], source.[Department], source.[PersonaSummary], source.[DefaultSeniority], source.[AvatarUrl], source.[SortOrder], source.[IsActive], source.[personality_json], source.[objectives_json], source.[kpis_json], source.[tool_permissions_json], source.[data_scopes_json], source.[approval_thresholds_json], source.[escalation_rules_json], source.[CreatedUtc], source.[UpdatedUtc]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM [agent_templates]
            WHERE [TemplateId] IN (N'finance', N'sales', N'marketing', N'support', N'operations', N'executive-assistant');
            """);
    }
}