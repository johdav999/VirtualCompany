using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260414200000_AddAgentGenerationAuditMetadata")]
public partial class AddAgentGenerationAuditMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "agent_name",
            table: "audit_events",
            type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(200)" : "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "agent_role",
            table: "audit_events",
            type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(128)" : "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "responsibility_domain",
            table: "audit_events",
            type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(128)" : "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "prompt_profile_version",
            table: "audit_events",
            type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(64)" : "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "boundary_decision_outcome",
            table: "audit_events",
            type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(64)" : "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "identity_reason_code",
            table: "audit_events",
            type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(128)" : "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "boundary_reason_code",
            table: "audit_events",
            type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(128)" : "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_RelatedAgentId_BoundaryDecisionOutcome_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "RelatedAgentId", "boundary_decision_outcome", "OccurredUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_audit_events_CompanyId_RelatedAgentId_BoundaryDecisionOutcome_OccurredUtc",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "agent_name",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "agent_role",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "responsibility_domain",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "prompt_profile_version",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "boundary_decision_outcome",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "identity_reason_code",
            table: "audit_events");

        migrationBuilder.DropColumn(
            name: "boundary_reason_code",
            table: "audit_events");
    }
}