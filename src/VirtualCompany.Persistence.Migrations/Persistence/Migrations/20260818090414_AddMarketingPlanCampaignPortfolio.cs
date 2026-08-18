using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingPlanCampaignPortfolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "approval_request_id",
                table: "marketing_plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence_references_json",
                table: "marketing_plans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "marketing_strategy_id",
                table: "marketing_plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "marketing_strategy_version",
                table: "marketing_plans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "missing_evidence_json",
                table: "marketing_plans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "rationale",
                table: "marketing_plans",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "Legacy plan");

            migrationBuilder.CreateTable(
                name: "marketing_plan_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    purpose = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    allocated_budget = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    budget_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    expected_contribution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    creating_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_plan_campaigns", x => x.id);
                    table.UniqueConstraint("AK_marketing_plan_campaigns_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_plan_campaigns_marketing_objectives_company_id_marketing_objective_id",
                        columns: x => new { x.company_id, x.marketing_objective_id },
                        principalTable: "marketing_objectives",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketing_plan_campaigns_marketing_plans_company_id_marketing_plan_id",
                        columns: x => new { x.company_id, x.marketing_plan_id },
                        principalTable: "marketing_plans",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_marketing_plan_campaigns_sales_campaigns_company_id_sales_campaign_id",
                        columns: x => new { x.company_id, x.sales_campaign_id },
                        principalTable: "sales_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "marketing_plan_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    expected_contribution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_plan_segments", x => x.id);
                    table.UniqueConstraint("AK_marketing_plan_segments_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_plan_segments_marketing_customer_segment_versions_company_id_segment_version_id",
                        columns: x => new { x.company_id, x.segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_marketing_plan_segments_marketing_plans_company_id_marketing_plan_id",
                        columns: x => new { x.company_id, x.marketing_plan_id },
                        principalTable: "marketing_plans",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_plan_campaign_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_plan_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_plan_segment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    expected_audience_contribution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_plan_campaign_segments", x => x.id);
                    table.UniqueConstraint("AK_marketing_plan_campaign_segments_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_plan_campaign_segments_marketing_plan_campaigns_company_id_marketing_plan_campaign_id",
                        columns: x => new { x.company_id, x.marketing_plan_campaign_id },
                        principalTable: "marketing_plan_campaigns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_marketing_plan_campaign_segments_marketing_plan_segments_company_id_marketing_plan_segment_id",
                        columns: x => new { x.company_id, x.marketing_plan_segment_id },
                        principalTable: "marketing_plan_segments",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                UPDATE p
                SET marketing_strategy_id = basis.marketing_strategy_id,
                    marketing_strategy_version = s.version,
                    rationale = CASE WHEN p.rationale = '' THEN 'Imported from the existing strategy campaign relationship.' ELSE p.rationale END,
                    evidence_references_json = CASE WHEN p.evidence_references_json = '' THEN '[]' ELSE p.evidence_references_json END,
                    missing_evidence_json = CASE WHEN p.missing_evidence_json = '' THEN '[]' ELSE p.missing_evidence_json END
                FROM marketing_plans p
                INNER JOIN (
                    SELECT company_id, marketing_plan_id, MIN(marketing_strategy_id) AS marketing_strategy_id
                    FROM marketing_strategy_campaign_links
                    GROUP BY company_id, marketing_plan_id
                ) basis ON basis.company_id = p.company_id AND basis.marketing_plan_id = p.id
                INNER JOIN marketing_strategies s ON s.company_id = basis.company_id AND s.id = basis.marketing_strategy_id;

                INSERT INTO marketing_plan_segments
                    (id, company_id, marketing_plan_id, segment_version_id, role, priority, rationale, expected_contribution, created_at)
                SELECT MIN(l.id), l.company_id, l.marketing_plan_id, l.marketing_customer_segment_version_id,
                       'primary', 1, 'Imported from the existing strategy campaign relationship.',
                       'Legacy campaign audience coverage.', MIN(l.created_at)
                FROM marketing_strategy_campaign_links l
                WHERE NOT EXISTS (
                    SELECT 1 FROM marketing_plan_segments ps
                    WHERE ps.company_id = l.company_id AND ps.marketing_plan_id = l.marketing_plan_id
                      AND ps.segment_version_id = l.marketing_customer_segment_version_id)
                GROUP BY l.company_id, l.marketing_plan_id, l.marketing_customer_segment_version_id;

                WITH ranked AS (
                    SELECT l.*, ROW_NUMBER() OVER (PARTITION BY l.company_id, l.sales_campaign_id ORDER BY l.created_at, l.id) AS rn
                    FROM marketing_strategy_campaign_links l
                )
                INSERT INTO marketing_plan_campaigns
                    (id, company_id, marketing_plan_id, sales_campaign_id, marketing_objective_id, purpose,
                     allocated_budget, budget_currency, priority, expected_contribution, status, creating_agent_id,
                     idempotency_key, created_at, updated_at)
                SELECT r.id, r.company_id, r.marketing_plan_id, r.sales_campaign_id, NULL,
                       COALESCE(NULLIF(c.description, ''), c.name), NULL, p.budget_currency, 1,
                       'Imported legacy strategy campaign contribution.', 'draft_created', p.owner_agent_id,
                       CONCAT('legacy:', CONVERT(varchar(36), r.id)), r.created_at, r.created_at
                FROM ranked r
                INNER JOIN sales_campaigns c ON c.company_id = r.company_id AND c.id = r.sales_campaign_id
                INNER JOIN marketing_plans p ON p.company_id = r.company_id AND p.id = r.marketing_plan_id
                WHERE r.rn = 1 AND NOT EXISTS (
                    SELECT 1 FROM marketing_plan_campaigns pc
                    WHERE pc.company_id = r.company_id AND pc.sales_campaign_id = r.sales_campaign_id);

                INSERT INTO marketing_plan_campaign_segments
                    (id, company_id, marketing_plan_campaign_id, marketing_plan_segment_id, rationale,
                     expected_audience_contribution, created_at)
                SELECT l.id, l.company_id, pc.id, ps.id,
                       'Imported from the existing strategy campaign relationship.',
                       'Legacy campaign audience coverage.', l.created_at
                FROM marketing_strategy_campaign_links l
                INNER JOIN marketing_plan_campaigns pc ON pc.company_id = l.company_id AND pc.sales_campaign_id = l.sales_campaign_id
                INNER JOIN marketing_plan_segments ps ON ps.company_id = l.company_id
                    AND ps.marketing_plan_id = pc.marketing_plan_id
                    AND ps.segment_version_id = l.marketing_customer_segment_version_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM marketing_plan_campaign_segments pcs
                    WHERE pcs.company_id = l.company_id AND pcs.marketing_plan_campaign_id = pc.id
                      AND pcs.marketing_plan_segment_id = ps.id);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plans_company_id_marketing_strategy_id",
                table: "marketing_plans",
                columns: new[] { "company_id", "marketing_strategy_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_campaign_segments_company_id",
                table: "marketing_plan_campaign_segments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_campaign_segments_company_id_marketing_plan_campaign_id_marketing_plan_segment_id",
                table: "marketing_plan_campaign_segments",
                columns: new[] { "company_id", "marketing_plan_campaign_id", "marketing_plan_segment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_campaign_segments_company_id_marketing_plan_segment_id",
                table: "marketing_plan_campaign_segments",
                columns: new[] { "company_id", "marketing_plan_segment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_campaigns_company_id",
                table: "marketing_plan_campaigns",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_campaigns_company_id_marketing_objective_id",
                table: "marketing_plan_campaigns",
                columns: new[] { "company_id", "marketing_objective_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_campaigns_company_id_marketing_plan_id_idempotency_key",
                table: "marketing_plan_campaigns",
                columns: new[] { "company_id", "marketing_plan_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_campaigns_company_id_sales_campaign_id",
                table: "marketing_plan_campaigns",
                columns: new[] { "company_id", "sales_campaign_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_segments_company_id",
                table: "marketing_plan_segments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_segments_company_id_marketing_plan_id_segment_version_id",
                table: "marketing_plan_segments",
                columns: new[] { "company_id", "marketing_plan_id", "segment_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plan_segments_company_id_segment_version_id",
                table: "marketing_plan_segments",
                columns: new[] { "company_id", "segment_version_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_marketing_plans_marketing_strategies_company_id_marketing_strategy_id",
                table: "marketing_plans",
                columns: new[] { "company_id", "marketing_strategy_id" },
                principalTable: "marketing_strategies",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_marketing_plans_marketing_strategies_company_id_marketing_strategy_id",
                table: "marketing_plans");

            migrationBuilder.DropTable(
                name: "marketing_plan_campaign_segments");

            migrationBuilder.DropTable(
                name: "marketing_plan_campaigns");

            migrationBuilder.DropTable(
                name: "marketing_plan_segments");

            migrationBuilder.DropIndex(
                name: "IX_marketing_plans_company_id_marketing_strategy_id",
                table: "marketing_plans");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                table: "marketing_plans");

            migrationBuilder.DropColumn(
                name: "evidence_references_json",
                table: "marketing_plans");

            migrationBuilder.DropColumn(
                name: "marketing_strategy_id",
                table: "marketing_plans");

            migrationBuilder.DropColumn(
                name: "marketing_strategy_version",
                table: "marketing_plans");

            migrationBuilder.DropColumn(
                name: "missing_evidence_json",
                table: "marketing_plans");

            migrationBuilder.DropColumn(
                name: "rationale",
                table: "marketing_plans");
        }
    }
}
