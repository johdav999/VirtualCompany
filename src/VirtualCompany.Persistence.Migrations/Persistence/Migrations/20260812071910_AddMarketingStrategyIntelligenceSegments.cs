using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingStrategyIntelligenceSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_customer_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_customer_segments", x => x.id);
                    table.UniqueConstraint("AK_marketing_customer_segments_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_intelligence_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    classification = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    review_due_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    dimensions_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    review_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_intelligence_records", x => x.id);
                    table.UniqueConstraint("AK_marketing_intelligence_records_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_strategies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    business_context = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    valid_from_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    valid_to_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sections_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_references_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    missing_evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_strategies", x => x.id);
                    table.UniqueConstraint("AK_marketing_strategies_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_customer_segment_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_customer_segment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    criteria_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    needs_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    behaviors_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    channels_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    pricing_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    size_low = table.Column<long>(type: "bigint", nullable: true),
                    size_high = table.Column<long>(type: "bigint", nullable: true),
                    size_method = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    economics_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    scorecard_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    attractiveness_score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    target_state = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    target_rationale = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    concurrency_version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_customer_segment_versions", x => x.id);
                    table.UniqueConstraint("AK_marketing_customer_segment_versions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_customer_segment_versions_marketing_customer_segments_company_id_marketing_customer_segment_id",
                        columns: x => new { x.company_id, x.marketing_customer_segment_id },
                        principalTable: "marketing_customer_segments",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_strategy_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_strategy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_customer_segment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_customer_segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_strategy_segments", x => x.id);
                    table.UniqueConstraint("AK_marketing_strategy_segments_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_strategy_segments_marketing_customer_segment_versions_company_id_marketing_customer_segment_version_id",
                        columns: x => new { x.company_id, x.marketing_customer_segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketing_strategy_segments_marketing_strategies_company_id_marketing_strategy_id",
                        columns: x => new { x.company_id, x.marketing_strategy_id },
                        principalTable: "marketing_strategies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "agent_templates",
                keyColumn: "Id",
                keyValue: new Guid("3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30103"),
                columns: new[] { "data_scopes_json", "tool_permissions_json" },
                values: new object[] { "{\"read\":[\"marketing\",\"sales\",\"knowledge\"],\"recommend\":[\"marketing\",\"sales\",\"knowledge\"],\"execute\":[],\"write\":[]}", "{\"allowed\":[\"marketing.read_workspace\",\"marketing.read_objectives\",\"marketing.read_campaigns\",\"marketing.read_content_calendar\",\"marketing.read_audience_evidence\",\"marketing.read_channel_observations\",\"marketing.read_attribution_summary\",\"marketing.search_approved_knowledge\",\"marketing.prepare_plan\",\"marketing.analyze_audience\",\"marketing.prepare_content_brief\",\"marketing.recommend_campaign_change\",\"marketing.prepare_performance_review\",\"marketing.prepare_experiment\",\"marketing.prepare_operating_review\"],\"denied\":[],\"actions\":[\"read\",\"recommend\"],\"deniedActions\":[\"execute\"]}" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_customer_segment_versions_company_id",
                table: "marketing_customer_segment_versions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_customer_segment_versions_company_id_idempotency_key",
                table: "marketing_customer_segment_versions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_customer_segment_versions_company_id_marketing_customer_segment_id_version_number",
                table: "marketing_customer_segment_versions",
                columns: new[] { "company_id", "marketing_customer_segment_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_customer_segment_versions_company_id_status_target_state",
                table: "marketing_customer_segment_versions",
                columns: new[] { "company_id", "status", "target_state" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_customer_segments_company_id",
                table: "marketing_customer_segments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_customer_segments_company_id_name",
                table: "marketing_customer_segments",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_intelligence_records_company_id",
                table: "marketing_intelligence_records",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_intelligence_records_company_id_kind_review_status_review_due_at",
                table: "marketing_intelligence_records",
                columns: new[] { "company_id", "kind", "review_status", "review_due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategies_company_id",
                table: "marketing_strategies",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategies_company_id_idempotency_key",
                table: "marketing_strategies",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategies_company_id_status_valid_from_at_valid_to_at",
                table: "marketing_strategies",
                columns: new[] { "company_id", "status", "valid_from_at", "valid_to_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_segments_company_id",
                table: "marketing_strategy_segments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_segments_company_id_marketing_customer_segment_version_id",
                table: "marketing_strategy_segments",
                columns: new[] { "company_id", "marketing_customer_segment_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_segments_company_id_marketing_strategy_id_marketing_customer_segment_version_id",
                table: "marketing_strategy_segments",
                columns: new[] { "company_id", "marketing_strategy_id", "marketing_customer_segment_version_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_intelligence_records");

            migrationBuilder.DropTable(
                name: "marketing_strategy_segments");

            migrationBuilder.DropTable(
                name: "marketing_customer_segment_versions");

            migrationBuilder.DropTable(
                name: "marketing_strategies");

            migrationBuilder.DropTable(
                name: "marketing_customer_segments");

            migrationBuilder.UpdateData(
                table: "agent_templates",
                keyColumn: "Id",
                keyValue: new Guid("3cda0f7c-0cb5-4b4f-9cf2-1a5b25f30103"),
                columns: new[] { "data_scopes_json", "tool_permissions_json" },
                values: new object[] { "{\"read\":[\"campaigns\",\"analytics\",\"content_calendar\"],\"write\":[\"campaign_briefs\",\"draft_copy\",\"weekly_reports\"]}", "{\"allowed\":[\"analytics\",\"cms\",\"email_marketing\",\"ads_manager\"]}" });
        }
    }
}
