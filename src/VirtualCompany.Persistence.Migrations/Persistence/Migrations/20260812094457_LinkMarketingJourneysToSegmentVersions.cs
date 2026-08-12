using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkMarketingJourneysToSegmentVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "marketing_customer_segment_version_id",
                table: "marketing_lifecycle_journeys",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_lifecycle_journeys_company_id_marketing_customer_segment_version_id",
                table: "marketing_lifecycle_journeys",
                columns: new[] { "company_id", "marketing_customer_segment_version_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_marketing_lifecycle_journeys_marketing_customer_segment_versions_company_id_marketing_customer_segment_version_id",
                table: "marketing_lifecycle_journeys",
                columns: new[] { "company_id", "marketing_customer_segment_version_id" },
                principalTable: "marketing_customer_segment_versions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_marketing_lifecycle_journeys_marketing_customer_segment_versions_company_id_marketing_customer_segment_version_id",
                table: "marketing_lifecycle_journeys");

            migrationBuilder.DropIndex(
                name: "IX_marketing_lifecycle_journeys_company_id_marketing_customer_segment_version_id",
                table: "marketing_lifecycle_journeys");

            migrationBuilder.DropColumn(
                name: "marketing_customer_segment_version_id",
                table: "marketing_lifecycle_journeys");
        }
    }
}
