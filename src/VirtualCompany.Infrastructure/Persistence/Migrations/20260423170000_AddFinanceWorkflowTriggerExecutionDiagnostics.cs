using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceWorkflowTriggerExecutionDiagnostics : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
            var string256Type = isPostgres ? "character varying(256)" : "nvarchar(256)";
            var string4000Type = isPostgres ? "character varying(4000)" : "nvarchar(4000)";
            var textType = isPostgres ? "text" : "nvarchar(max)";
            var jsonArrayDefault = isPostgres ? "'[]'" : "N'[]'";
            var jsonObjectDefault = isPostgres ? "'{}'" : "N'{}'";

            migrationBuilder.DropIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id_source_entity_version",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.AddColumn<string>(
                name: "event_id",
                table: "finance_workflow_trigger_executions",
                type: string200Type,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "causation_id",
                table: "finance_workflow_trigger_executions",
                type: string128Type,
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trigger_message_id",
                table: "finance_workflow_trigger_executions",
                type: string64Type,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                table: "finance_workflow_trigger_executions",
                type: textType,
                nullable: false,
                defaultValueSql: jsonObjectDefault);

            migrationBuilder.AlterColumn<string>(
                name: "executed_checks",
                table: "finance_workflow_trigger_executions",
                type: textType,
                nullable: false,
                defaultValueSql: jsonArrayDefault,
                oldClrType: typeof(string),
                oldType: string4000Type,
                oldMaxLength: 4000);

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_correlation_id",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_event_id",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "trigger_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id_source_entity_version",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "trigger_type", "source_entity_id", "source_entity_version" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var string4000Type = isPostgres ? "character varying(4000)" : "nvarchar(4000)";

            migrationBuilder.DropIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_correlation_id",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.DropIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_event_id",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.DropIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.DropIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id_source_entity_version",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.DropColumn(
                name: "causation_id",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.DropColumn(
                name: "trigger_message_id",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.DropColumn(
                name: "metadata_json",
                table: "finance_workflow_trigger_executions");

            migrationBuilder.AlterColumn<string>(
                name: "executed_checks",
                table: "finance_workflow_trigger_executions",
                type: string4000Type,
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string));

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id_source_entity_version",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "trigger_type", "source_entity_id", "source_entity_version" },
                unique: true);
        }
    }
}