using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddBriefingUpdateJobRetryFailureMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "max_attempts",
            table: "company_briefing_update_jobs",
            type: "INTEGER",
            nullable: false,
            defaultValue: 5);

        migrationBuilder.AddColumn<string>(
            name: "last_error_code",
            table: "company_briefing_update_jobs",
            type: "TEXT",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "last_error_details",
            table: "company_briefing_update_jobs",
            type: "TEXT",
            maxLength: 12000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "last_failure_at",
            table: "company_briefing_update_jobs",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "started_at",
            table: "company_briefing_update_jobs",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "completed_at",
            table: "company_briefing_update_jobs",
            type: "TEXT",
            nullable: true);

        migrationBuilder.DropIndex(
            name: "ix_company_briefing_update_jobs_status_next_attempt_at_created_at",
            table: "company_briefing_update_jobs");

        migrationBuilder.CreateIndex(
            name: "ix_company_briefing_update_jobs_status_next_attempt_at_started_at_created_at",
            table: "company_briefing_update_jobs",
            columns: new[] { "status", "next_attempt_at", "started_at", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_company_briefing_update_jobs_status_next_attempt_at_started_at_created_at",
            table: "company_briefing_update_jobs");

        migrationBuilder.CreateIndex(
            name: "ix_company_briefing_update_jobs_status_next_attempt_at_created_at",
            table: "company_briefing_update_jobs",
            columns: new[] { "status", "next_attempt_at", "created_at" });

        migrationBuilder.DropColumn(
            name: "completed_at",
            table: "company_briefing_update_jobs");

        migrationBuilder.DropColumn(
            name: "last_error_code",
            table: "company_briefing_update_jobs");

        migrationBuilder.DropColumn(
            name: "last_error_details",
            table: "company_briefing_update_jobs");

        migrationBuilder.DropColumn(
            name: "last_failure_at",
            table: "company_briefing_update_jobs");

        migrationBuilder.DropColumn(
            name: "max_attempts",
            table: "company_briefing_update_jobs");

        migrationBuilder.DropColumn(
            name: "started_at",
            table: "company_briefing_update_jobs");
    }
}