using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingGroundedCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "capture_version",
                table: "sales_meeting_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "last_capture_batch_id",
                table: "sales_meeting_sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_meeting_action_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    owner_label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    source_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    review_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_client_batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_action_items", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_action_items_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_action_items_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("CK_sales_meeting_action_items_sequence", "sequence_number > 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_action_items_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    source_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    review_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_client_batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_observations", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_observations_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_observations_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("CK_sales_meeting_observations_sequence", "sequence_number > 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_observations_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_question_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    question_text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    answer_text = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    asker_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    asker_label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    input_source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    visible_slide_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    presentation_version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    follow_up_required = table.Column<bool>(type: "bit", nullable: false),
                    review_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    visibility = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ai_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    asked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stage_approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    asked_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    answered_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    stage_approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_questions", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_questions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_questions_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("CK_sales_meeting_questions_sequence", "sequence_number > 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_questions_agents_company_id_agent_id",
                        columns: x => new { x.company_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_questions_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_questions_sales_presentation_slides_company_id_visible_slide_id",
                        columns: x => new { x.company_id, x.visible_slide_id },
                        principalTable: "sales_presentation_slides",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_transcript_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    speaker_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    speaker_label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    input_source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    review_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_client_batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_transcript_segments", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_transcript_segments_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_transcript_segments_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("CK_sales_meeting_transcript_segments_sequence", "sequence_number > 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_segments_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_question_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    question_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_order = table.Column<int>(type: "int", nullable: false),
                    claim_text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_question_evidence", x => x.id);
                    table.CheckConstraint("CK_sales_meeting_question_evidence_confidence", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "FK_sales_meeting_question_evidence_sales_meeting_questions_company_id_question_id",
                        columns: x => new { x.company_id, x.question_id },
                        principalTable: "sales_meeting_questions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_action_items_company_id_session_id_client_item_id",
                table: "sales_meeting_action_items",
                columns: new[] { "company_id", "session_id", "client_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_action_items_company_id_session_id_sequence_number",
                table: "sales_meeting_action_items",
                columns: new[] { "company_id", "session_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_observations_company_id_session_id_client_item_id",
                table: "sales_meeting_observations",
                columns: new[] { "company_id", "session_id", "client_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_observations_company_id_session_id_sequence_number",
                table: "sales_meeting_observations",
                columns: new[] { "company_id", "session_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_question_evidence_company_id_question_id_claim_order_source_id",
                table: "sales_meeting_question_evidence",
                columns: new[] { "company_id", "question_id", "claim_order", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_questions_company_id_agent_id",
                table: "sales_meeting_questions",
                columns: new[] { "company_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_questions_company_id_session_id_client_question_id",
                table: "sales_meeting_questions",
                columns: new[] { "company_id", "session_id", "client_question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_questions_company_id_session_id_sequence_number",
                table: "sales_meeting_questions",
                columns: new[] { "company_id", "session_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_questions_company_id_session_id_status_updated_at",
                table: "sales_meeting_questions",
                columns: new[] { "company_id", "session_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_questions_company_id_visible_slide_id",
                table: "sales_meeting_questions",
                columns: new[] { "company_id", "visible_slide_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_segments_company_id_session_id_client_item_id",
                table: "sales_meeting_transcript_segments",
                columns: new[] { "company_id", "session_id", "client_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_segments_company_id_session_id_sequence_number",
                table: "sales_meeting_transcript_segments",
                columns: new[] { "company_id", "session_id", "sequence_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_action_items");

            migrationBuilder.DropTable(
                name: "sales_meeting_observations");

            migrationBuilder.DropTable(
                name: "sales_meeting_question_evidence");

            migrationBuilder.DropTable(
                name: "sales_meeting_transcript_segments");

            migrationBuilder.DropTable(
                name: "sales_meeting_questions");

            migrationBuilder.DropColumn(
                name: "capture_version",
                table: "sales_meeting_sessions");

            migrationBuilder.DropColumn(
                name: "last_capture_batch_id",
                table: "sales_meeting_sessions");
        }
    }
}
