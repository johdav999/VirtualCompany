using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsPresenterOrganizerControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "media_start_authorized_at",
                table: "teams_meeting_calls",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "media_start_authorized_by_user_id",
                table: "teams_meeting_calls",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "media_start_authorized_at",
                table: "teams_meeting_calls");

            migrationBuilder.DropColumn(
                name: "media_start_authorized_by_user_id",
                table: "teams_meeting_calls");
        }
    }
}
