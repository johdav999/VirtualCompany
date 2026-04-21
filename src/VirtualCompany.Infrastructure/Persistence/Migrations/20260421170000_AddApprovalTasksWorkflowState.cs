using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Domain.Enums;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddApprovalTasksWorkflowState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";

            migrationBuilder.CreateTable(
                name: "approval_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    target_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: guidType, nullable: false),
                    assignee_id = table.Column<Guid>(type: guidType, nullable: true),
                    status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    due_date = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_tasks", x => x.id);
                    table.UniqueConstraint("AK_approval_tasks_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_approval_tasks_status", ApprovalTaskStatusValues.BuildCheckConstraintSql("status"));
                    table.CheckConstraint("CK_approval_tasks_target_type", ApprovalTargetTypeValues.BuildCheckConstraintSql("target_type"));
                    table.ForeignKey(
                        name: "FK_approval_tasks_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_approval_tasks_users_assignee_id",
                        column: x => x.assignee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approval_tasks_company_id",
                table: "approval_tasks",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_approval_tasks_company_id_assignee_id_status",
                table: "approval_tasks",
                columns: new[] { "company_id", "assignee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_tasks_company_id_status_due_date",
                table: "approval_tasks",
                columns: new[] { "company_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_tasks_company_id_target_type_target_id",
                table: "approval_tasks",
                columns: new[] { "company_id", "target_type", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_tasks_assignee_id",
                table: "approval_tasks",
                column: "assignee_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_tasks");
        }
    }
}