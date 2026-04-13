using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddApprovalTargetChains : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_approval_requests_ToolExecutionAttemptId",
            table: "approval_requests");

        migrationBuilder.AlterColumn<Guid>(
            name: "ToolExecutionAttemptId",
            table: "approval_requests",
            type: "uniqueidentifier",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier");

        migrationBuilder.AddColumn<string>(
            name: "entity_type",
            table: "approval_requests",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "action");

        migrationBuilder.AddColumn<Guid>(
            name: "entity_id",
            table: "approval_requests",
            type: "uniqueidentifier",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.Sql("""
            UPDATE approval_requests
            SET entity_id = ToolExecutionAttemptId
            WHERE entity_id = '00000000-0000-0000-0000-000000000000' AND ToolExecutionAttemptId IS NOT NULL
            """);

        migrationBuilder.AddColumn<string>(
            name: "requested_by_actor_type",
            table: "approval_requests",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "user");

        migrationBuilder.AddColumn<Guid>(
            name: "requested_by_actor_id",
            table: "approval_requests",
            type: "uniqueidentifier",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.Sql("""
            UPDATE approval_requests
            SET requested_by_actor_id = RequestedByUserId
            WHERE requested_by_actor_id = '00000000-0000-0000-0000-000000000000'
            """);

        migrationBuilder.AddColumn<string>(
            name: "approval_type",
            table: "approval_requests",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "threshold");

        migrationBuilder.AddColumn<string>(
            name: "required_role",
            table: "approval_requests",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "required_user_id",
            table: "approval_requests",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "decision_summary",
            table: "approval_requests",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "decided_at",
            table: "approval_requests",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "approval_steps",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sequence_no = table.Column<int>(type: "int", nullable: false),
                approver_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                approver_ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                decided_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_approval_steps", x => x.Id);
                table.ForeignKey(
                    name: "FK_approval_steps_approval_requests_ApprovalId",
                    column: x => x.ApprovalId,
                    principalTable: "approval_requests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            INSERT INTO approval_steps (Id, ApprovalId, sequence_no, approver_type, approver_ref, Status)
            SELECT NEWID(), Id, 1, 'role', COALESCE(NULLIF(ApprovalTarget, ''), 'finance_approver'), Status
            FROM approval_requests
            WHERE NOT EXISTS (
                SELECT 1
                FROM approval_steps
                WHERE approval_steps.ApprovalId = approval_requests.Id
            )
            AND Status IN ('pending', 'approved', 'rejected')
            """);

        migrationBuilder.CreateIndex(
            name: "IX_approval_requests_ToolExecutionAttemptId",
            table: "approval_requests",
            column: "ToolExecutionAttemptId",
            unique: true,
            filter: "[ToolExecutionAttemptId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_approval_requests_CompanyId_TargetEntity",
            table: "approval_requests",
            columns: new[] { "CompanyId", "entity_type", "entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_approval_steps_ApprovalId_sequence_no",
            table: "approval_steps",
            columns: new[] { "ApprovalId", "sequence_no" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_approval_steps_Status",
            table: "approval_steps",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_approval_steps_ApprovalId_Status_sequence_no",
            table: "approval_steps",
            columns: new[] { "ApprovalId", "Status", "sequence_no" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "approval_steps");
        migrationBuilder.DropIndex(name: "IX_approval_requests_CompanyId_TargetEntity", table: "approval_requests");
        migrationBuilder.DropIndex(name: "IX_approval_requests_ToolExecutionAttemptId", table: "approval_requests");
        migrationBuilder.DropColumn(name: "entity_type", table: "approval_requests");
        migrationBuilder.DropColumn(name: "entity_id", table: "approval_requests");
        migrationBuilder.DropColumn(name: "requested_by_actor_type", table: "approval_requests");
        migrationBuilder.DropColumn(name: "requested_by_actor_id", table: "approval_requests");
        migrationBuilder.DropColumn(name: "approval_type", table: "approval_requests");
        migrationBuilder.DropColumn(name: "required_role", table: "approval_requests");
        migrationBuilder.DropColumn(name: "required_user_id", table: "approval_requests");
        migrationBuilder.DropColumn(name: "decision_summary", table: "approval_requests");
        migrationBuilder.DropColumn(name: "decided_at", table: "approval_requests");
        migrationBuilder.AlterColumn<Guid>(name: "ToolExecutionAttemptId", table: "approval_requests", type: "uniqueidentifier", nullable: false, defaultValue: Guid.Empty, oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);
        migrationBuilder.DropIndex(name: "IX_approval_steps_ApprovalId_Status_sequence_no", table: "approval_steps");
        migrationBuilder.CreateIndex(name: "IX_approval_requests_ToolExecutionAttemptId", table: "approval_requests", column: "ToolExecutionAttemptId", unique: true);
    }
}