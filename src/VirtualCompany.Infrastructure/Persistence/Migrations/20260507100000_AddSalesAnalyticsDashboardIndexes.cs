using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260507100000_AddSalesAnalyticsDashboardIndexes")]

public partial class AddSalesAnalyticsDashboardIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_created_at",
            table: "sales_message_performances",
            columns: new[] { "company_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_deal_intelligence_signals_company_id_created_at",
            table: "deal_intelligence_signals",
            columns: new[] { "company_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_deal_intelligence_signals_company_id_deal_id_detected_at",
            table: "deal_intelligence_signals",
            columns: new[] { "company_id", "deal_id", "detected_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_sales_message_performances_company_id_created_at", table: "sales_message_performances");
        migrationBuilder.DropIndex(name: "IX_deal_intelligence_signals_company_id_created_at", table: "deal_intelligence_signals");
        migrationBuilder.DropIndex(name: "IX_deal_intelligence_signals_company_id_deal_id_detected_at", table: "deal_intelligence_signals");
    }
}
