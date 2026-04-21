using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddApprovalTaskInboxIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_approval_tasks_due_date",
                table: "approval_tasks",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "IX_approval_tasks_status",
                table: "approval_tasks",
                column: "status");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_tasks_due_date",
                table: "approval_tasks");
            migrationBuilder.DropIndex(
                name: "IX_approval_tasks_status",
                table: "approval_tasks");
        }
    }
}