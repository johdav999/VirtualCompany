using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260415120000_AddActivityEvents")]
public partial class AddActivityEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
        var string100Type = isPostgres ? "character varying(100)" : "nvarchar(100)";
        var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
        var string500Type = isPostgres ? "character varying(500)" : "nvarchar(500)";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonObjectDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";

        migrationBuilder.CreateTable(
            name: "activity_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                agent_id = table.Column<Guid>(type: guidType, nullable: true),
                event_type = table.Column<string>(type: string100Type, maxLength: 100, nullable: false),
                occurred_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                status = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                summary = table.Column<string>(type: string500Type, maxLength: 500, nullable: false),
                correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                source_metadata_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonObjectDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_activity_events", x => x.id);
                table.ForeignKey(
                    "FK_activity_events_companies_company_id",
                    x => x.company_id,
                    "companies",
                    "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    "FK_activity_events_agents_company_id_agent_id",
                    x => x.agent_id,
                    "agents",
                    "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        if (isPostgres)
        {
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_occurred_at_id"
                ON "activity_events" ("company_id", "occurred_at" DESC, "id" DESC);
                """);
            migrationBuilder.Sql("""
                CREATE INDEX "IX_activity_events_company_id_agent_id_occurred_at_id"
                ON "activity_events" ("company_id", "agent_id", "occurred_at" DESC, "id" DESC);
                """);
        }
        else
        {
            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_occurred_at_id",
                table: "activity_events",
                columns: new[] { "company_id", "occurred_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_activity_events_company_id_agent_id_occurred_at_id",
                table: "activity_events",
                columns: new[] { "company_id", "agent_id", "occurred_at", "id" },
                descending: new[] { false, false, true, true });
        }

        migrationBuilder.CreateIndex(
            name: "IX_activity_events_company_id_correlation_id",
            table: "activity_events",
            columns: new[] { "company_id", "correlation_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "activity_events");
    }
}
