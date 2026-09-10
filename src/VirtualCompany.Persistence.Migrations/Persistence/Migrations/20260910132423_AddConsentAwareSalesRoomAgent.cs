using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentAwareSalesRoomAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AgentDetectedSpeechMilliseconds",
                table: "sales_browser_rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AgentForwardedAudioMilliseconds",
                table: "sales_browser_rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AgentGeneration",
                table: "sales_browser_rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "sales_browser_rooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgentInputTokens",
                table: "sales_browser_rooms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AgentLastErrorCode",
                table: "sales_browser_rooms",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentLastErrorSummary",
                table: "sales_browser_rooms",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgentLeaseExpiresUtc",
                table: "sales_browser_rooms",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgentLeaseOwnerId",
                table: "sales_browser_rooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AgentOutputAudioMilliseconds",
                table: "sales_browser_rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AgentOutputTokens",
                table: "sales_browser_rooms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "AgentProviderBilledAudioMilliseconds",
                table: "sales_browser_rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AgentReceivedAudioMilliseconds",
                table: "sales_browser_rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "AgentStartedByUserId",
                table: "sales_browser_rooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgentStartedUtc",
                table: "sales_browser_rooms",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgentStoppedUtc",
                table: "sales_browser_rooms",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AgentTurnGeneration",
                table: "sales_browser_rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "AgentVoiceHealth",
                table: "sales_browser_rooms",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "not_connected");

            migrationBuilder.CreateTable(
                name: "sales_room_agent_speech",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentGeneration = table.Column<long>(type: "bigint", nullable: false),
                    TurnGeneration = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    NarrationRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NarrationSegmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OffsetMilliseconds = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ReleasedText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: true),
                    ProviderResponseId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DurationMilliseconds = table.Column<int>(type: "int", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_agent_speech", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_agent_speech_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_agent_speech_agents_CompanyId_AgentId",
                        columns: x => new { x.CompanyId, x.AgentId },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_agent_speech_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_agent_speech_sales_meeting_questions_CompanyId_QuestionId",
                        columns: x => new { x.CompanyId, x.QuestionId },
                        principalTable: "sales_meeting_questions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_sales_room_agent_speech_sales_meeting_sessions_CompanyId_SessionId",
                        columns: x => new { x.CompanyId, x.SessionId },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_sales_room_agent_speech_sales_narration_revisions_CompanyId_NarrationRevisionId",
                        columns: x => new { x.CompanyId, x.NarrationRevisionId },
                        principalTable: "sales_narration_revisions",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_agent_speech_sales_narration_segments_CompanyId_NarrationSegmentId",
                        columns: x => new { x.CompanyId, x.NarrationSegmentId },
                        principalTable: "sales_narration_segments",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_room_agent_transcripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantConsentVersion = table.Column<long>(type: "bigint", nullable: false),
                    TrackIdHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TrackGeneration = table.Column<long>(type: "bigint", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Overlapped = table.Column<bool>(type: "bit", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_agent_transcripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_room_agent_transcripts_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_agent_transcripts_sales_room_participants_CompanyId_ParticipantId",
                        columns: x => new { x.CompanyId, x.ParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_browser_rooms_CompanyId_AgentId",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_speech_CompanyId_AgentId",
                table: "sales_room_agent_speech",
                columns: new[] { "CompanyId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_speech_CompanyId_NarrationRevisionId",
                table: "sales_room_agent_speech",
                columns: new[] { "CompanyId", "NarrationRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_speech_CompanyId_NarrationSegmentId",
                table: "sales_room_agent_speech",
                columns: new[] { "CompanyId", "NarrationSegmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_speech_CompanyId_QuestionId",
                table: "sales_room_agent_speech",
                columns: new[] { "CompanyId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_speech_CompanyId_RoomId_CommandId",
                table: "sales_room_agent_speech",
                columns: new[] { "CompanyId", "RoomId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_speech_CompanyId_RoomId_Status_CreatedUtc",
                table: "sales_room_agent_speech",
                columns: new[] { "CompanyId", "RoomId", "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_speech_CompanyId_SessionId",
                table: "sales_room_agent_speech",
                columns: new[] { "CompanyId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_transcripts_CompanyId_ParticipantId",
                table: "sales_room_agent_transcripts",
                columns: new[] { "CompanyId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_agent_transcripts_CompanyId_RoomId_StartedUtc",
                table: "sales_room_agent_transcripts",
                columns: new[] { "CompanyId", "RoomId", "StartedUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_sales_browser_rooms_agents_CompanyId_AgentId",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "AgentId" },
                principalTable: "agents",
                principalColumns: new[] { "CompanyId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_browser_rooms_agents_CompanyId_AgentId",
                table: "sales_browser_rooms");

            migrationBuilder.DropTable(
                name: "sales_room_agent_speech");

            migrationBuilder.DropTable(
                name: "sales_room_agent_transcripts");

            migrationBuilder.DropIndex(
                name: "IX_sales_browser_rooms_CompanyId_AgentId",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentDetectedSpeechMilliseconds",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentForwardedAudioMilliseconds",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentGeneration",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentInputTokens",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentLastErrorCode",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentLastErrorSummary",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentLeaseExpiresUtc",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentLeaseOwnerId",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentOutputAudioMilliseconds",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentOutputTokens",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentProviderBilledAudioMilliseconds",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentReceivedAudioMilliseconds",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentStartedByUserId",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentStartedUtc",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentStoppedUtc",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentTurnGeneration",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "AgentVoiceHealth",
                table: "sales_browser_rooms");
        }
    }
}
