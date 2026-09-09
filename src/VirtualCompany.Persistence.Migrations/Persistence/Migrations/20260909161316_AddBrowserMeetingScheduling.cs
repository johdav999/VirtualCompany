using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserMeetingScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sales_browser_rooms_CompanyId_MeetingSessionId",
                table: "sales_browser_rooms");

            migrationBuilder.AddColumn<Guid>(
                name: "browser_room_id",
                table: "sales_meeting_invitations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "conferencing",
                table: "sales_meeting_invitations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");

            // Preserve legacy conferencing semantics using stored fields, never a URL heuristic.
            migrationBuilder.Sql("EXEC(N'UPDATE sales_meeting_invitations SET conferencing = CASE WHEN create_online_meeting = 0 THEN ''none'' WHEN provider = ''microsoft365'' THEN ''teams'' WHEN provider = ''google'' THEN ''google_meet'' ELSE ''none'' END;');");

            migrationBuilder.AddColumn<string>(
                name: "protected_browser_invitation_link",
                table: "sales_meeting_invitations",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "MeetingSessionId",
                table: "sales_browser_rooms",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "InvitationId",
                table: "sales_browser_rooms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_browser_rooms_CompanyId_InvitationId",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "InvitationId" },
                unique: true,
                filter: "[InvitationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_browser_rooms_CompanyId_MeetingSessionId",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "MeetingSessionId" },
                unique: true,
                filter: "[MeetingSessionId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_browser_rooms_sales_meeting_invitations_CompanyId_InvitationId",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "InvitationId" },
                principalTable: "sales_meeting_invitations",
                principalColumns: new[] { "company_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sales_browser_rooms WHERE MeetingSessionId IS NULL) THROW 51000, 'Cannot roll back while browser rooms await meeting-session preparation. Preserve their data and complete preparation first.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_sales_browser_rooms_sales_meeting_invitations_CompanyId_InvitationId",
                table: "sales_browser_rooms");

            migrationBuilder.DropIndex(
                name: "IX_sales_browser_rooms_CompanyId_InvitationId",
                table: "sales_browser_rooms");

            migrationBuilder.DropIndex(
                name: "IX_sales_browser_rooms_CompanyId_MeetingSessionId",
                table: "sales_browser_rooms");

            migrationBuilder.DropColumn(
                name: "browser_room_id",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "conferencing",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "protected_browser_invitation_link",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "InvitationId",
                table: "sales_browser_rooms");

            migrationBuilder.AlterColumn<Guid>(
                name: "MeetingSessionId",
                table: "sales_browser_rooms",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_browser_rooms_CompanyId_MeetingSessionId",
                table: "sales_browser_rooms",
                columns: new[] { "CompanyId", "MeetingSessionId" },
                unique: true);
        }
    }
}
