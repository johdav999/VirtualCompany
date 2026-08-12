using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingDeliveryLifecycleAndLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_attribution_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attributed_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    period_start_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    period_end_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_attribution_results", x => x.id);
                    table.UniqueConstraint("AK_marketing_attribution_results_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_channel_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_channel_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    marketing_content_brief_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    destination_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_channel_actions", x => x.id);
                    table.UniqueConstraint("AK_marketing_channel_actions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_channel_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    external_account_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    capabilities_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    secret_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    health_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    last_checked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_channel_connections", x => x.id);
                    table.UniqueConstraint("AK_marketing_channel_connections_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_creative_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_content_brief_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    media_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    dimensions = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    generation_summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    prompt_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    brand_profile_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    safety_result = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    alt_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    storage_reference = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_creative_assets", x => x.id);
                    table.UniqueConstraint("AK_marketing_creative_assets_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_event_triggers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    operating_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_event_triggers", x => x.id);
                    table.UniqueConstraint("AK_marketing_event_triggers_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_lifecycle_journeys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    audience_eligibility_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    entry_exit_criteria_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    steps_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    guardrails_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    frequency_cap = table.Column<int>(type: "int", nullable: false),
                    valid_from_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    valid_to_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_lifecycle_journeys", x => x.id);
                    table.UniqueConstraint("AK_marketing_lifecycle_journeys_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_results_company_id",
                table: "marketing_attribution_results",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_results_company_id_idempotency_key",
                table: "marketing_attribution_results",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_actions_company_id",
                table: "marketing_channel_actions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_actions_company_id_idempotency_key",
                table: "marketing_channel_actions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_actions_company_id_status_scheduled_at",
                table: "marketing_channel_actions",
                columns: new[] { "company_id", "status", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_connections_company_id",
                table: "marketing_channel_connections",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_connections_company_id_provider_external_account_reference",
                table: "marketing_channel_connections",
                columns: new[] { "company_id", "provider", "external_account_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_creative_assets_company_id",
                table: "marketing_creative_assets",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_creative_assets_company_id_idempotency_key",
                table: "marketing_creative_assets",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_event_triggers_company_id",
                table: "marketing_event_triggers",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_event_triggers_company_id_idempotency_key",
                table: "marketing_event_triggers",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_event_triggers_company_id_status_severity",
                table: "marketing_event_triggers",
                columns: new[] { "company_id", "status", "severity" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_lifecycle_journeys_company_id",
                table: "marketing_lifecycle_journeys",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_lifecycle_journeys_company_id_idempotency_key",
                table: "marketing_lifecycle_journeys",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_attribution_results");

            migrationBuilder.DropTable(
                name: "marketing_channel_actions");

            migrationBuilder.DropTable(
                name: "marketing_channel_connections");

            migrationBuilder.DropTable(
                name: "marketing_creative_assets");

            migrationBuilder.DropTable(
                name: "marketing_event_triggers");

            migrationBuilder.DropTable(
                name: "marketing_lifecycle_journeys");
        }
    }
}
