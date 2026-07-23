using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260412190000_AddWorkflowTriggerInstanceStartSupport")]
public partial class AddWorkflowTriggerInstanceStartSupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var stringType = isPostgres ? "character varying" : "nvarchar";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var boolType = isPostgres ? "boolean" : "bit";
        var boolDefault = isPostgres ? "TRUE" : "CAST(1 AS bit)";

        migrationBuilder.CreateTable(
            name: "workflow_triggers",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                definition_id = table.Column<Guid>(type: guidType, nullable: false),
                event_name = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: false),
                criteria_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                is_enabled = table.Column<bool>(type: boolType, nullable: false, defaultValueSql: boolDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workflow_triggers", x => x.id);
                table.ForeignKey(
                    name: "FK_workflow_triggers_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_workflow_triggers_workflow_definitions_definition_id",
                    column: x => x.definition_id,
                    principalTable: "workflow_definitions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "workflow_instances",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                definition_id = table.Column<Guid>(type: guidType, nullable: false),
                trigger_id = table.Column<Guid>(type: guidType, nullable: true),
                trigger_source = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false, defaultValue: "manual"),
                trigger_ref = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: true),
                status = table.Column<string>(type: $"{stringType}(32)", maxLength: 32, nullable: false, defaultValue: "started"),
                current_step = table.Column<string>(type: $"{stringType}(200)", maxLength: 200, nullable: true),
                input_payload = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                context_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                output_payload = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                started_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                completed_at = table.Column<DateTime>(type: dateTimeType, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workflow_instances", x => x.id);
                table.ForeignKey(
                    name: "FK_workflow_instances_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_workflow_instances_workflow_definitions_definition_id",
                    column: x => x.definition_id,
                    principalTable: "workflow_definitions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_workflow_instances_workflow_triggers_trigger_id",
                    column: x => x.trigger_id,
                    principalTable: "workflow_triggers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_triggers_company_id_definition_id_event_name",
            table: "workflow_triggers",
            columns: new[] { "company_id", "definition_id", "event_name" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_triggers_company_id_event_name_is_enabled",
            table: "workflow_triggers",
            columns: new[] { "company_id", "event_name", "is_enabled" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_triggers_definition_id",
            table: "workflow_triggers",
            column: "definition_id");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_definition_id_started_at",
            table: "workflow_instances",
            columns: new[] { "company_id", "definition_id", "started_at" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_definition_id_trigger_source_trigger_ref",
            table: "workflow_instances",
            columns: new[] { "company_id", "definition_id", "trigger_source", "trigger_ref" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_status",
            table: "workflow_instances",
            columns: new[] { "company_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_definition_id",
            table: "workflow_instances",
            column: "definition_id");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_trigger_id",
            table: "workflow_instances",
            column: "trigger_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "workflow_instances");
        migrationBuilder.DropTable(name: "workflow_triggers");
    }
}
