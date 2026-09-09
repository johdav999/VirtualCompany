using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingClosingArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_sales_meeting_artifacts_company_id_id",
                table: "sales_meeting_artifacts",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "sales_meeting_minutes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generation_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    previous_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    artifact_version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    evidence_capture_version = table.Column<long>(type: "bigint", nullable: false),
                    evidence_cutoff_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    generator_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    generator_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    prompt_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    retention_until_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_minutes", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_minutes_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_minutes_capture_version", "evidence_capture_version >= 0");
                    table.CheckConstraint("CK_sales_meeting_minutes_version", "artifact_version > 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_minutes_sales_meeting_minutes_company_id_previous_version_id",
                        columns: x => new { x.company_id, x.previous_version_id },
                        principalTable: "sales_meeting_minutes",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_sales_meeting_minutes_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_internal_intelligence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    minutes_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    artifact_version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    evidence_capture_version = table.Column<long>(type: "bigint", nullable: false),
                    evidence_cutoff_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    generator_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    generator_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    prompt_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    retention_until_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_internal_intelligence", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_internal_intelligence_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_internal_intelligence_capture_version", "evidence_capture_version >= 0");
                    table.CheckConstraint("CK_sales_meeting_internal_intelligence_version", "artifact_version > 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_internal_intelligence_sales_meeting_minutes_company_id_minutes_id",
                        columns: x => new { x.company_id, x.minutes_id },
                        principalTable: "sales_meeting_minutes",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_internal_intelligence_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_minutes_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    minutes_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    item_order = table.Column<int>(type: "int", nullable: false),
                    item_type = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    owner_label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    source_id = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    source_artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requires_review = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_minutes_items", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_minutes_items_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_minutes_items_order", "item_order >= 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_minutes_items_sales_meeting_artifacts_company_id_source_artifact_id",
                        columns: x => new { x.company_id, x.source_artifact_id },
                        principalTable: "sales_meeting_artifacts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_sales_meeting_minutes_items_sales_meeting_minutes_company_id_minutes_id",
                        columns: x => new { x.company_id, x.minutes_id },
                        principalTable: "sales_meeting_minutes",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_internal_intelligence_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    intelligence_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    item_order = table.Column<int>(type: "int", nullable: false),
                    item_type = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    source_id = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    source_artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requires_review = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_internal_intelligence_items", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_internal_intelligence_items_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_internal_intelligence_items_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("CK_sales_meeting_internal_intelligence_items_order", "item_order >= 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_internal_intelligence_items_sales_meeting_artifacts_company_id_source_artifact_id",
                        columns: x => new { x.company_id, x.source_artifact_id },
                        principalTable: "sales_meeting_artifacts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_sales_meeting_internal_intelligence_items_sales_meeting_internal_intelligence_company_id_intelligence_id",
                        columns: x => new { x.company_id, x.intelligence_id },
                        principalTable: "sales_meeting_internal_intelligence",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_internal_intelligence_company_id_minutes_id",
                table: "sales_meeting_internal_intelligence",
                columns: new[] { "company_id", "minutes_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_internal_intelligence_company_id_retention_until_utc",
                table: "sales_meeting_internal_intelligence",
                columns: new[] { "company_id", "retention_until_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_internal_intelligence_company_id_session_id_artifact_version",
                table: "sales_meeting_internal_intelligence",
                columns: new[] { "company_id", "session_id", "artifact_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_internal_intelligence_company_id_session_id_status_updated_at",
                table: "sales_meeting_internal_intelligence",
                columns: new[] { "company_id", "session_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_internal_intelligence_items_company_id_intelligence_id_item_order",
                table: "sales_meeting_internal_intelligence_items",
                columns: new[] { "company_id", "intelligence_id", "item_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_internal_intelligence_items_company_id_source_artifact_id",
                table: "sales_meeting_internal_intelligence_items",
                columns: new[] { "company_id", "source_artifact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_minutes_company_id_previous_version_id",
                table: "sales_meeting_minutes",
                columns: new[] { "company_id", "previous_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_minutes_company_id_retention_until_utc",
                table: "sales_meeting_minutes",
                columns: new[] { "company_id", "retention_until_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_minutes_company_id_session_id_artifact_version",
                table: "sales_meeting_minutes",
                columns: new[] { "company_id", "session_id", "artifact_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_minutes_company_id_session_id_generation_request_id",
                table: "sales_meeting_minutes",
                columns: new[] { "company_id", "session_id", "generation_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_minutes_company_id_session_id_status_updated_at",
                table: "sales_meeting_minutes",
                columns: new[] { "company_id", "session_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_minutes_items_company_id_minutes_id_item_order",
                table: "sales_meeting_minutes_items",
                columns: new[] { "company_id", "minutes_id", "item_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_minutes_items_company_id_source_artifact_id",
                table: "sales_meeting_minutes_items",
                columns: new[] { "company_id", "source_artifact_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_internal_intelligence_items");

            migrationBuilder.DropTable(
                name: "sales_meeting_minutes_items");

            migrationBuilder.DropTable(
                name: "sales_meeting_internal_intelligence");

            migrationBuilder.DropTable(
                name: "sales_meeting_minutes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_sales_meeting_artifacts_company_id_id",
                table: "sales_meeting_artifacts");
        }
    }
}
