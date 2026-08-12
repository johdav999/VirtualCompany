using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceMarketingJourneyVersionTenantLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_marketing_lifecycle_journeys_marketing_lifecycle_journeys_company_id_supersedes_journey_id",
                table: "marketing_lifecycle_journeys",
                columns: new[] { "company_id", "supersedes_journey_id" },
                principalTable: "marketing_lifecycle_journeys",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_marketing_lifecycle_journeys_marketing_lifecycle_journeys_company_id_supersedes_journey_id",
                table: "marketing_lifecycle_journeys");
        }
    }
}
