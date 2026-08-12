using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingCreativeAssetVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "asset_family_id",
                table: "marketing_creative_assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version_number",
                table: "marketing_creative_assets",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                "UPDATE marketing_creative_assets SET asset_family_id = id WHERE asset_family_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "asset_family_id",
                table: "marketing_creative_assets",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_creative_assets_company_id_asset_family_id_version_number",
                table: "marketing_creative_assets",
                columns: new[] { "company_id", "asset_family_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_marketing_creative_assets_company_id_asset_family_id_version_number",
                table: "marketing_creative_assets");

            migrationBuilder.DropColumn(
                name: "asset_family_id",
                table: "marketing_creative_assets");

            migrationBuilder.DropColumn(
                name: "version_number",
                table: "marketing_creative_assets");
        }
    }
}
