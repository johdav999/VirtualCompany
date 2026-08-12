using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceMarketingDestinationTenantKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_marketing_channel_destinations_marketing_channel_connections_marketing_channel_connection_id",
                table: "marketing_channel_destinations");

            migrationBuilder.DropIndex(
                name: "IX_marketing_channel_destinations_marketing_channel_connection_id",
                table: "marketing_channel_destinations");

            migrationBuilder.AddForeignKey(
                name: "FK_marketing_channel_destinations_marketing_channel_connections_company_id_marketing_channel_connection_id",
                table: "marketing_channel_destinations",
                columns: new[] { "company_id", "marketing_channel_connection_id" },
                principalTable: "marketing_channel_connections",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_marketing_channel_destinations_marketing_channel_connections_company_id_marketing_channel_connection_id",
                table: "marketing_channel_destinations");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_destinations_marketing_channel_connection_id",
                table: "marketing_channel_destinations",
                column: "marketing_channel_connection_id");

            migrationBuilder.AddForeignKey(
                name: "FK_marketing_channel_destinations_marketing_channel_connections_marketing_channel_connection_id",
                table: "marketing_channel_destinations",
                column: "marketing_channel_connection_id",
                principalTable: "marketing_channel_connections",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
