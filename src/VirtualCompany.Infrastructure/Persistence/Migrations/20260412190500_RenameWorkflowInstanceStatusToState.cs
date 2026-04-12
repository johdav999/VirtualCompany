using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260412190500_RenameWorkflowInstanceStatusToState")]
public partial class RenameWorkflowInstanceStatusToState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_workflow_instances_company_id_status",
            table: "workflow_instances");

        migrationBuilder.RenameColumn(
            name: "status",
            table: "workflow_instances",
            newName: "state");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_state",
            table: "workflow_instances",
            columns: new[] { "company_id", "state" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_workflow_instances_company_id_state",
            table: "workflow_instances");

        migrationBuilder.RenameColumn(
            name: "state",
            table: "workflow_instances",
            newName: "status");

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_status",
            table: "workflow_instances",
            columns: new[] { "company_id", "status" });
    }
}
