using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsDeploymentPrerequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "first_uat_authorized_at",
                table: "teams_tenant_registrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "first_uat_authorized_by_user_id",
                table: "teams_tenant_registrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "first_uat_expires_at",
                table: "teams_tenant_registrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "first_uat_meeting_id",
                table: "teams_tenant_registrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "first_uat_organizer_id",
                table: "teams_tenant_registrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_uat_reason",
                table: "teams_tenant_registrations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "presenter_agent_id",
                table: "teams_meeting_calls",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "presenter_agent_id",
                table: "sales_meeting_sessions",
                type: "uniqueidentifier",
                nullable: true);
            // Preserve the prior deterministic Alex selection for existing meetings only.
            // Future meetings require organizer selection; no permissions are manufactured.
            migrationBuilder.Sql("""
                EXEC(N'UPDATE meeting
                SET presenter_agent_id = chosen.Id
                FROM sales_meeting_sessions AS meeting
                CROSS APPLY (
                    SELECT TOP (1) agent.Id
                    FROM agents AS agent
                    WHERE agent.CompanyId = meeting.company_id
                      AND agent.TemplateId = N''alex'' AND agent.Department = N''Sales''
                      AND agent.Status = N''active''
                    ORDER BY agent.Id
                ) AS chosen
                WHERE meeting.presenter_agent_id IS NULL;

                UPDATE call
                SET presenter_agent_id = meeting.presenter_agent_id
                FROM teams_meeting_calls AS call
                INNER JOIN sales_meeting_sessions AS meeting
                  ON meeting.company_id = call.company_id AND meeting.id = call.meeting_session_id
                WHERE call.presenter_agent_id IS NULL;');
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "first_uat_authorized_at",
                table: "teams_tenant_registrations");

            migrationBuilder.DropColumn(
                name: "first_uat_authorized_by_user_id",
                table: "teams_tenant_registrations");

            migrationBuilder.DropColumn(
                name: "first_uat_expires_at",
                table: "teams_tenant_registrations");

            migrationBuilder.DropColumn(
                name: "first_uat_meeting_id",
                table: "teams_tenant_registrations");

            migrationBuilder.DropColumn(
                name: "first_uat_organizer_id",
                table: "teams_tenant_registrations");

            migrationBuilder.DropColumn(
                name: "first_uat_reason",
                table: "teams_tenant_registrations");

            migrationBuilder.DropColumn(
                name: "presenter_agent_id",
                table: "teams_meeting_calls");

            migrationBuilder.DropColumn(
                name: "presenter_agent_id",
                table: "sales_meeting_sessions");
        }
    }
}
