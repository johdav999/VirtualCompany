using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260415193000_AddActivityFeedFilterIndexes")]
public partial class AddActivityFeedFilterIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var string100Type = isPostgres ? "character varying(100)" : "nvarchar(100)";

        migrationBuilder.AddColumn<string>(
            name: "department",
            table: "activity_events",
            type: string100Type,
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "task_id",
            table: "activity_events",
            type: guidType,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "audit_event_id",
            table: "activity_events",
            type: guidType,
            nullable: true);

        if (isPostgres)
        {
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_department_occurred_at_id"
                ON "activity_events" ("company_id", "department", "occurred_at" DESC, "id" DESC);
                """);
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_task_id_occurred_at_id"
                ON "activity_events" ("company_id", "task_id", "occurred_at" DESC, "id" DESC);
                """);
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_event_type_occurred_at_id"
                ON "activity_events" ("company_id", "event_type", "occurred_at" DESC, "id" DESC);
                """);
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_status_occurred_at_id"
                ON "activity_events" ("company_id", "status", "occurred_at" DESC, "id" DESC);
                """);
        }
        else
        {
            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_department_occurred_at_id",
                table: "activity_events",
                columns: new[] { "company_id", "department", "occurred_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_task_id_occurred_at_id",
                table: "activity_events",
                columns: new[] { "company_id", "task_id", "occurred_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_event_type_occurred_at_id",
                table: "activity_events",
                columns: new[] { "company_id", "event_type", "occurred_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_status_occurred_at_id",
                table: "activity_events",
                columns: new[] { "company_id", "status", "occurred_at", "id" },
                descending: new[] { false, false, true, true });
        }

        migrationBuilder.CreateIndex(
            name: "IX_activity_events_company_id_audit_event_id",
            table: "activity_events",
            columns: new[] { "company_id", "audit_event_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_activity_events_company_id_audit_event_id",
            table: "activity_events");

        migrationBuilder.DropIndex(
            name: "IX_activity_events_company_id_department_occurred_at_id",
            table: "activity_events");

        migrationBuilder.DropIndex(
            name: "IX_activity_events_company_id_task_id_occurred_at_id",
            table: "activity_events");

        migrationBuilder.DropIndex(
            name: "IX_activity_events_company_id_event_type_occurred_at_id",
            table: "activity_events");

        migrationBuilder.DropIndex(
            name: "IX_activity_events_company_id_status_occurred_at_id",
            table: "activity_events");

        migrationBuilder.DropColumn(
            name: "audit_event_id",
            table: "activity_events");

        migrationBuilder.DropColumn(
            name: "task_id",
            table: "activity_events");

        migrationBuilder.DropColumn(
            name: "department",
            table: "activity_events");
    }
}