using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260629120000_AddSupportAgentDomain")]
    public partial class AddSupportAgentDomain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    case_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    source = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    sentiment = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    confidence_score = table.Column<decimal>(type: "decimal(5,3)", nullable: true),
                    suggested_next_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    rationale_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    related_invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    related_payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    assigned_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    assigned_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    first_response_due_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolution_due_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_customer_message_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_internal_activity_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    first_response_sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    closed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_sla_risk = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    is_sla_breached = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    is_vip_risk = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    is_churn_risk = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    provider_thread_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    provider_message_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_cases", x => x.id);
                    table.UniqueConstraint("AK_support_cases_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey("FK_support_cases_companies_company_id", x => x.company_id, "Companies", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_sla_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    customer_tier = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    first_response_minutes = table.Column<int>(type: "int", nullable: false),
                    resolution_minutes = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_sla_policies", x => x.id);
                    table.ForeignKey("FK_support_sla_policies_companies_company_id", x => x.company_id, "Companies", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_case_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    assigned_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    assigned_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    assigned_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_case_assignments", x => x.id);
                    table.ForeignKey("FK_support_case_assignments_support_cases_company_id_support_case_id", x => new { x.company_id, x.support_case_id }, "support_cases", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_case_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    actor_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'{}'"),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_case_events", x => x.id);
                    table.ForeignKey("FK_support_case_events_support_cases_company_id_support_case_id", x => new { x.company_id, x.support_case_id }, "support_cases", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_case_resolutions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_case_resolutions", x => x.id);
                    table.ForeignKey("FK_support_case_resolutions_support_cases_company_id_support_case_id", x => new { x.company_id, x.support_case_id }, "support_cases", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_reply_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    draft_body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    tone = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,3)", nullable: false),
                    answerability = table.Column<decimal>(type: "decimal(5,3)", nullable: false),
                    rationale_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    source_references_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    created_by_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    send_failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_reply_drafts", x => x.id);
                    table.UniqueConstraint("AK_support_reply_drafts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey("FK_support_reply_drafts_support_cases_company_id_support_case_id", x => new { x.company_id, x.support_case_id }, "support_cases", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    direction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    sender = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    email_message_snapshot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider_message_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    provider_thread_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    reply_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_messages", x => x.id);
                    table.UniqueConstraint("AK_support_messages_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey("FK_support_messages_support_cases_company_id_support_case_id", x => new { x.company_id, x.support_case_id }, "support_cases", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_support_messages_support_reply_drafts_company_id_reply_draft_id", x => new { x.company_id, x.reply_draft_id }, "support_reply_drafts", new[] { "company_id", "id" }, onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "support_refund_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    finance_action_reference_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_refund_requests", x => x.id);
                    table.ForeignKey("FK_support_refund_requests_support_cases_company_id_support_case_id", x => new { x.company_id, x.support_case_id }, "support_cases", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_knowledge_gaps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    support_case_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    support_reply_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    question_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    missing_information_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    retrieval_source_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    frequency_count = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    linked_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_knowledge_gaps", x => x.id);
                    table.ForeignKey("FK_support_knowledge_gaps_support_cases_company_id_support_case_id", x => new { x.company_id, x.support_case_id }, "support_cases", new[] { "company_id", "id" }, onDelete: ReferentialAction.NoAction);
                    table.ForeignKey("FK_support_knowledge_gaps_support_reply_drafts_company_id_support_reply_draft_id", x => new { x.company_id, x.support_reply_draft_id }, "support_reply_drafts", new[] { "company_id", "id" }, onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex("IX_support_cases_company_id_case_number", "support_cases", new[] { "company_id", "case_number" }, unique: true);
            migrationBuilder.CreateIndex("IX_support_cases_company_id_status_updated_at", "support_cases", new[] { "company_id", "status", "updated_at" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_priority_status", "support_cases", new[] { "company_id", "priority", "status" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_category_created_at", "support_cases", new[] { "company_id", "category", "created_at" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_assigned_agent_id_status", "support_cases", new[] { "company_id", "assigned_agent_id", "status" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_assigned_user_id_status", "support_cases", new[] { "company_id", "assigned_user_id", "status" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_first_response_due_at", "support_cases", new[] { "company_id", "first_response_due_at" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_resolution_due_at", "support_cases", new[] { "company_id", "resolution_due_at" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_provider_thread_id", "support_cases", new[] { "company_id", "provider_thread_id" });
            migrationBuilder.CreateIndex("IX_support_cases_company_id_provider_message_id", "support_cases", new[] { "company_id", "provider_message_id" });
            migrationBuilder.CreateIndex("IX_support_messages_company_id_support_case_id_occurred_at", "support_messages", new[] { "company_id", "support_case_id", "occurred_at" });
            migrationBuilder.CreateIndex("IX_support_messages_company_id_provider_message_id", "support_messages", new[] { "company_id", "provider_message_id" }, unique: true, filter: "provider_message_id IS NOT NULL");
            migrationBuilder.CreateIndex("IX_support_messages_company_id_reply_draft_id", "support_messages", new[] { "company_id", "reply_draft_id" });
            migrationBuilder.CreateIndex("IX_support_case_events_company_id_support_case_id_occurred_at", "support_case_events", new[] { "company_id", "support_case_id", "occurred_at" });
            migrationBuilder.CreateIndex("IX_support_case_events_company_id_event_type_occurred_at", "support_case_events", new[] { "company_id", "event_type", "occurred_at" });
            migrationBuilder.CreateIndex("IX_support_case_assignments_company_id_support_case_id_assigned_at", "support_case_assignments", new[] { "company_id", "support_case_id", "assigned_at" });
            migrationBuilder.CreateIndex("IX_support_sla_policies_company_id_category_priority_customer_tier_is_active", "support_sla_policies", new[] { "company_id", "category", "priority", "customer_tier", "is_active" });
            migrationBuilder.CreateIndex("IX_support_case_resolutions_company_id_support_case_id", "support_case_resolutions", new[] { "company_id", "support_case_id" }, unique: true);
            migrationBuilder.CreateIndex("IX_support_reply_drafts_company_id_support_case_id_created_at", "support_reply_drafts", new[] { "company_id", "support_case_id", "created_at" });
            migrationBuilder.CreateIndex("IX_support_reply_drafts_company_id_status", "support_reply_drafts", new[] { "company_id", "status" });
            migrationBuilder.CreateIndex("IX_support_refund_requests_company_id_support_case_id_created_at", "support_refund_requests", new[] { "company_id", "support_case_id", "created_at" });
            migrationBuilder.CreateIndex("IX_support_refund_requests_company_id_status", "support_refund_requests", new[] { "company_id", "status" });
            migrationBuilder.CreateIndex("IX_support_knowledge_gaps_company_id_status_category", "support_knowledge_gaps", new[] { "company_id", "status", "category" });
            migrationBuilder.CreateIndex("IX_support_knowledge_gaps_company_id_support_case_id", "support_knowledge_gaps", new[] { "company_id", "support_case_id" });
            migrationBuilder.CreateIndex("IX_support_knowledge_gaps_company_id_support_reply_draft_id", "support_knowledge_gaps", new[] { "company_id", "support_reply_draft_id" });
            migrationBuilder.CreateIndex("IX_support_knowledge_gaps_company_id_category_question_summary", "support_knowledge_gaps", new[] { "company_id", "category", "question_summary" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("support_knowledge_gaps");
            migrationBuilder.DropTable("support_messages");
            migrationBuilder.DropTable("support_refund_requests");
            migrationBuilder.DropTable("support_case_assignments");
            migrationBuilder.DropTable("support_case_events");
            migrationBuilder.DropTable("support_case_resolutions");
            migrationBuilder.DropTable("support_sla_policies");
            migrationBuilder.DropTable("support_reply_drafts");
            migrationBuilder.DropTable("support_cases");
        }
    }
}

