using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260415183000_AddActivityCorrelationLookupIndex")]
public partial class AddActivityCorrelationLookupIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";

        migrationBuilder.DropIndex(
            name: "IX_activity_events_company_id_correlation_id",
            table: "activity_events");

        if (isPostgres)
        {
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_correlation_id_occurred_at_id"
                ON "activity_events" ("company_id", "correlation_id", "occurred_at", "id");
                """);
        }
        else
        {
            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_correlation_id_occurred_at_id",
                table: "activity_events",
                columns: new[] { "company_id", "correlation_id", "occurred_at", "id" });
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";

        migrationBuilder.DropIndex(
            name: "IX_activity_events_company_id_correlation_id_occurred_at_id",
            table: "activity_events");

        if (isPostgres)
        {
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_correlation_id"
                ON "activity_events" ("company_id", "correlation_id");
                """);
        }
        else
        {
            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_correlation_id",
                table: "activity_events",
                columns: new[] { "company_id", "correlation_id" });
        }
    }
}
