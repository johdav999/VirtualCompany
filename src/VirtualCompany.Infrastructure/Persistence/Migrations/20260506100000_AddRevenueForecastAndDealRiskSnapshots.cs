using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260506100000_AddRevenueForecastAndDealRiskSnapshots")]
public partial class AddRevenueForecastAndDealRiskSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "revenue_forecast_snapshots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                as_of_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                gross_pipeline_30_days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                expected_revenue_30_days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                deal_count_30_days = table.Column<int>(type: "int", nullable: false),
                gross_pipeline_60_days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                expected_revenue_60_days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                deal_count_60_days = table.Column<int>(type: "int", nullable: false),
                gross_pipeline_90_days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                expected_revenue_90_days = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                deal_count_90_days = table.Column<int>(type: "int", nullable: false),
                unknown_risk_deals = table.Column<int>(type: "int", nullable: false),
                low_risk_deals = table.Column<int>(type: "int", nullable: false),
                medium_risk_deals = table.Column<int>(type: "int", nullable: false),
                high_risk_deals = table.Column<int>(type: "int", nullable: false),
                calculated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_revenue_forecast_snapshots", x => x.id);
                table.UniqueConstraint("AK_revenue_forecast_snapshots_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_revenue_forecast_snapshots_deal_counts_nonnegative", "deal_count_30_days >= 0 AND deal_count_60_days >= 0 AND deal_count_90_days >= 0");
                table.CheckConstraint("CK_revenue_forecast_snapshots_risk_counts_nonnegative", "unknown_risk_deals >= 0 AND low_risk_deals >= 0 AND medium_risk_deals >= 0 AND high_risk_deals >= 0");
                table.ForeignKey(
                    name: "FK_revenue_forecast_snapshots_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "deal_risk_score_snapshots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                score_date = table.Column<DateTime>(type: "datetime2", nullable: false),
                score = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                band = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                factors_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                calculated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deal_risk_score_snapshots", x => x.id);
                table.UniqueConstraint("AK_deal_risk_score_snapshots_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_deal_risk_score_snapshots_score_range", "score >= 0 AND score <= 1");
                table.ForeignKey(
                    name: "FK_deal_risk_score_snapshots_deals_company_id_deal_id",
                    columns: x => new { x.company_id, x.deal_id },
                    principalTable: "deals",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_deal_risk_score_snapshots_company_id_band_score_date",
            table: "deal_risk_score_snapshots",
            columns: new[] { "company_id", "band", "score_date" });

        migrationBuilder.CreateIndex(
            name: "IX_deal_risk_score_snapshots_company_id_calculated_at",
            table: "deal_risk_score_snapshots",
            columns: new[] { "company_id", "calculated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_deal_risk_score_snapshots_company_id_deal_id",
            table: "deal_risk_score_snapshots",
            columns: new[] { "company_id", "deal_id" });

        migrationBuilder.CreateIndex(
            name: "IX_deal_risk_score_snapshots_company_id_deal_id_score_date",
            table: "deal_risk_score_snapshots",
            columns: new[] { "company_id", "deal_id", "score_date" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_revenue_forecast_snapshots_company_id_as_of_at",
            table: "revenue_forecast_snapshots",
            columns: new[] { "company_id", "as_of_at" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_revenue_forecast_snapshots_company_id_calculated_at",
            table: "revenue_forecast_snapshots",
            columns: new[] { "company_id", "calculated_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "deal_risk_score_snapshots");
        migrationBuilder.DropTable(name: "revenue_forecast_snapshots");
    }
}
