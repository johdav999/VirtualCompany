using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredMarketingSegmentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_segment_artifact_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mapping_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_artifact_mappings", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_artifact_mappings_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_segment_artifact_mappings_marketing_customer_segment_versions_company_id_segment_version_id",
                        columns: x => new { x.company_id, x.segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_segment_economic_estimates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    metric_code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    low = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    high = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    method = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    source_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    classification = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_economic_estimates", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_economic_estimates_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_segment_economic_estimates_marketing_customer_segment_versions_company_id_segment_version_id",
                        columns: x => new { x.company_id, x.segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_segment_score_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_threshold = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    missing_evidence_behavior = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    exclusions_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    risk_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_score_policies", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_score_policies_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_segment_score_policies_marketing_customer_segment_versions_company_id_segment_version_id",
                        columns: x => new { x.company_id, x.segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_segment_size_estimates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    low = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    high = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    period = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    geography = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    method = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    assumptions_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    source_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    as_of_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    classification = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_size_estimates", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_size_estimates_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_segment_size_estimates_marketing_customer_segment_versions_company_id_segment_version_id",
                        columns: x => new { x.company_id, x.segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_segment_target_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    rationale = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    expected_impact_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    risks_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    review_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    approval_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_target_decisions", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_target_decisions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_segment_target_decisions_marketing_customer_segment_versions_company_id_segment_version_id",
                        columns: x => new { x.company_id, x.segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "marketing_segment_score_dimensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    score_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    weight = table.Column<decimal>(type: "decimal(6,5)", precision: 6, scale: 5, nullable: false),
                    score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_score_dimensions", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_score_dimensions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_segment_score_dimensions_marketing_segment_score_policies_company_id_score_policy_id",
                        columns: x => new { x.company_id, x.score_policy_id },
                        principalTable: "marketing_segment_score_policies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_artifact_mappings_company_id",
                table: "marketing_segment_artifact_mappings",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_artifact_mappings_company_id_idempotency_key",
                table: "marketing_segment_artifact_mappings",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_artifact_mappings_company_id_segment_version_id_mapping_type_artifact_id",
                table: "marketing_segment_artifact_mappings",
                columns: new[] { "company_id", "segment_version_id", "mapping_type", "artifact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_economic_estimates_company_id",
                table: "marketing_segment_economic_estimates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_economic_estimates_company_id_segment_version_id_metric_code",
                table: "marketing_segment_economic_estimates",
                columns: new[] { "company_id", "segment_version_id", "metric_code" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_score_dimensions_company_id",
                table: "marketing_segment_score_dimensions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_score_dimensions_company_id_score_policy_id_code",
                table: "marketing_segment_score_dimensions",
                columns: new[] { "company_id", "score_policy_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_score_policies_company_id",
                table: "marketing_segment_score_policies",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_score_policies_company_id_segment_version_id",
                table: "marketing_segment_score_policies",
                columns: new[] { "company_id", "segment_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_size_estimates_company_id",
                table: "marketing_segment_size_estimates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_size_estimates_company_id_segment_version_id_method",
                table: "marketing_segment_size_estimates",
                columns: new[] { "company_id", "segment_version_id", "method" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_target_decisions_company_id",
                table: "marketing_segment_target_decisions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_target_decisions_company_id_idempotency_key",
                table: "marketing_segment_target_decisions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_target_decisions_company_id_segment_version_id",
                table: "marketing_segment_target_decisions",
                columns: new[] { "company_id", "segment_version_id" });

            migrationBuilder.Sql(@"
INSERT INTO marketing_segment_size_estimates
    (id, company_id, segment_version_id, low, high, unit, period, geography, currency, method,
     assumptions_json, source_ids_json, confidence, observed_at, as_of_at, classification, created_at)
SELECT NEWID(), company_id, id, CONVERT(decimal(19,4), size_low), CONVERT(decimal(19,4), size_high),
       N'entities', N'legacy_unspecified', N'legacy_unspecified', NULL, N'legacy_unverified',
       N'[{""gap"":""Legacy size fields did not preserve unit, period, geography, assumptions, or sources.""}]',
       N'[]', confidence, evidence_observed_at, evidence_observed_at, N'gap', SYSUTCDATETIME()
FROM marketing_customer_segment_versions
WHERE size_low IS NOT NULL OR size_high IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_segment_artifact_mappings");

            migrationBuilder.DropTable(
                name: "marketing_segment_economic_estimates");

            migrationBuilder.DropTable(
                name: "marketing_segment_score_dimensions");

            migrationBuilder.DropTable(
                name: "marketing_segment_size_estimates");

            migrationBuilder.DropTable(
                name: "marketing_segment_target_decisions");

            migrationBuilder.DropTable(
                name: "marketing_segment_score_policies");
        }
    }
}
