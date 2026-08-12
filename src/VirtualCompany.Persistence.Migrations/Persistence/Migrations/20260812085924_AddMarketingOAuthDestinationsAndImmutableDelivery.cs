using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingOAuthDestinationsAndImmutableDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "content_brief_version",
                table: "marketing_channel_actions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "marketing_channel_destinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_channel_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    destination_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    capabilities_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    secret_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    last_discovered_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_channel_destinations", x => x.id);
                    table.UniqueConstraint("AK_marketing_channel_destinations_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_channel_destinations_marketing_channel_connections_marketing_channel_connection_id",
                        column: x => x.marketing_channel_connection_id,
                        principalTable: "marketing_channel_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_channel_oauth_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    state_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    redirect_uri = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_channel_oauth_sessions", x => x.id);
                    table.UniqueConstraint("AK_marketing_channel_oauth_sessions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_destinations_company_id",
                table: "marketing_channel_destinations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_destinations_company_id_marketing_channel_connection_id_provider_reference",
                table: "marketing_channel_destinations",
                columns: new[] { "company_id", "marketing_channel_connection_id", "provider_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_destinations_company_id_status",
                table: "marketing_channel_destinations",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_destinations_marketing_channel_connection_id",
                table: "marketing_channel_destinations",
                column: "marketing_channel_connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_oauth_sessions_company_id",
                table: "marketing_channel_oauth_sessions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_oauth_sessions_company_id_state_hash",
                table: "marketing_channel_oauth_sessions",
                columns: new[] { "company_id", "state_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_oauth_sessions_company_id_status_expires_at",
                table: "marketing_channel_oauth_sessions",
                columns: new[] { "company_id", "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_channel_destinations");

            migrationBuilder.DropTable(
                name: "marketing_channel_oauth_sessions");

            migrationBuilder.DropColumn(
                name: "content_brief_version",
                table: "marketing_channel_actions");
        }
    }
}
