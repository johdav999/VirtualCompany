using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260423153000_AddFinanceWorkflowTriggerExecutions")]
    public partial class AddFinanceWorkflowTriggerExecutions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string256Type = isPostgres ? "character varying(256)" : "nvarchar(256)";
            var string4000Type = isPostgres ? "character varying(4000)" : "nvarchar(4000)";

            migrationBuilder.CreateTable(
                name: "finance_workflow_trigger_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    trigger_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    source_entity_type = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    source_entity_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    source_entity_version = table.Column<string>(type: string256Type, maxLength: 256, nullable: false),
                    correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                    occurred_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    started_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    completed_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    executed_checks = table.Column<string>(type: string4000Type, maxLength: 4000, nullable: false),
                    outcome = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    error_details = table.Column<string>(type: string4000Type, maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_workflow_trigger_executions", x => x.id);
                    table.UniqueConstraint("AK_finance_workflow_trigger_executions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_workflow_trigger_executions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id",
                table: "finance_workflow_trigger_executions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_source_entity_type_source_entity_id",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "source_entity_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_occurred_at",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "trigger_type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id_source_entity_version",
                table: "finance_workflow_trigger_executions",
                columns: new[] { "company_id", "trigger_type", "source_entity_id", "source_entity_version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_workflow_trigger_check_executions_finance_workflow_trigger_executions_trigger_execution_id",
                table: "finance_workflow_trigger_check_executions",
                column: "trigger_execution_id",
                principalTable: "finance_workflow_trigger_executions",
                principalColumn: "id",
                onDelete: ReferentialAction.NoAction);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_finance_workflow_trigger_check_executions_finance_workflow_trigger_executions_trigger_execution_id",
                table: "finance_workflow_trigger_check_executions");

            migrationBuilder.DropTable(
                name: "finance_workflow_trigger_executions");
        }
    }
}
