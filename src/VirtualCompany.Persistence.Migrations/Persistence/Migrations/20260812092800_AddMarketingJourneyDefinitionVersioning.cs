using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingJourneyDefinitionVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "concurrency_version",
                table: "marketing_lifecycle_journeys",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_journey_id",
                table: "marketing_lifecycle_journeys",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_lifecycle_journeys_company_id_supersedes_journey_id_version",
                table: "marketing_lifecycle_journeys",
                columns: new[] { "company_id", "supersedes_journey_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_marketing_lifecycle_journeys_company_id_supersedes_journey_id_version",
                table: "marketing_lifecycle_journeys");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "marketing_lifecycle_journeys");

            migrationBuilder.DropColumn(
                name: "supersedes_journey_id",
                table: "marketing_lifecycle_journeys");
        }
    }
}
