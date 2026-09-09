using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserSalesRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_browser_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeetingSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AgentHealth = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProvisionOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LiveStartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_browser_rooms", x => x.Id);
                    table.UniqueConstraint("AK_sales_browser_rooms_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_browser_rooms_sales_meeting_sessions_CompanyId_MeetingSessionId",
                        columns: x => new { x.CompanyId, x.MeetingSessionId },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_room_invitation_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecretHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RedeemedParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Revoked = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_invitation_grants", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_invitation_grants_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_invitation_grants_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_room_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProblemCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_operations", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_operations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_operations_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_room_participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SessionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    AiProcessingAllowed = table.Column<bool>(type: "bit", nullable: false),
                    TranscriptRetentionAllowed = table.Column<bool>(type: "bit", nullable: false),
                    LastTokenExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Connected = table.Column<bool>(type: "bit", nullable: false),
                    LastProviderEventUtc = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_participants", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_participants_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_participants_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_room_provider_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ParticipantIdentity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OccurredUnixSeconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_provider_events", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_provider_events_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_provider_events_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "sales_room_consents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    NoticeVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_consents", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_consents_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_consents_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_consents_sales_room_participants_CompanyId_ParticipantId",
                        columns: x => new { x.CompanyId, x.ParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_browser_rooms_CompanyId_MeetingSessionId",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "MeetingSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_browser_rooms_CompanyId_State_ExpiresUtc",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "State", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_browser_rooms_ProviderReference",
                table: "sales_browser_rooms",
                column: "ProviderReference",
                unique: true,
                filter: "[ProviderReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_consents_CompanyId_ParticipantId_Version",
                table: "sales_room_consents",
                columns: new[] { "CompanyId", "ParticipantId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_consents_CompanyId_RoomId",
                table: "sales_room_consents",
                columns: new[] { "CompanyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_invitation_grants_CompanyId_RoomId",
                table: "sales_room_invitation_grants",
                columns: new[] { "CompanyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_invitation_grants_SecretHash",
                table: "sales_room_invitation_grants",
                column: "SecretHash",
                unique: true,
                filter: "[SecretHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_operations_CompanyId_CommandId",
                table: "sales_room_operations",
                columns: new[] { "CompanyId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_operations_CompanyId_RoomId",
                table: "sales_room_operations",
                columns: new[] { "CompanyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_operations_State_LeaseUntilUtc",
                table: "sales_room_operations",
                columns: new[] { "State", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_participants_CompanyId_RoomId_MemberUserId",
                table: "sales_room_participants",
                columns: new[] { "CompanyId", "RoomId", "MemberUserId" },
                unique: true,
                filter: "[MemberUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_participants_SessionHash",
                table: "sales_room_participants",
                column: "SessionHash",
                unique: true,
                filter: "[SessionHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_provider_events_CompanyId_RoomId",
                table: "sales_room_provider_events",
                columns: new[] { "CompanyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_provider_events_ProviderEventId",
                table: "sales_room_provider_events",
                column: "ProviderEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_room_consents");

            migrationBuilder.DropTable(
                name: "sales_room_invitation_grants");

            migrationBuilder.DropTable(
                name: "sales_room_operations");

            migrationBuilder.DropTable(
                name: "sales_room_provider_events");

            migrationBuilder.DropTable(
                name: "sales_room_participants");

            migrationBuilder.DropTable(
                name: "sales_browser_rooms");
        }
    }
}
