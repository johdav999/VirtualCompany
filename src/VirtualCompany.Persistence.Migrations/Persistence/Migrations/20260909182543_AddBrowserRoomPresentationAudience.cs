using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserRoomPresentationAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_room_presentation_audience",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantGeneration = table.Column<long>(type: "bigint", nullable: false),
                    DeckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeckVersion = table.Column<int>(type: "int", nullable: false),
                    SlideNumber = table.Column<int>(type: "int", nullable: false),
                    PresentationSequence = table.Column<long>(type: "bigint", nullable: false),
                    PresentationVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RenderedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OverrideUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisconnectedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_room_presentation_audience", x => x.Id);
                    table.UniqueConstraint("AK_sales_room_presentation_audience_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_sales_room_presentation_audience_sales_browser_rooms_CompanyId_RoomId",
                        columns: x => new { x.CompanyId, x.RoomId },
                        principalTable: "sales_browser_rooms",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_sales_room_presentation_audience_sales_room_participants_CompanyId_ParticipantId",
                        columns: x => new { x.CompanyId, x.ParticipantId },
                        principalTable: "sales_room_participants",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_presentation_audience_CompanyId_ParticipantId",
                table: "sales_room_presentation_audience",
                columns: new[] { "CompanyId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_presentation_audience_CompanyId_RoomId_PresentationVersion",
                table: "sales_room_presentation_audience",
                columns: new[] { "CompanyId", "RoomId", "PresentationVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_room_presentation_audience_CompanyId_RoomId_PresentationVersion_ParticipantId",
                table: "sales_room_presentation_audience",
                columns: new[] { "CompanyId", "RoomId", "PresentationVersion", "ParticipantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_room_presentation_audience");
        }
    }
}
