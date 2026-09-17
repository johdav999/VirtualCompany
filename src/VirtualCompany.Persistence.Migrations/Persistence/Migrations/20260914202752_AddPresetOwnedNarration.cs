using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPresetOwnedNarration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sales_narration_revisions_CompanyId_SessionId_ManifestHash",
                table: "sales_narration_revisions");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceSlideId",
                table: "sales_narration_segments",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PresetSlideId",
                table: "sales_narration_segments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "DeckId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "AudienceId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PresetVersionId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourcePresetRevisionId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_sales_narration_segment_source",
                table: "sales_narration_segments",
                sql: "([SourceSlideId] IS NOT NULL AND [PresetSlideId] IS NULL) OR ([SourceSlideId] IS NULL AND [PresetSlideId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_revisions_CompanyId_PresetVersionId_ManifestHash",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "PresetVersionId", "ManifestHash" },
                unique: true,
                filter: "[PresetVersionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_revisions_CompanyId_SessionId_ManifestHash",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "SessionId", "ManifestHash" },
                unique: true,
                filter: "[SessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_revisions_CompanyId_SourcePresetRevisionId",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "SourcePresetRevisionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_sales_narration_revision_owner",
                table: "sales_narration_revisions",
                sql: "([PresetVersionId] IS NOT NULL AND [SessionId] IS NULL AND [DeckId] IS NULL AND [AudienceId] IS NULL AND [SourcePresetRevisionId] IS NULL) OR ([PresetVersionId] IS NULL AND [SessionId] IS NOT NULL AND [DeckId] IS NOT NULL AND [AudienceId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_narration_revisions_sales_narration_revisions_CompanyId_SourcePresetRevisionId",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "SourcePresetRevisionId" },
                principalTable: "sales_narration_revisions",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_narration_revisions_sales_presentation_preset_versions_CompanyId_PresetVersionId",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "PresetVersionId" },
                principalTable: "sales_presentation_preset_versions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sales_narration_revisions WHERE PresetVersionId IS NOT NULL OR SourcePresetRevisionId IS NOT NULL) THROW 51000, 'Preset narration exists. Export and explicitly migrate it before downgrading.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_sales_narration_revisions_sales_narration_revisions_CompanyId_SourcePresetRevisionId",
                table: "sales_narration_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_narration_revisions_sales_presentation_preset_versions_CompanyId_PresetVersionId",
                table: "sales_narration_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_sales_narration_segment_source",
                table: "sales_narration_segments");

            migrationBuilder.DropIndex(
                name: "IX_sales_narration_revisions_CompanyId_PresetVersionId_ManifestHash",
                table: "sales_narration_revisions");

            migrationBuilder.DropIndex(
                name: "IX_sales_narration_revisions_CompanyId_SessionId_ManifestHash",
                table: "sales_narration_revisions");

            migrationBuilder.DropIndex(
                name: "IX_sales_narration_revisions_CompanyId_SourcePresetRevisionId",
                table: "sales_narration_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_sales_narration_revision_owner",
                table: "sales_narration_revisions");

            migrationBuilder.DropColumn(
                name: "PresetSlideId",
                table: "sales_narration_segments");

            migrationBuilder.DropColumn(
                name: "PresetVersionId",
                table: "sales_narration_revisions");

            migrationBuilder.DropColumn(
                name: "SourcePresetRevisionId",
                table: "sales_narration_revisions");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceSlideId",
                table: "sales_narration_segments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "DeckId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "AudienceId",
                table: "sales_narration_revisions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_narration_revisions_CompanyId_SessionId_ManifestHash",
                table: "sales_narration_revisions",
                columns: new[] { "CompanyId", "SessionId", "ManifestHash" },
                unique: true);
        }
    }
}
