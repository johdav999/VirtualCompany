using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordMeetingConfirmationThreadingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "confirmation_threading_mode",
                table: "sales_meeting_invitations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddCheckConstraint(
                name: "CK_sales_meeting_invitations_confirmation_threading_mode",
                table: "sales_meeting_invitations",
                sql: "confirmation_threading_mode IN ('unknown', 'native', 'header_based')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_sales_meeting_invitations_confirmation_threading_mode",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_threading_mode",
                table: "sales_meeting_invitations");
        }
    }
}
