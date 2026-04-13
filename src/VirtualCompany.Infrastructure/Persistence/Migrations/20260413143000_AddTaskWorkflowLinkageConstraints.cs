using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413143000_AddTaskWorkflowLinkageConstraints")]
public partial class AddTaskWorkflowLinkageConstraints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_tasks_company_id_workflow_instance_id",
            table: "tasks",
            columns: new[] { "company_id", "workflow_instance_id" });

        migrationBuilder.CreateIndex(
            name: "IX_tasks_workflow_instance_id",
            table: "tasks",
            column: "workflow_instance_id");

        migrationBuilder.AddForeignKey(
            name: "FK_tasks_workflow_instances_workflow_instance_id",
            table: "tasks",
            column: "workflow_instance_id",
            principalTable: "workflow_instances",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_tasks_workflow_instances_workflow_instance_id",
            table: "tasks");

        migrationBuilder.DropIndex(
            name: "IX_tasks_workflow_instance_id",
            table: "tasks");

        migrationBuilder.DropIndex(
            name: "IX_tasks_company_id_workflow_instance_id",
            table: "tasks");
    }
}
