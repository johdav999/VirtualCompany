using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingStrategyCampaignTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_strategy_campaign_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_strategy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_customer_segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_strategy_campaign_links", x => x.id);
                    table.UniqueConstraint("AK_marketing_strategy_campaign_links_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_strategy_campaign_links_marketing_customer_segment_versions_company_id_marketing_customer_segment_version_id",
                        columns: x => new { x.company_id, x.marketing_customer_segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketing_strategy_campaign_links_marketing_plans_company_id_marketing_plan_id",
                        columns: x => new { x.company_id, x.marketing_plan_id },
                        principalTable: "marketing_plans",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketing_strategy_campaign_links_marketing_strategies_company_id_marketing_strategy_id",
                        columns: x => new { x.company_id, x.marketing_strategy_id },
                        principalTable: "marketing_strategies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketing_strategy_campaign_links_sales_campaigns_company_id_sales_campaign_id",
                        columns: x => new { x.company_id, x.sales_campaign_id },
                        principalTable: "sales_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_campaign_links_company_id",
                table: "marketing_strategy_campaign_links",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_campaign_links_company_id_idempotency_key",
                table: "marketing_strategy_campaign_links",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_campaign_links_company_id_marketing_customer_segment_version_id",
                table: "marketing_strategy_campaign_links",
                columns: new[] { "company_id", "marketing_customer_segment_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_campaign_links_company_id_marketing_plan_id",
                table: "marketing_strategy_campaign_links",
                columns: new[] { "company_id", "marketing_plan_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_campaign_links_company_id_marketing_strategy_id_sales_campaign_id",
                table: "marketing_strategy_campaign_links",
                columns: new[] { "company_id", "marketing_strategy_id", "sales_campaign_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_strategy_campaign_links_company_id_sales_campaign_id",
                table: "marketing_strategy_campaign_links",
                columns: new[] { "company_id", "sales_campaign_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_strategy_campaign_links");
        }
    }
}
