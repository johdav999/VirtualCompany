using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingContentVariantGenerationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "batch_index",
                table: "marketing_content_variants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "capability_version",
                table: "marketing_content_variants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<string>(
                name: "content_format",
                table: "marketing_content_variants",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "generation_run_id",
                table: "marketing_content_variants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "marketing_content_variants",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prompt_version",
                table: "marketing_content_variants",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<Guid>(
                name: "variant_family_id",
                table: "marketing_content_variants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version_number",
                table: "marketing_content_variants",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                "UPDATE marketing_content_variants SET variant_family_id = id WHERE variant_family_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "variant_family_id",
                table: "marketing_content_variants",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_content_variants_company_id_idempotency_key_batch_index",
                table: "marketing_content_variants",
                columns: new[] { "company_id", "idempotency_key", "batch_index" },
                unique: true,
                filter: "[idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_content_variants_company_id_variant_family_id_version_number",
                table: "marketing_content_variants",
                columns: new[] { "company_id", "variant_family_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_marketing_content_variants_company_id_idempotency_key_batch_index",
                table: "marketing_content_variants");

            migrationBuilder.DropIndex(
                name: "IX_marketing_content_variants_company_id_variant_family_id_version_number",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "batch_index",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "capability_version",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "content_format",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "generation_run_id",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "prompt_version",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "variant_family_id",
                table: "marketing_content_variants");

            migrationBuilder.DropColumn(
                name: "version_number",
                table: "marketing_content_variants");
        }
    }
}
