using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260412214500_AddWorkflowSchedulerOccurrenceDedupe")]
public partial class AddWorkflowSchedulerOccurrenceDedupe : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_workflow_instances_company_id_definition_id_trigger_source_trigger_ref",
            table: "workflow_instances");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_definition_id_trigger_source_trigger_ref",
            table: "workflow_instances",
            columns: new[] { "company_id", "definition_id", "trigger_source", "trigger_ref" },
            unique: true,
            filter: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"
                ? "trigger_ref IS NOT NULL"
                : "[trigger_ref] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_workflow_instances_company_id_definition_id_trigger_source_trigger_ref",
            table: "workflow_instances");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_definition_id_trigger_source_trigger_ref",
            table: "workflow_instances",
            columns: new[] { "company_id", "definition_id", "trigger_source", "trigger_ref" });
    }
}
