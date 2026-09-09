using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsMeetingCallControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teams_meeting_calls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meeting_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organizer_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_call_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    meeting_reference_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    safe_provider_reference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    state = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    provider_state = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    action_version = table.Column<long>(type: "bigint", nullable: false),
                    join_idempotency_key = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    last_callback_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_callback_version = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    media_host_instance_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    consent_evidence_version = table.Column<long>(type: "bigint", nullable: false),
                    policy_evidence_version = table.Column<long>(type: "bigint", nullable: false),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    reconciliation_count = table.Column<int>(type: "int", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    admitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    connected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ending_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams_meeting_calls", x => x.id);
                    table.UniqueConstraint("AK_teams_meeting_calls_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_teams_meeting_call_state", "state IN ('requested','joining','waiting_in_lobby','admitted','connected','leave_requested','ending','ended','rejected','failed','reconciliation_required')");
                    table.ForeignKey(
                        name: "FK_teams_meeting_calls_sales_meeting_sessions_company_id_meeting_session_id",
                        columns: x => new { x.company_id, x.meeting_session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_teams_meeting_calls_teams_tenant_registrations_company_id_registration_id",
                        columns: x => new { x.company_id, x.registration_id },
                        principalTable: "teams_tenant_registrations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teams_call_notification_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    call_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    resource_version = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    normalized_state = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ignored = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams_call_notification_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_teams_call_notification_receipts_teams_meeting_calls_company_id_call_id",
                        columns: x => new { x.company_id, x.call_id },
                        principalTable: "teams_meeting_calls",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_teams_call_notification_receipts_company_id_call_id_processed_at",
                table: "teams_call_notification_receipts",
                columns: new[] { "company_id", "call_id", "processed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_call_notification_receipts_company_id_event_key",
                table: "teams_call_notification_receipts",
                columns: new[] { "company_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_meeting_calls_company_id_meeting_session_id",
                table: "teams_meeting_calls",
                columns: new[] { "company_id", "meeting_session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_meeting_calls_company_id_provider_call_id",
                table: "teams_meeting_calls",
                columns: new[] { "company_id", "provider_call_id" },
                unique: true,
                filter: "provider_call_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_teams_meeting_calls_company_id_registration_id",
                table: "teams_meeting_calls",
                columns: new[] { "company_id", "registration_id" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_meeting_calls_state_media_host_instance_id",
                table: "teams_meeting_calls",
                columns: new[] { "state", "media_host_instance_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teams_call_notification_receipts");

            migrationBuilder.DropTable(
                name: "teams_meeting_calls");
        }
    }
}
