using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceWorkflowTriggerCheckExecutions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
            var string256Type = isPostgres ? "character varying(256)" : "nvarchar(256)";
            var string4000Type = isPostgres ? "character varying(4000)" : "nvarchar(4000)";
            var textType = isPostgres ? "text" : "nvarchar(max)";
            var jsonObjectDefault = isPostgres ? "'{}'" : "N'{}'";

            migrationBuilder.CreateTable(
                name: "finance_workflow_trigger_check_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    trigger_execution_id = table.Column<Guid>(type: guidType, nullable: false),
                    trigger_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    source_entity_type = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    source_entity_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    source_entity_version = table.Column<string>(type: string256Type, maxLength: 256, nullable: false),
                    check_type = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                    event_id = table.Column<string>(type: string200Type, maxLength: 200, nullable: true),
                    causation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                    trigger_message_id = table.Column<string>(type: string64Type, maxLength: 64, nullable: true),
                    started_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    completed_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    outcome = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    metadata_json = table.Column<string>(type: textType, nullable: false, defaultValueSql: jsonObjectDefault),
                    error_details = table.Column<string>(type: string4000Type, maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_workflow_trigger_check_executions", x => x.id);
                    table.UniqueConstraint("AK_finance_workflow_trigger_check_executions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_workflow_trigger_check_executions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_workflow_trigger_check_executions_finance_workflow_trigger_executions_trigger_execution_id",
                        column: x => x.trigger_execution_id,
                        principalTable: "finance_workflow_trigger_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_check_executions_company_id",
                table: "finance_workflow_trigger_check_executions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_check_executions_company_id_correlation_id",
                table: "finance_workflow_trigger_check_executions",
                columns: new[] { "company_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_check_executions_company_id_event_id",
                table: "finance_workflow_trigger_check_executions",
                columns: new[] { "company_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_check_exec_dedupe",
                table: "finance_workflow_trigger_check_executions",
                columns: new[] { "company_id", "trigger_type", "source_entity_type", "source_entity_id", "source_entity_version", "check_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_check_executions_trigger_execution_id",
                table: "finance_workflow_trigger_check_executions",
                column: "trigger_execution_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_workflow_trigger_check_executions");
        }
    }
}
