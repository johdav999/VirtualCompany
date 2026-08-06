using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_channel_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sales_campaign_activity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    metric_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    period_start_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    period_end_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    retrieved_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_channel_observations", x => x.id);
                    table.UniqueConstraint("AK_marketing_channel_observations_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_content_briefs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    marketing_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    audience = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    tone = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    call_to_action = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_content_briefs", x => x.id);
                    table.UniqueConstraint("AK_marketing_content_briefs_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_content_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_content_brief_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    source_references = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    generated_by_ai = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_content_variants", x => x.id);
                    table.UniqueConstraint("AK_marketing_content_variants_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_experiments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    hypothesis = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    primary_metric = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    guardrail_metric = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    minimum_sample_size = table.Column<int>(type: "int", nullable: false),
                    starts_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ends_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    decision = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_experiments", x => x.id);
                    table.UniqueConstraint("AK_marketing_experiments_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    objective_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    target_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    baseline_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    period_start_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    period_end_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_objectives", x => x.id);
                    table.UniqueConstraint("AK_marketing_objectives_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_plan_objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_plan_objectives", x => x.id);
                    table.UniqueConstraint("AK_marketing_plan_objectives_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    starts_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ends_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    planned_budget = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    budget_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_plans", x => x.id);
                    table.UniqueConstraint("AK_marketing_plans_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_sales_handoffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    linked_lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    linked_deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    suggested_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    urgency = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    evidence_references = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    decision_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_sales_handoffs", x => x.id);
                    table.UniqueConstraint("AK_marketing_sales_handoffs_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_observations_company_id",
                table: "marketing_channel_observations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_observations_company_id_idempotency_key",
                table: "marketing_channel_observations",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_observations_company_id_sales_campaign_id_metric_code_period_end_at",
                table: "marketing_channel_observations",
                columns: new[] { "company_id", "sales_campaign_id", "metric_code", "period_end_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_content_briefs_company_id",
                table: "marketing_content_briefs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_content_briefs_company_id_status_due_at",
                table: "marketing_content_briefs",
                columns: new[] { "company_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_content_variants_company_id",
                table: "marketing_content_variants",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_content_variants_company_id_marketing_content_brief_id_status",
                table: "marketing_content_variants",
                columns: new[] { "company_id", "marketing_content_brief_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_experiments_company_id",
                table: "marketing_experiments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_experiments_company_id_status_ends_at",
                table: "marketing_experiments",
                columns: new[] { "company_id", "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_objectives_company_id",
                table: "marketing_objectives",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_objectives_company_id_status_period_end_at",
                table: "marketing_objectives",
                columns: new[] { "company_id", "status", "period_end_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_objectives_company_id",
                table: "marketing_plan_objectives",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_objectives_company_id_marketing_plan_id_marketing_objective_id",
                table: "marketing_plan_objectives",
                columns: new[] { "company_id", "marketing_plan_id", "marketing_objective_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plans_company_id",
                table: "marketing_plans",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plans_company_id_status_starts_at_ends_at",
                table: "marketing_plans",
                columns: new[] { "company_id", "status", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_sales_handoffs_company_id",
                table: "marketing_sales_handoffs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_sales_handoffs_company_id_idempotency_key",
                table: "marketing_sales_handoffs",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_sales_handoffs_company_id_status_expires_at",
                table: "marketing_sales_handoffs",
                columns: new[] { "company_id", "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_channel_observations");

            migrationBuilder.DropTable(
                name: "marketing_content_briefs");

            migrationBuilder.DropTable(
                name: "marketing_content_variants");

            migrationBuilder.DropTable(
                name: "marketing_experiments");

            migrationBuilder.DropTable(
                name: "marketing_objectives");

            migrationBuilder.DropTable(
                name: "marketing_plan_objectives");

            migrationBuilder.DropTable(
                name: "marketing_plans");

            migrationBuilder.DropTable(
                name: "marketing_sales_handoffs");
        }
    }
}
