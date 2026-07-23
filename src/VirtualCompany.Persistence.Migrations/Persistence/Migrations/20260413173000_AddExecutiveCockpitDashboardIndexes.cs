using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413173000_AddExecutiveCockpitDashboardIndexes")]
public partial class AddExecutiveCockpitDashboardIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_agents_company_id_department_status",
            table: "agents",
            columns: new[] { "CompanyId", "Department", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_requests_company_id_status_agent_id_CreatedUtc",
            table: "approval_requests",
            columns: new[] { "CompanyId", "Status", "AgentId", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_tasks_company_id_assigned_agent_id_status_completed_at",
            table: "tasks",
            columns: new[] { "company_id", "assigned_agent_id", "status", "completed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_tasks_company_id_status_updated_at",
            table: "tasks",
            columns: new[] { "company_id", "status", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_tasks_company_id_updated_at",
            table: "tasks",
            columns: new[] { "company_id", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_definitions_company_id_active_department",
            table: "workflow_definitions",
            columns: new[] { "company_id", "active", "department" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_state_updated_at",
            table: "workflow_instances",
            columns: new[] { "company_id", "state", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_workflow_instances_company_id_updated_at",
            table: "workflow_instances",
            columns: new[] { "company_id", "updated_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_workflow_instances_company_id_updated_at",
            table: "workflow_instances");

        migrationBuilder.DropIndex(
            name: "IX_workflow_instances_company_id_state_updated_at",
            table: "workflow_instances");

        migrationBuilder.DropIndex(
            name: "IX_workflow_definitions_company_id_active_department",
            table: "workflow_definitions");

        migrationBuilder.DropIndex(
            name: "IX_tasks_company_id_updated_at",
            table: "tasks");

        migrationBuilder.DropIndex(
            name: "IX_tasks_company_id_status_updated_at",
            table: "tasks");

        migrationBuilder.DropIndex(
            name: "IX_tasks_company_id_assigned_agent_id_status_completed_at",
            table: "tasks");

        migrationBuilder.DropIndex(
            name: "IX_approval_requests_company_id_status_agent_id_CreatedUtc",
            table: "approval_requests");

        migrationBuilder.DropIndex(
            name: "IX_agents_company_id_department_status",
            table: "agents");
    }
}
