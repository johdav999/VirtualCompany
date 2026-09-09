using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260904072045_AddSalesMeetingRealtimeVoicePilot")]
public partial class AddSalesMeetingRealtimeVoicePilot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sales_meeting_voice_sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                meeting_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                started_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                media_route = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                provider_session_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                media_transport = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                connected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                last_provider_sequence = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                reconnect_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                audio_duration_ms = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                input_tokens = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                output_tokens = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                last_error_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_meeting_voice_sessions", x => x.id);
                table.UniqueConstraint("AK_sales_meeting_voice_sessions_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_sales_meeting_voice_sessions_expiry", "expires_at > created_at");
                table.CheckConstraint("CK_sales_meeting_voice_sessions_usage", "audio_duration_ms >= 0 AND input_tokens >= 0 AND output_tokens >= 0 AND reconnect_count >= 0");
                table.ForeignKey(
                    name: "FK_sales_meeting_voice_sessions_agents_company_id_agent_id",
                    columns: x => new { x.company_id, x.agent_id },
                    principalTable: "agents",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_meeting_voice_sessions_sales_meeting_sessions_company_id_meeting_session_id",
                    columns: x => new { x.company_id, x.meeting_session_id },
                    principalTable: "sales_meeting_sessions",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sales_meeting_voice_event_receipts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                voice_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                provider_event_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false),
                event_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                question_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                result_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                reason_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_meeting_voice_event_receipts", x => x.id);
                table.CheckConstraint("CK_sales_meeting_voice_event_receipts_sequence", "sequence >= 1");
                table.ForeignKey(
                    name: "FK_sales_meeting_voice_event_receipts_sales_meeting_voice_sessions_company_id_voice_session_id",
                    columns: x => new { x.company_id, x.voice_session_id },
                    principalTable: "sales_meeting_voice_sessions",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sales_meeting_voice_event_receipts_company_id_voice_session_id_provider_event_id",
            table: "sales_meeting_voice_event_receipts",
            columns: new[] { "company_id", "voice_session_id", "provider_event_id" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_sales_meeting_voice_event_receipts_company_id_voice_session_id_sequence",
            table: "sales_meeting_voice_event_receipts",
            columns: new[] { "company_id", "voice_session_id", "sequence" });
        migrationBuilder.CreateIndex(
            name: "IX_sales_meeting_voice_sessions_company_id_expires_at",
            table: "sales_meeting_voice_sessions",
            columns: new[] { "company_id", "expires_at" });
        migrationBuilder.CreateIndex(
            name: "IX_sales_meeting_voice_sessions_company_id_meeting_session_id_status",
            table: "sales_meeting_voice_sessions",
            columns: new[] { "company_id", "meeting_session_id", "status" });
        migrationBuilder.CreateIndex(
            name: "IX_sales_meeting_voice_sessions_company_id_provider_session_id",
            table: "sales_meeting_voice_sessions",
            columns: new[] { "company_id", "provider_session_id" },
            unique: true,
            filter: "[provider_session_id] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_sales_meeting_voice_sessions_company_id_agent_id",
            table: "sales_meeting_voice_sessions",
            columns: new[] { "company_id", "agent_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sales_meeting_voice_event_receipts");
        migrationBuilder.DropTable(name: "sales_meeting_voice_sessions");
    }
}
