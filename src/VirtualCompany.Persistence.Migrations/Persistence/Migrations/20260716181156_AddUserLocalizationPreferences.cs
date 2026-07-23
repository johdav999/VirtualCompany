using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLocalizationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_preference_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    previous_ui_culture = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    new_ui_culture = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    previous_formatting_culture = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    new_formatting_culture = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    changed_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preference_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_preference_changes_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ui_culture = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "en-GB"),
                    formatting_culture = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_preferences_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_preference_changes_UserId_changed_utc",
                table: "user_preference_changes",
                columns: new[] { "UserId", "changed_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_preference_changes");

            migrationBuilder.DropTable(
                name: "user_preferences");
        }
    }
}
