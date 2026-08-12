using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendMarketingContentBriefGrounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_policy_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "customer_insight",
                table: "marketing_content_briefs",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "Not specified");

            migrationBuilder.AddColumn<string>(
                name: "desired_formats_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "evidence_requirements_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "funnel_stage",
                table: "marketing_content_briefs",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "awareness");

            migrationBuilder.AddColumn<string>(
                name: "key_message",
                table: "marketing_content_briefs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "Not specified");

            migrationBuilder.AddColumn<Guid>(
                name: "marketing_customer_segment_version_id",
                table: "marketing_content_briefs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "measurable_objective",
                table: "marketing_content_briefs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "Not specified");

            migrationBuilder.AddColumn<string>(
                name: "offer",
                table: "marketing_content_briefs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "Not specified");

            migrationBuilder.AddColumn<string>(
                name: "prohibited_claims_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "required_claims_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "seo_requirements_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "supporting_points_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "variant_requirements_json",
                table: "marketing_content_briefs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "visual_direction",
                table: "marketing_content_briefs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "Not specified");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval_policy_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "customer_insight",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "desired_formats_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "evidence_requirements_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "funnel_stage",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "key_message",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "marketing_customer_segment_version_id",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "measurable_objective",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "offer",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "prohibited_claims_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "required_claims_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "seo_requirements_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "supporting_points_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "variant_requirements_json",
                table: "marketing_content_briefs");

            migrationBuilder.DropColumn(
                name: "visual_direction",
                table: "marketing_content_briefs");
        }
    }
}
