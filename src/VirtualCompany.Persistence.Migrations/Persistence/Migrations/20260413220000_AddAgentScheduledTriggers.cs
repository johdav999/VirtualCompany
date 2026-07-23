using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413220000_AddAgentScheduledTriggers")]
public partial class AddAgentScheduledTriggers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var boolType = isPostgres ? "boolean" : "bit";
        var string100Type = isPostgres ? "character varying(100)" : "nvarchar(100)";
        var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
        var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var checkSql = isPostgres ? "window_end_at > window_start_at" : "[window_end_at] > [window_start_at]";

        migrationBuilder.AddUniqueConstraint(
            name: "AK_agents_company_id_id",
            table: "agents",
            columns: new[] { "CompanyId", "Id" });

        migrationBuilder.CreateTable(
            name: "agent_scheduled_triggers",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                agent_id = table.Column<Guid>(type: guidType, nullable: false),
                name = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                code = table.Column<string>(type: string100Type, maxLength: 100, nullable: false),
                cron_expression = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                timezone = table.Column<string>(type: string100Type, maxLength: 100, nullable: false),
                is_enabled = table.Column<bool>(type: boolType, nullable: false, defaultValue: true),
                next_run_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                enabled_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                last_evaluated_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                last_enqueued_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                last_run_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                disabled_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                metadata_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_scheduled_triggers", x => x.id);
                table.UniqueConstraint("AK_agent_scheduled_triggers_company_id_id", x => new { x.company_id, x.id });
                table.ForeignKey(
                    name: "FK_agent_scheduled_triggers_agents_company_id_agent_id",
                    columns: x => new { x.company_id, x.agent_id },
                    principalTable: "agents",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_agent_scheduled_triggers_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "agent_scheduled_trigger_enqueue_windows",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                scheduled_trigger_id = table.Column<Guid>(type: guidType, nullable: false),
                window_start_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                window_end_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                enqueued_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                execution_request_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_scheduled_trigger_enqueue_windows", x => x.id);
                table.CheckConstraint("CK_agent_scheduled_trigger_enqueue_windows_window_order", checkSql);
                table.ForeignKey(
                    name: "FK_agent_scheduled_trigger_enqueue_windows_agent_scheduled_triggers_company_id_scheduled_trigger_id",
                    columns: x => new { x.company_id, x.scheduled_trigger_id },
                    principalTable: "agent_scheduled_triggers",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_agent_scheduled_trigger_enqueue_windows_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_triggers_company_id_agent_id",
            table: "agent_scheduled_triggers",
            columns: new[] { "company_id", "agent_id" });
        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_triggers_company_id_agent_id_is_enabled",
            table: "agent_scheduled_triggers",
            columns: new[] { "company_id", "agent_id", "is_enabled" });
        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_triggers_company_id_code",
            table: "agent_scheduled_triggers",
            columns: new[] { "company_id", "code" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_triggers_company_id_is_enabled_next_run_at",
            table: "agent_scheduled_triggers",
            columns: new[] { "company_id", "is_enabled", "next_run_at" });
        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_triggers_agent_id",
            table: "agent_scheduled_triggers",
            column: "agent_id");

        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_trigger_enqueue_windows_company_id_enqueued_at",
            table: "agent_scheduled_trigger_enqueue_windows",
            columns: new[] { "company_id", "enqueued_at" });
        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_trigger_enqueue_windows_company_id_scheduled_trigger_id_window_start_at_window_end_at",
            table: "agent_scheduled_trigger_enqueue_windows",
            columns: new[] { "company_id", "scheduled_trigger_id", "window_start_at", "window_end_at" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_trigger_enqueue_windows_execution_request_id",
            table: "agent_scheduled_trigger_enqueue_windows",
            column: "execution_request_id",
            filter: isPostgres ? "execution_request_id IS NOT NULL" : "[execution_request_id] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_agent_scheduled_trigger_enqueue_windows_scheduled_trigger_id",
            table: "agent_scheduled_trigger_enqueue_windows",
            column: "scheduled_trigger_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "agent_scheduled_trigger_enqueue_windows");
        migrationBuilder.DropTable(name: "agent_scheduled_triggers");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_agents_company_id_id",
            table: "agents");
    }
}
