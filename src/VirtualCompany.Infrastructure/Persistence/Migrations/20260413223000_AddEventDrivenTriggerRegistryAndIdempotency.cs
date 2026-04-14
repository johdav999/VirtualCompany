using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413223000_AddEventDrivenTriggerRegistryAndIdempotency")]
public partial class AddEventDrivenTriggerRegistryAndIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var eventIdType = isPostgres ? "character varying(200)" : "nvarchar(200)";

        migrationBuilder.CreateTable(
            name: "processed_workflow_trigger_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                workflow_trigger_id = table.Column<Guid>(type: guidType, nullable: false),
                event_id = table.Column<string>(type: eventIdType, maxLength: 200, nullable: false),
                created_workflow_instance_id = table.Column<Guid>(type: guidType, nullable: true),
                processed_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_processed_workflow_trigger_events", x => x.id);
                table.ForeignKey(
                    name: "FK_processed_workflow_trigger_events_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
                table.ForeignKey(
                    name: "FK_processed_workflow_trigger_events_workflow_instances_created_workflow_instance_id",
                    column: x => x.created_workflow_instance_id,
                    principalTable: "workflow_instances",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_processed_workflow_trigger_events_workflow_triggers_workflow_trigger_id",
                    column: x => x.workflow_trigger_id,
                    principalTable: "workflow_triggers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_processed_workflow_trigger_events_company_id_processed_at",
            table: "processed_workflow_trigger_events",
            columns: new[] { "company_id", "processed_at" });
        migrationBuilder.CreateIndex(
            name: "IX_processed_workflow_trigger_events_company_id_workflow_trigger_id_event_id",
            table: "processed_workflow_trigger_events",
            columns: new[] { "company_id", "workflow_trigger_id", "event_id" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_processed_workflow_trigger_events_created_workflow_instance_id",
            table: "processed_workflow_trigger_events",
            column: "created_workflow_instance_id",
            filter: isPostgres ? "created_workflow_instance_id IS NOT NULL" : "[created_workflow_instance_id] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_processed_workflow_trigger_events_workflow_trigger_id",
            table: "processed_workflow_trigger_events",
            column: "workflow_trigger_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "processed_workflow_trigger_events");
    }
}
