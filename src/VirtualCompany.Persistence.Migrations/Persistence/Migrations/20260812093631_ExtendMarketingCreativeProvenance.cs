using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendMarketingCreativeProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "audit_reference",
                table: "marketing_creative_assets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "marketing_content_variant_id",
                table: "marketing_creative_assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provenance_json",
                table: "marketing_creative_assets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_asset_ids_json",
                table: "marketing_creative_assets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE marketing_creative_assets
                SET audit_reference = CONCAT('marketing-creative:', CONVERT(nvarchar(36), id), ':v', version_number),
                    provenance_json = '{"origin":"legacy_asset","copyrightStatus":"operator_attestation_required","likenessReviewRequired":true}',
                    source_asset_ids_json = '[]'
                WHERE audit_reference = '' OR provenance_json = '' OR source_asset_ids_json = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_creative_assets_company_id_marketing_content_variant_id",
                table: "marketing_creative_assets",
                columns: new[] { "company_id", "marketing_content_variant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_marketing_creative_assets_company_id_marketing_content_variant_id",
                table: "marketing_creative_assets");

            migrationBuilder.DropColumn(
                name: "audit_reference",
                table: "marketing_creative_assets");

            migrationBuilder.DropColumn(
                name: "marketing_content_variant_id",
                table: "marketing_creative_assets");

            migrationBuilder.DropColumn(
                name: "provenance_json",
                table: "marketing_creative_assets");

            migrationBuilder.DropColumn(
                name: "source_asset_ids_json",
                table: "marketing_creative_assets");
        }
    }
}
