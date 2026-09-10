using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesRoomFloorControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ResponseGeneration",
                table: "sales_room_agent_speech",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "sales_room_floors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreauthorizedCoHostParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FloorOwnerParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PendingTurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PendingParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PendingParticipantGeneration = table.Column<long>(type: "bigint", nullable: true),
                    PendingQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PendingTurnState = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PendingAddressedAgent = table.Column<bool>(type: "bit", nullable: false),
                    Overlap = table.Column<bool>(type: "bit", nullable: false),
                    TurnGeneration = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    ResponseGeneration = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    PresentationVersion = table.Column<long>(type: "bigint", nullable: false),
                    SlideNumber = table.Column<int>(type: "int", nullable: false),
                    TalkingPointIndex = table.Column<int>(type: "int", nullable: false),
                    ResumeMarker = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResumeOffsetMilliseconds = table.Column<int>(type: "int", nullable: false),
                    ControlMode = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    LastPlaybackStopId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlaybackStopRequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlaybackStopDeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlaybackStopRequiredCount = table.Column<int>(type: "int", nullable: false),
                    PlaybackStopAcknowledgedCount = table.Column<int>(type: "int", nullable: false),
                    PlaybackStopState = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_floors", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_floors_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_floors_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_floors_sales_meeting_questions_CompanyId_PendingQuestionId",
                        columns: x => new { x.CompanyId, x.PendingQuestionId },
                        principalTable: "sales_meeting_questions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_sales_room_floors_sales_room_participants_CompanyId_FloorOwnerParticipantId",
                        columns: x => new { x.CompanyId, x.FloorOwnerParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_floors_sales_room_participants_CompanyId_HostParticipantId",
                        columns: x => new { x.CompanyId, x.HostParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_floors_sales_room_participants_CompanyId_PendingParticipantId",
                        columns: x => new { x.CompanyId, x.PendingParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_floors_sales_room_participants_CompanyId_PreauthorizedCoHostParticipantId",
                        columns: x => new { x.CompanyId, x.PreauthorizedCoHostParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_room_playback_stop_acknowledgements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantGeneration = table.Column<long>(type: "bigint", nullable: false),
                    ResponseGeneration = table.Column<long>(type: "bigint", nullable: false),
                    ConnectionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AcknowledgedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_playback_stop_acknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_room_playback_stop_acknowledgements_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_playback_stop_acknowledgements_sales_room_participants_CompanyId_ParticipantId",
                        columns: x => new { x.CompanyId, x.ParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_floors_CompanyId_FloorOwnerParticipantId",
                table: "sales_room_floors",
                columns: new[] { "CompanyId", "FloorOwnerParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_floors_CompanyId_HostParticipantId",
                table: "sales_room_floors",
                columns: new[] { "CompanyId", "HostParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_floors_CompanyId_LastPlaybackStopId",
                table: "sales_room_floors",
                columns: new[] { "CompanyId", "LastPlaybackStopId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_floors_CompanyId_PendingParticipantId",
                table: "sales_room_floors",
                columns: new[] { "CompanyId", "PendingParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_floors_CompanyId_PendingQuestionId",
                table: "sales_room_floors",
                columns: new[] { "CompanyId", "PendingQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_floors_CompanyId_PreauthorizedCoHostParticipantId",
                table: "sales_room_floors",
                columns: new[] { "CompanyId", "PreauthorizedCoHostParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_floors_CompanyId_RoomId",
                table: "sales_room_floors",
                columns: new[] { "CompanyId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_playback_stop_acknowledgements_CompanyId_ParticipantId",
                table: "sales_room_playback_stop_acknowledgements",
                columns: new[] { "CompanyId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_playback_stop_acknowledgements_CompanyId_RoomId_StopId_ParticipantId_ParticipantGeneration",
                table: "sales_room_playback_stop_acknowledgements",
                columns: new[] { "CompanyId", "RoomId", "StopId", "ParticipantId", "ParticipantGeneration" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_room_floors");

            migrationBuilder.DropTable(
                name: "sales_room_playback_stop_acknowledgements");

            migrationBuilder.DropColumn(
                name: "ResponseGeneration",
                table: "sales_room_agent_speech");
        }
    }
}
