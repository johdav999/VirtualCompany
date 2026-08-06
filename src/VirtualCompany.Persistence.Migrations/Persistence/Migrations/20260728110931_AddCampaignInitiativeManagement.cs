using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignInitiativeManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "budget_currency",
                table: "sales_campaigns",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "campaign_type",
                table: "sales_campaigns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "lead_generation");

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                table: "sales_campaigns",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "sales_campaigns",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ends_at",
                table: "sales_campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "legacy_setup_required",
                table: "sales_campaigns",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "lifecycle_status",
                table: "sales_campaigns",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_agent_id",
                table: "sales_campaigns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                table: "sales_campaigns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "planned_budget",
                table: "sales_campaigns",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "planning_starts_at",
                table: "sales_campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "primary_objective_target",
                table: "sales_campaigns",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "primary_objective_target_at",
                table: "sales_campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_objective_type",
                table: "sales_campaigns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_objective_unit",
                table: "sales_campaigns",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "review_due_at",
                table: "sales_campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "scheduled_launch_at",
                table: "sales_campaigns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                table: "sales_campaigns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.CreateTable(
                name: "sales_campaign_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    milestone_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    depends_on_activity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sales_sequence_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    activity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    execution_mode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    planned_start_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    time_zone_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    required_tool_capability = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    result_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    failure_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    claimed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    claim_token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_activities", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_activities_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_campaign_activities_sales_campaigns_company_id_sales_campaign_id",
                        columns: x => new { x.company_id, x.sales_campaign_id },
                        principalTable: "sales_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_audience_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    segment_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    industry = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    country = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    min_employees = table.Column<int>(type: "int", nullable: true),
                    max_employees = table.Column<int>(type: "int", nullable: true),
                    buying_role = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    customer_lifecycle = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    product_interest = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    preferred_language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    require_communication_permission = table.Column<bool>(type: "bit", nullable: false),
                    exclude_open_critical_support_cases = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_audience_segments", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_audience_segments_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_audience_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    audience_segment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    segment_version = table.Column<int>(type: "int", nullable: false),
                    snapshot_version = table.Column<int>(type: "int", nullable: false),
                    captured_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_audience_snapshots", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_audience_snapshots_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_campaign_audience_snapshots_sales_campaigns_company_id_sales_campaign_id",
                        columns: x => new { x.company_id, x.sales_campaign_id },
                        principalTable: "sales_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    source = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    finance_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sales_campaign_activity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_costs", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_costs_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_kpi_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    numerator = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    denominator = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    baseline = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    target = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    attribution_window_days = table.Column<int>(type: "int", nullable: false),
                    data_source = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_kpi_definitions", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_kpi_definitions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_kpi_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_version = table.Column<int>(type: "int", nullable: false),
                    numerator_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    denominator_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    metric_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    evidence_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_kpi_snapshots", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_kpi_snapshots_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_milestones", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_milestones_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_campaign_milestones_sales_campaigns_company_id_sales_campaign_id",
                        columns: x => new { x.company_id, x.sales_campaign_id },
                        principalTable: "sales_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    objective_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    target_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    target_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_objectives", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_objectives_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_campaign_objectives_sales_campaigns_company_id_sales_campaign_id",
                        columns: x => new { x.company_id, x.sales_campaign_id },
                        principalTable: "sales_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    knowledge_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    no_offer_required = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_offers", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_offers_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_campaign_offers_sales_campaigns_company_id_sales_campaign_id",
                        columns: x => new { x.company_id, x.sales_campaign_id },
                        principalTable: "sales_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_campaign_audience_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    audience_snapshot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    prospect_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    eligibility_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    inclusion_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    consent_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    communication_language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_campaign_audience_members", x => x.id);
                    table.UniqueConstraint("AK_sales_campaign_audience_members_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_campaign_audience_members_sales_campaign_audience_snapshots_company_id_audience_snapshot_id",
                        columns: x => new { x.company_id, x.audience_snapshot_id },
                        principalTable: "sales_campaign_audience_snapshots",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaigns_company_id_lifecycle_status_scheduled_launch_at",
                table: "sales_campaigns",
                columns: new[] { "company_id", "lifecycle_status", "scheduled_launch_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_activities_company_id",
                table: "sales_campaign_activities",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_activities_company_id_idempotency_key",
                table: "sales_campaign_activities",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_activities_company_id_sales_campaign_id_planned_start_at",
                table: "sales_campaign_activities",
                columns: new[] { "company_id", "sales_campaign_id", "planned_start_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_activities_company_id_status_due_at",
                table: "sales_campaign_activities",
                columns: new[] { "company_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_audience_members_company_id",
                table: "sales_campaign_audience_members",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_audience_members_company_id_audience_snapshot_id_contact_id",
                table: "sales_campaign_audience_members",
                columns: new[] { "company_id", "audience_snapshot_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_audience_segments_company_id",
                table: "sales_campaign_audience_segments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_audience_segments_company_id_is_active_segment_kind",
                table: "sales_campaign_audience_segments",
                columns: new[] { "company_id", "is_active", "segment_kind" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_audience_segments_company_id_name",
                table: "sales_campaign_audience_segments",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_audience_snapshots_company_id",
                table: "sales_campaign_audience_snapshots",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_audience_snapshots_company_id_sales_campaign_id_snapshot_version",
                table: "sales_campaign_audience_snapshots",
                columns: new[] { "company_id", "sales_campaign_id", "snapshot_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_costs_company_id",
                table: "sales_campaign_costs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_costs_company_id_sales_campaign_id_classification_currency_observed_at",
                table: "sales_campaign_costs",
                columns: new[] { "company_id", "sales_campaign_id", "classification", "currency", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_kpi_definitions_company_id",
                table: "sales_campaign_kpi_definitions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_kpi_definitions_company_id_sales_campaign_id_key_version",
                table: "sales_campaign_kpi_definitions",
                columns: new[] { "company_id", "sales_campaign_id", "key", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_kpi_snapshots_company_id",
                table: "sales_campaign_kpi_snapshots",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_kpi_snapshots_company_id_sales_campaign_id_definition_id_observed_at",
                table: "sales_campaign_kpi_snapshots",
                columns: new[] { "company_id", "sales_campaign_id", "definition_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_milestones_company_id",
                table: "sales_campaign_milestones",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_milestones_company_id_sales_campaign_id_due_at",
                table: "sales_campaign_milestones",
                columns: new[] { "company_id", "sales_campaign_id", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_objectives_company_id",
                table: "sales_campaign_objectives",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_objectives_company_id_sales_campaign_id_is_primary",
                table: "sales_campaign_objectives",
                columns: new[] { "company_id", "sales_campaign_id", "is_primary" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_offers_company_id",
                table: "sales_campaign_offers",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_campaign_offers_company_id_sales_campaign_id",
                table: "sales_campaign_offers",
                columns: new[] { "company_id", "sales_campaign_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_campaign_activities");

            migrationBuilder.DropTable(
                name: "sales_campaign_audience_members");

            migrationBuilder.DropTable(
                name: "sales_campaign_audience_segments");

            migrationBuilder.DropTable(
                name: "sales_campaign_costs");

            migrationBuilder.DropTable(
                name: "sales_campaign_kpi_definitions");

            migrationBuilder.DropTable(
                name: "sales_campaign_kpi_snapshots");

            migrationBuilder.DropTable(
                name: "sales_campaign_milestones");

            migrationBuilder.DropTable(
                name: "sales_campaign_objectives");

            migrationBuilder.DropTable(
                name: "sales_campaign_offers");

            migrationBuilder.DropTable(
                name: "sales_campaign_audience_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_sales_campaigns_company_id_lifecycle_status_scheduled_launch_at",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "budget_currency",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "campaign_type",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "description",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "ends_at",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "legacy_setup_required",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "lifecycle_status",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "owner_agent_id",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "planned_budget",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "planning_starts_at",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "primary_objective_target",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "primary_objective_target_at",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "primary_objective_type",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "primary_objective_unit",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "review_due_at",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "scheduled_launch_at",
                table: "sales_campaigns");

            migrationBuilder.DropColumn(
                name: "time_zone_id",
                table: "sales_campaigns");
        }
    }
}
