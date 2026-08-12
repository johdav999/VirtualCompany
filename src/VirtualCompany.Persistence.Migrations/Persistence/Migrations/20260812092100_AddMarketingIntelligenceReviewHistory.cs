using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingIntelligenceReviewHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_intelligence_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_intelligence_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    review_number = table.Column<int>(type: "int", nullable: false),
                    reviewer_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    rationale = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_intelligence_reviews", x => x.id);
                    table.UniqueConstraint("AK_marketing_intelligence_reviews_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_intelligence_reviews_marketing_intelligence_records_company_id_marketing_intelligence_record_id",
                        columns: x => new { x.company_id, x.marketing_intelligence_record_id },
                        principalTable: "marketing_intelligence_records",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_intelligence_reviews_company_id",
                table: "marketing_intelligence_reviews",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_intelligence_reviews_company_id_marketing_intelligence_record_id_review_number",
                table: "marketing_intelligence_reviews",
                columns: new[] { "company_id", "marketing_intelligence_record_id", "review_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_intelligence_reviews");
        }
    }
}
