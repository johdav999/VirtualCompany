using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBriefingCadenceDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cadence_settings_json",
                table: "company_briefing_delivery_preferences",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "briefing_cadence_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Cadence = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SlotKey = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    SettingsHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ScheduledUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Routing = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_briefing_cadence_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_briefing_cadence_deliveries_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_briefing_cadence_deliveries_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_briefing_cadence_deliveries_users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_briefing_cadence_deliveries_CompanyId_OwnerUserId_ScheduledUtc",
                table: "briefing_cadence_deliveries",
                columns: new[] { "CompanyId", "OwnerUserId", "ScheduledUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_briefing_cadence_deliveries_CompanyId_OwnerUserId_SlotKey",
                table: "briefing_cadence_deliveries",
                columns: new[] { "CompanyId", "OwnerUserId", "SlotKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_briefing_cadence_deliveries_OwnerUserId",
                table: "briefing_cadence_deliveries",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_briefing_cadence_deliveries_RecipientUserId",
                table: "briefing_cadence_deliveries",
                column: "RecipientUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "briefing_cadence_deliveries");

            migrationBuilder.DropColumn(
                name: "cadence_settings_json",
                table: "company_briefing_delivery_preferences");
        }
    }
}
