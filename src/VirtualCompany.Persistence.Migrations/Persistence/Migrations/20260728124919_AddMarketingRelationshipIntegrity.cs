using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingRelationshipIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_objectives_company_id_marketing_objective_id",
                table: "marketing_plan_objectives",
                columns: new[] { "company_id", "marketing_objective_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_marketing_content_variants_marketing_content_briefs_company_id_marketing_content_brief_id",
                table: "marketing_content_variants",
                columns: new[] { "company_id", "marketing_content_brief_id" },
                principalTable: "marketing_content_briefs",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_marketing_plan_objectives_marketing_objectives_company_id_marketing_objective_id",
                table: "marketing_plan_objectives",
                columns: new[] { "company_id", "marketing_objective_id" },
                principalTable: "marketing_objectives",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_marketing_plan_objectives_marketing_plans_company_id_marketing_plan_id",
                table: "marketing_plan_objectives",
                columns: new[] { "company_id", "marketing_plan_id" },
                principalTable: "marketing_plans",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_marketing_content_variants_marketing_content_briefs_company_id_marketing_content_brief_id",
                table: "marketing_content_variants");

            migrationBuilder.DropForeignKey(
                name: "FK_marketing_plan_objectives_marketing_objectives_company_id_marketing_objective_id",
                table: "marketing_plan_objectives");

            migrationBuilder.DropForeignKey(
                name: "FK_marketing_plan_objectives_marketing_plans_company_id_marketing_plan_id",
                table: "marketing_plan_objectives");

            migrationBuilder.DropIndex(
                name: "IX_marketing_plan_objectives_company_id_marketing_objective_id",
                table: "marketing_plan_objectives");
        }
    }
}
