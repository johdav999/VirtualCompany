using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413233000_AddConditionTriggerEvaluations")]
public partial class AddConditionTriggerEvaluations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var boolType = isPostgres ? "boolean" : "bit";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";

        migrationBuilder.CreateTable(
            name: "condition_trigger_evaluations",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                condition_definition_id = table.Column<string>(type: isPostgres ? "character varying(200)" : "nvarchar(200)", maxLength: 200, nullable: false),
                workflow_trigger_id = table.Column<Guid>(type: guidType, nullable: true),
                evaluated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                source_type = table.Column<string>(type: isPostgres ? "character varying(32)" : "nvarchar(32)", maxLength: 32, nullable: false),
                source_name = table.Column<string>(type: isPostgres ? "character varying(200)" : "nvarchar(200)", maxLength: 200, nullable: true),
                entity_type = table.Column<string>(type: isPostgres ? "character varying(100)" : "nvarchar(100)", maxLength: 100, nullable: true),
                field_path = table.Column<string>(type: isPostgres ? "character varying(200)" : "nvarchar(200)", maxLength: 200, nullable: true),
                @operator = table.Column<string>(name: "operator", type: isPostgres ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: false),
                value_type = table.Column<string>(type: isPostgres ? "character varying(32)" : "nvarchar(32)", maxLength: 32, nullable: true),
                repeat_firing_mode = table.Column<string>(type: isPostgres ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "falseToTrueTransition"),
                input_values_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: isPostgres ? "'{}'::jsonb" : "N'{}'"),
                previous_outcome = table.Column<bool>(type: boolType, nullable: true),
                current_outcome = table.Column<bool>(type: boolType, nullable: false),
                fired = table.Column<bool>(type: boolType, nullable: false),
                diagnostic = table.Column<string>(type: isPostgres ? "character varying(2000)" : "nvarchar(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_condition_trigger_evaluations", x => x.id);
                table.ForeignKey(
                    name: "FK_condition_trigger_evaluations_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
                table.ForeignKey(
                    name: "FK_condition_trigger_evaluations_workflow_triggers_workflow_trigger_id",
                    column: x => x.workflow_trigger_id,
                    principalTable: "workflow_triggers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_condition_trigger_evaluations_company_id_workflow_trigger_id_condition_definition_id_evaluated_at",
            table: "condition_trigger_evaluations",
            columns: new[] { "company_id", "workflow_trigger_id", "condition_definition_id", "evaluated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_condition_trigger_evaluations_company_id_fired_evaluated_at",
            table: "condition_trigger_evaluations",
            columns: new[] { "company_id", "fired", "evaluated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_condition_trigger_evaluations_company_id_workflow_trigger_id_evaluated_at",
            table: "condition_trigger_evaluations",
            columns: new[] { "company_id", "workflow_trigger_id", "evaluated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_condition_trigger_evaluations_workflow_trigger_id",
            table: "condition_trigger_evaluations",
            column: "workflow_trigger_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "condition_trigger_evaluations");
    }
}
