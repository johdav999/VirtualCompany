using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddTaskLifecycle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    parent_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_actor_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_by_actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    priority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "normal"),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "new"),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    input_payload = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    output_payload = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    rationale_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    confidence_score = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_tasks_agents_assigned_agent_id",
                        column: x => x.assigned_agent_id,
                        principalTable: "agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tasks_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tasks_tasks_parent_task_id",
                        column: x => x.parent_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_assigned_agent_id",
                table: "tasks",
                columns: new[] { "company_id", "assigned_agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_due_at",
                table: "tasks",
                columns: new[] { "company_id", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_parent_task_id",
                table: "tasks",
                columns: new[] { "company_id", "parent_task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_company_id_status",
                table: "tasks",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_parent_task_id",
                table: "tasks",
                column: "parent_task_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "tasks");
        }
    }
}
