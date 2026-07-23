using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260720113000_AddWorkflowProgressionPollingIndex")]
public partial class AddWorkflowProgressionPollingIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_state_updated_at",
            table: "workflow_instances",
            columns: new[] { "state", "updated_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_workflow_instances_state_updated_at",
            table: "workflow_instances");
    }
}
