using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingCreativeAssetSafetyScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_creative_asset_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_creative_asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    scanner_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    scanned_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_creative_asset_scans", x => x.id);
                    table.UniqueConstraint("AK_marketing_creative_asset_scans_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_creative_asset_scans_marketing_creative_assets_company_id_marketing_creative_asset_id",
                        columns: x => new { x.company_id, x.marketing_creative_asset_id },
                        principalTable: "marketing_creative_assets",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_creative_asset_scans_company_id",
                table: "marketing_creative_asset_scans",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_creative_asset_scans_company_id_marketing_creative_asset_id_scanned_at",
                table: "marketing_creative_asset_scans",
                columns: new[] { "company_id", "marketing_creative_asset_id", "scanned_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_creative_asset_scans");
        }
    }
}
