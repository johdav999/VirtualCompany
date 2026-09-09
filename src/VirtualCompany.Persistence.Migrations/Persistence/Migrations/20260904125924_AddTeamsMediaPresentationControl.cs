using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsMediaPresentationControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "presentation_control_mode",
                table: "sales_meeting_sessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<DateTime>(
                name: "presentation_control_updated_at",
                table: "sales_meeting_sessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "presentation_control_updated_by_user_id",
                table: "sales_meeting_sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE sales_meeting_sessions
                SET presentation_control_updated_by_user_id = created_by_user_id,
                    presentation_control_updated_at = updated_at
                WHERE presentation_control_updated_by_user_id IS NULL
                   OR presentation_control_updated_at IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "presentation_control_updated_at",
                table: "sales_meeting_sessions",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "presentation_control_updated_by_user_id",
                table: "sales_meeting_sessions",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "presentation_control_mode",
                table: "sales_meeting_sessions");

            migrationBuilder.DropColumn(
                name: "presentation_control_updated_at",
                table: "sales_meeting_sessions");

            migrationBuilder.DropColumn(
                name: "presentation_control_updated_by_user_id",
                table: "sales_meeting_sessions");
        }
    }
}
