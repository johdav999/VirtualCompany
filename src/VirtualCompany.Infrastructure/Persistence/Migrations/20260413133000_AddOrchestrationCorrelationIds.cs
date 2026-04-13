using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddOrchestrationCorrelationIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "correlation_id",
            table: "tasks",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_tasks_company_id_correlation_id",
            table: "tasks",
            columns: new[] { "company_id", "correlation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_CorrelationId",
            table: "audit_events",
            columns: new[] { "CompanyId", "CorrelationId" });

        migrationBuilder.CreateIndex(
            name: "IX_context_retrievals_CompanyId_CorrelationId",
            table: "context_retrievals",
            columns: new[] { "CompanyId", "CorrelationId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_context_retrievals_CompanyId_CorrelationId",
            table: "context_retrievals");

        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_CorrelationId",
            table: "audit_events");

        migrationBuilder.DropIndex(
            name: "IX_tasks_company_id_correlation_id",
            table: "tasks");

        migrationBuilder.DropColumn(
            name: "correlation_id",
            table: "tasks");
    }
}
