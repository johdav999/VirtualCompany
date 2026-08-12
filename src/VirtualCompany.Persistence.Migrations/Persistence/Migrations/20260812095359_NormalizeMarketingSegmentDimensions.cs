using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeMarketingSegmentDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_segment_dimensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_customer_segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    classification = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    numeric_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_dimensions", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_dimensions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_segment_dimensions_marketing_customer_segment_versions_company_id_marketing_customer_segment_version_id",
                        columns: x => new { x.company_id, x.marketing_customer_segment_version_id },
                        principalTable: "marketing_customer_segment_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_dimensions_company_id",
                table: "marketing_segment_dimensions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_dimensions_company_id_marketing_customer_segment_version_id_category_path",
                table: "marketing_segment_dimensions",
                columns: new[] { "company_id", "marketing_customer_segment_version_id", "category", "path" });

            migrationBuilder.Sql("""
                INSERT INTO marketing_segment_dimensions
                    (id, company_id, marketing_customer_segment_version_id, category, path, value,
                     classification, numeric_value, created_at)
                SELECT NEWID(), versions.company_id, versions.id, source.category,
                       CONCAT('$.', dimensions.[key]), LEFT(dimensions.[value], 4000), source.classification,
                       CASE WHEN dimensions.[type] = 2 THEN TRY_CONVERT(decimal(19,4), dimensions.[value]) END,
                       SYSUTCDATETIME()
                FROM marketing_customer_segment_versions AS versions
                CROSS APPLY (VALUES
                    ('criteria', versions.criteria_json, 'submitted'),
                    ('needs', versions.needs_json, 'submitted'),
                    ('behavior', versions.behaviors_json, 'submitted'),
                    ('channel_presence', versions.channels_json, 'estimated'),
                    ('price_sensitivity', versions.pricing_json, 'estimated'),
                    ('economics', versions.economics_json, 'estimated'),
                    ('scorecard', versions.scorecard_json, 'computed'),
                    ('evidence', versions.evidence_json, 'submitted')
                ) AS source(category, payload, classification)
                CROSS APPLY OPENJSON(CASE WHEN ISJSON(source.payload) = 1 THEN source.payload ELSE '{}' END) AS dimensions;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_segment_dimensions");
        }
    }
}
