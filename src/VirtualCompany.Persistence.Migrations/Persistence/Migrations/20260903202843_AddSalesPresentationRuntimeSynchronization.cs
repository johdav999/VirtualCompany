using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPresentationRuntimeSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "last_presentation_command_id",
                table: "sales_meeting_sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_presentation_sequence",
                table: "sales_meeting_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_presentation_command_id",
                table: "sales_meeting_sessions");

            migrationBuilder.DropColumn(
                name: "last_presentation_sequence",
                table: "sales_meeting_sessions");
        }
    }
}
