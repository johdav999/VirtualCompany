using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPresentationDeckProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_presentation_decks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
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
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    brief_version = table.Column<int>(type: "int", nullable: false),
                    brief_regeneration_requested_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    processing_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    activated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_decks", x => x.id);
                    table.UniqueConstraint("AK_sales_presentation_decks_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_presentation_decks_counts", "file_size_bytes > 0 AND slide_count >= 0 AND processing_attempt_count >= 0");
                    table.CheckConstraint("CK_sales_presentation_decks_version", "version >= 1 AND processing_version >= 1");
                    table.ForeignKey(
                        name: "FK_sales_presentation_decks_agents_company_id_agent_id",
                        columns: x => new { x.company_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_presentation_decks_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_presentation_decks_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_presentation_slides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deck_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    expected_duration_seconds = table.Column<int>(type: "int", nullable: false),
                    transition_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_slides", x => x.id);
                    table.UniqueConstraint("AK_sales_presentation_slides_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_presentation_slides_dimensions", "image_width_pixels > 0 AND image_height_pixels > 0");
                    table.CheckConstraint("CK_sales_presentation_slides_numbers", "processing_version >= 1 AND slide_number >= 1");
                    table.ForeignKey(
                        name: "FK_sales_presentation_slides_sales_presentation_decks_company_id_deck_id",
                        columns: x => new { x.company_id, x.deck_id },
                        principalTable: "sales_presentation_decks",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deck_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slide_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    artifact_version = table.Column<int>(type: "int", nullable: false),
                    artifact_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    section = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    artifact_order = table.Column<int>(type: "int", nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ai_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_artifacts", x => x.id);
                    table.CheckConstraint("CK_sales_meeting_artifacts_order", "artifact_version >= 1 AND artifact_order >= 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_artifacts_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_artifacts_sales_presentation_decks_company_id_deck_id",
                        columns: x => new { x.company_id, x.deck_id },
                        principalTable: "sales_presentation_decks",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_artifacts_sales_presentation_slides_company_id_slide_id",
                        columns: x => new { x.company_id, x.slide_id },
                        principalTable: "sales_presentation_slides",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_artifacts_company_id_deck_id",
                table: "sales_meeting_artifacts",
                columns: new[] { "company_id", "deck_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_artifacts_company_id_session_id_deck_id_artifact_version_artifact_type",
                table: "sales_meeting_artifacts",
                columns: new[] { "company_id", "session_id", "deck_id", "artifact_version", "artifact_type" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_artifacts_company_id_slide_id_artifact_version_artifact_order",
                table: "sales_meeting_artifacts",
                columns: new[] { "company_id", "slide_id", "artifact_version", "artifact_order" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_agent_id",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_session_id_content_hash_processing_version",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "session_id", "content_hash", "processing_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_session_id_is_active",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "session_id", "is_active" },
                unique: true,
                filter: "[is_active] = CAST(1 AS bit)");

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_session_id_version",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "session_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_status_processing_started_at",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "status", "processing_started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_slides_company_id_deck_id_processing_version_slide_number",
                table: "sales_presentation_slides",
                columns: new[] { "company_id", "deck_id", "processing_version", "slide_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_artifacts");

            migrationBuilder.DropTable(
                name: "sales_presentation_slides");

            migrationBuilder.DropTable(
                name: "sales_presentation_decks");
        }
    }
}
