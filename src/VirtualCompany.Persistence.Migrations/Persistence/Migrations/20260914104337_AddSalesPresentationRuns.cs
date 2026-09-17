using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPresentationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "presentation_run_id",
                table: "sales_presentation_decks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preset_asset_id",
                table: "sales_presentation_decks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_sales_presentation_preset_slides_company_id_id",
                table: "sales_presentation_preset_slides",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "sales_presentation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preset_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    context_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    context_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    meeting_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    presenter_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    goal = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    audience = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    duration_minutes = table.Column<int>(type: "int", nullable: false),
                    demo_scenario = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    control_mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    goal_overridden = table.Column<bool>(type: "bit", nullable: false),
                    audience_overridden = table.Column<bool>(type: "bit", nullable: false),
                    duration_overridden = table.Column<bool>(type: "bit", nullable: false),
                    demo_scenario_overridden = table.Column<bool>(type: "bit", nullable: false),
                    presenter_overridden = table.Column<bool>(type: "bit", nullable: false),
                    preparation_status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    readiness_blockers_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    preparation_version = table.Column<int>(type: "int", nullable: false),
                    preparation_attempt_count = table.Column<int>(type: "int", nullable: false),
                    can_retry = table.Column<bool>(type: "bit", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    preparation_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    prepared_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    compatibility_deck_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_runs", x => x.id);
                    table.UniqueConstraint("AK_sales_presentation_runs_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_presentation_runs_duration", "duration_minutes BETWEEN 5 AND 480");
                    table.CheckConstraint("CK_sales_presentation_runs_preparation", "preparation_version >= 1 AND preparation_attempt_count >= 0");
                    table.ForeignKey(
                        name: "FK_sales_presentation_runs_agents_company_id_presenter_agent_id",
                        columns: x => new { x.company_id, x.presenter_agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_presentation_runs_sales_meeting_sessions_company_id_meeting_session_id",
                        columns: x => new { x.company_id, x.meeting_session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_presentation_runs_sales_presentation_preset_versions_company_id_preset_version_id",
                        columns: x => new { x.company_id, x.preset_version_id },
                        principalTable: "sales_presentation_preset_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_presentation_run_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preparation_version = table.Column<int>(type: "int", nullable: false),
                    artifact_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    preset_slide_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    artifact_order = table.Column<int>(type: "int", nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_run_artifacts", x => x.id);
                    table.CheckConstraint("CK_sales_presentation_run_artifacts_order", "preparation_version >= 1 AND artifact_order >= 0");
                    table.ForeignKey(
                        name: "FK_sales_presentation_run_artifacts_sales_presentation_preset_slides_company_id_preset_slide_id",
                        columns: x => new { x.company_id, x.preset_slide_id },
                        principalTable: "sales_presentation_preset_slides",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_presentation_run_artifacts_sales_presentation_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "sales_presentation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_presentation_run_id",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "presentation_run_id" },
                unique: true,
                filter: "[presentation_run_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_decks_company_id_preset_asset_id",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "preset_asset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_run_artifacts_company_id_preset_slide_id",
                table: "sales_presentation_run_artifacts",
                columns: new[] { "company_id", "preset_slide_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_run_artifacts_company_id_run_id_preparation_version_artifact_type_artifact_order",
                table: "sales_presentation_run_artifacts",
                columns: new[] { "company_id", "run_id", "preparation_version", "artifact_type", "artifact_order" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_meeting_session_id_is_active",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "meeting_session_id", "is_active" },
                unique: true,
                filter: "[is_active] = CAST(1 AS bit) AND [meeting_session_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_preparation_status_preparation_started_at",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "preparation_status", "preparation_started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_presenter_agent_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "presenter_agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_preset_version_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "preset_version_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_decks_sales_presentation_preset_assets_company_id_preset_asset_id",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "preset_asset_id" },
                principalTable: "sales_presentation_preset_assets",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_decks_sales_presentation_runs_company_id_presentation_run_id",
                table: "sales_presentation_decks",
                columns: new[] { "company_id", "presentation_run_id" },
                principalTable: "sales_presentation_runs",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_presentation_decks_sales_presentation_preset_assets_company_id_preset_asset_id",
                table: "sales_presentation_decks");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_presentation_decks_sales_presentation_runs_company_id_presentation_run_id",
                table: "sales_presentation_decks");

            migrationBuilder.DropTable(
                name: "sales_presentation_run_artifacts");

            migrationBuilder.DropTable(
                name: "sales_presentation_runs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_sales_presentation_preset_slides_company_id_id",
                table: "sales_presentation_preset_slides");

            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_decks_company_id_presentation_run_id",
                table: "sales_presentation_decks");

            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_decks_company_id_preset_asset_id",
                table: "sales_presentation_decks");

            migrationBuilder.DropColumn(
                name: "presentation_run_id",
                table: "sales_presentation_decks");

            migrationBuilder.DropColumn(
                name: "preset_asset_id",
                table: "sales_presentation_decks");
        }
    }
}
