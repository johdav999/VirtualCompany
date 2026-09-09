using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingTranscriptReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "last_transcript_reconciliation_id",
                table: "sales_meeting_sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "transcript_reconciliation_version",
                table: "sales_meeting_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "evidence_stale_at",
                table: "sales_meeting_minutes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence_stale_reason",
                table: "sales_meeting_minutes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_evidence_stale",
                table: "sales_meeting_minutes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "evidence_stale_at",
                table: "sales_meeting_internal_intelligence",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence_stale_reason",
                table: "sales_meeting_internal_intelligence",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_evidence_stale",
                table: "sales_meeting_internal_intelligence",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "sales_meeting_transcript_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_meeting_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider_online_meeting_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider_subscription_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    provider_resource = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    client_state_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    retention_until_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_notification_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_renewed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    renewal_attempt_count = table.Column<int>(type: "int", nullable: false),
                    authenticity_failure_count = table.Column<int>(type: "int", nullable: false),
                    last_error_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_transcript_subscriptions", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_transcript_subscriptions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_subscriptions_calendar_connections_company_id_calendar_connection_id",
                        columns: x => new { x.company_id, x.calendar_connection_id },
                        principalTable: "calendar_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_subscriptions_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_provider_transcripts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_transcript_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider_version = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    metadata_json = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    provider_created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    retention_until_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_provider_transcripts", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_provider_transcripts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_meeting_provider_transcripts_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_provider_transcripts_sales_meeting_transcript_subscriptions_company_id_subscription_id",
                        columns: x => new { x.company_id, x.subscription_id },
                        principalTable: "sales_meeting_transcript_subscriptions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_transcript_ingestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_meeting_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider_transcript_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider_version = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    retention_until_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    equivalent_count = table.Column<int>(type: "int", nullable: false),
                    added_count = table.Column<int>(type: "int", nullable: false),
                    speaker_correction_count = table.Column<int>(type: "int", nullable: false),
                    conflict_count = table.Column<int>(type: "int", nullable: false),
                    materially_changed = table.Column<bool>(type: "bit", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    processing_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_transcript_ingestions", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_transcript_ingestions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_ingestions_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_ingestions_sales_meeting_transcript_subscriptions_company_id_subscription_id",
                        columns: x => new { x.company_id, x.subscription_id },
                        principalTable: "sales_meeting_transcript_subscriptions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_meeting_transcript_provenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_transcript_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    transcript_segment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_segment_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    provider_version = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider_content_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    provider_content = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    provider_speaker_label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    provider_started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    provider_ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    match_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    before_content = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    before_speaker_label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    conflict_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    requires_review = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_transcript_provenance", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_transcript_provenance_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_provenance_sales_meeting_provider_transcripts_company_id_provider_transcript_id",
                        columns: x => new { x.company_id, x.provider_transcript_id },
                        principalTable: "sales_meeting_provider_transcripts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_provenance_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_sales_meeting_transcript_provenance_sales_meeting_transcript_segments_company_id_transcript_segment_id",
                        columns: x => new { x.company_id, x.transcript_segment_id },
                        principalTable: "sales_meeting_transcript_segments",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_provider_transcripts_company_id_session_id_provider_transcript_id",
                table: "sales_meeting_provider_transcripts",
                columns: new[] { "company_id", "session_id", "provider_transcript_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_provider_transcripts_company_id_subscription_id",
                table: "sales_meeting_provider_transcripts",
                columns: new[] { "company_id", "subscription_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_provider_transcripts_retention_until_at",
                table: "sales_meeting_provider_transcripts",
                column: "retention_until_at");

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_ingestions_company_id_idempotency_key",
                table: "sales_meeting_transcript_ingestions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_ingestions_company_id_session_id_received_at",
                table: "sales_meeting_transcript_ingestions",
                columns: new[] { "company_id", "session_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_ingestions_company_id_subscription_id",
                table: "sales_meeting_transcript_ingestions",
                columns: new[] { "company_id", "subscription_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_ingestions_retention_until_at",
                table: "sales_meeting_transcript_ingestions",
                column: "retention_until_at");

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_ingestions_status_updated_at",
                table: "sales_meeting_transcript_ingestions",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_provenance_company_id_provider_transcript_id_provider_segment_id_provider_version",
                table: "sales_meeting_transcript_provenance",
                columns: new[] { "company_id", "provider_transcript_id", "provider_segment_id", "provider_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_provenance_company_id_session_id_requires_review",
                table: "sales_meeting_transcript_provenance",
                columns: new[] { "company_id", "session_id", "requires_review" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_provenance_company_id_transcript_segment_id",
                table: "sales_meeting_transcript_provenance",
                columns: new[] { "company_id", "transcript_segment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_subscriptions_company_id_calendar_connection_id",
                table: "sales_meeting_transcript_subscriptions",
                columns: new[] { "company_id", "calendar_connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_subscriptions_company_id_session_id",
                table: "sales_meeting_transcript_subscriptions",
                columns: new[] { "company_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_subscriptions_provider_subscription_id",
                table: "sales_meeting_transcript_subscriptions",
                column: "provider_subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_subscriptions_retention_until_at",
                table: "sales_meeting_transcript_subscriptions",
                column: "retention_until_at");

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_transcript_subscriptions_status_expires_at",
                table: "sales_meeting_transcript_subscriptions",
                columns: new[] { "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_transcript_ingestions");

            migrationBuilder.DropTable(
                name: "sales_meeting_transcript_provenance");

            migrationBuilder.DropTable(
                name: "sales_meeting_provider_transcripts");

            migrationBuilder.DropTable(
                name: "sales_meeting_transcript_subscriptions");

            migrationBuilder.DropColumn(
                name: "last_transcript_reconciliation_id",
                table: "sales_meeting_sessions");

            migrationBuilder.DropColumn(
                name: "transcript_reconciliation_version",
                table: "sales_meeting_sessions");

            migrationBuilder.DropColumn(
                name: "evidence_stale_at",
                table: "sales_meeting_minutes");

            migrationBuilder.DropColumn(
                name: "evidence_stale_reason",
                table: "sales_meeting_minutes");

            migrationBuilder.DropColumn(
                name: "is_evidence_stale",
                table: "sales_meeting_minutes");

            migrationBuilder.DropColumn(
                name: "evidence_stale_at",
                table: "sales_meeting_internal_intelligence");

            migrationBuilder.DropColumn(
                name: "evidence_stale_reason",
                table: "sales_meeting_internal_intelligence");

            migrationBuilder.DropColumn(
                name: "is_evidence_stale",
                table: "sales_meeting_internal_intelligence");
        }
    }
}
