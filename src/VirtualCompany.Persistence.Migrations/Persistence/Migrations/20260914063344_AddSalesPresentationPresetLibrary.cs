using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPresentationPresetLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_presentation_preset_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preset_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    original_file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    storage_url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    processing_version = table.Column<int>(type: "int", nullable: false),
                    processing_attempt_count = table.Column<int>(type: "int", nullable: false),
                    slide_count = table.Column<int>(type: "int", nullable: false),
                    renderer_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    renderer_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    animation_handling = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    can_retry = table.Column<bool>(type: "bit", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    processing_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_preset_assets", x => x.id);
                    table.UniqueConstraint("AK_sales_presentation_preset_assets_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_presentation_preset_assets_counts", "file_size_bytes > 0 AND slide_count >= 0 AND processing_attempt_count >= 0 AND processing_version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "sales_presentation_preset_slides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    processing_version = table.Column<int>(type: "int", nullable: false),
                    slide_number = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    extracted_text = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    speaker_notes = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: true),
                    image_storage_key = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    image_storage_url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    image_width_pixels = table.Column<int>(type: "int", nullable: false),
                    image_height_pixels = table.Column<int>(type: "int", nullable: false),
                    source_width_emus = table.Column<long>(type: "bigint", nullable: false),
                    source_height_emus = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    objective = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    baseline_talking_points = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    expected_duration_seconds = table.Column<int>(type: "int", nullable: false),
                    transition_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_preset_slides", x => x.id);
                    table.CheckConstraint("CK_sales_presentation_preset_slides_dimensions", "slide_number >= 1 AND processing_version >= 1 AND image_width_pixels > 0 AND image_height_pixels > 0 AND expected_duration_seconds > 0");
                    table.ForeignKey(
                        name: "FK_sales_presentation_preset_slides_sales_presentation_preset_assets_company_id_asset_id",
                        columns: x => new { x.company_id, x.asset_id },
                        principalTable: "sales_presentation_preset_assets",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_presentation_preset_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    lifecycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    default_presenter_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    behavior_settings_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    goal = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    audience = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    duration_minutes = table.Column<int>(type: "int", nullable: false),
                    demo_scenario = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    control_mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    allow_sales_meeting = table.Column<bool>(type: "bit", nullable: false),
                    allow_campaign_activity = table.Column<bool>(type: "bit", nullable: false),
                    allow_ad_hoc = table.Column<bool>(type: "bit", nullable: false),
                    required_knowledge_scope = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    published_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_preset_versions", x => x.id);
                    table.UniqueConstraint("AK_sales_presentation_preset_versions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_presentation_preset_versions_duration", "duration_minutes BETWEEN 1 AND 480");
                    table.ForeignKey(
                        name: "FK_sales_presentation_preset_versions_agents_company_id_default_presenter_agent_id",
                        columns: x => new { x.company_id, x.default_presenter_agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_presentation_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lifecycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    current_published_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    archived_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_presets", x => x.id);
                    table.UniqueConstraint("AK_sales_presentation_presets_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_presentation_presets_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_presentation_presets_sales_presentation_preset_versions_company_id_current_published_version_id",
                        columns: x => new { x.company_id, x.current_published_version_id },
                        principalTable: "sales_presentation_preset_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_preset_assets_company_id_content_hash_processing_version",
                table: "sales_presentation_preset_assets",
                columns: new[] { "company_id", "content_hash", "processing_version" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_preset_assets_company_id_preset_version_id",
                table: "sales_presentation_preset_assets",
                columns: new[] { "company_id", "preset_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_preset_assets_company_id_status_processing_started_at",
                table: "sales_presentation_preset_assets",
                columns: new[] { "company_id", "status", "processing_started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_preset_slides_company_id_asset_id_processing_version_slide_number",
                table: "sales_presentation_preset_slides",
                columns: new[] { "company_id", "asset_id", "processing_version", "slide_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_preset_versions_company_id_default_presenter_agent_id",
                table: "sales_presentation_preset_versions",
                columns: new[] { "company_id", "default_presenter_agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_preset_versions_company_id_preset_id_lifecycle",
                table: "sales_presentation_preset_versions",
                columns: new[] { "company_id", "preset_id", "lifecycle" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_preset_versions_company_id_preset_id_version_number",
                table: "sales_presentation_preset_versions",
                columns: new[] { "company_id", "preset_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_presets_company_id_current_published_version_id",
                table: "sales_presentation_presets",
                columns: new[] { "company_id", "current_published_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_presets_company_id_lifecycle_updated_at",
                table: "sales_presentation_presets",
                columns: new[] { "company_id", "lifecycle", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_presets_company_id_name",
                table: "sales_presentation_presets",
                columns: new[] { "company_id", "name" });

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_preset_assets_sales_presentation_preset_versions_company_id_preset_version_id",
                table: "sales_presentation_preset_assets",
                columns: new[] { "company_id", "preset_version_id" },
                principalTable: "sales_presentation_preset_versions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_preset_versions_sales_presentation_presets_company_id_preset_id",
                table: "sales_presentation_preset_versions",
                columns: new[] { "company_id", "preset_id" },
                principalTable: "sales_presentation_presets",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_presentation_presets_sales_presentation_preset_versions_company_id_current_published_version_id",
                table: "sales_presentation_presets");

            migrationBuilder.DropTable(
                name: "sales_presentation_preset_slides");

            migrationBuilder.DropTable(
                name: "sales_presentation_preset_assets");

            migrationBuilder.DropTable(
                name: "sales_presentation_preset_versions");

            migrationBuilder.DropTable(
                name: "sales_presentation_presets");
        }
    }
}
