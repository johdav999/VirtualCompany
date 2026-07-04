using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260415010000_AddBriefingUpdateJobRetryFailureMetadata")]

public partial class AddBriefingUpdateJobRetryFailureMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var isSqlite = ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite";
        var intType = isPostgres ? "integer" : isSqlite ? "INTEGER" : "int";
        var dateTimeType = isPostgres ? "timestamp with time zone" : isSqlite ? "TEXT" : "datetime2";
        var string256Type = isPostgres ? "character varying(256)" : isSqlite ? "TEXT" : "nvarchar(256)";
        var string12000Type = isPostgres ? "text" : isSqlite ? "TEXT" : "nvarchar(max)";

        migrationBuilder.AddColumn<int>(
            name: "max_attempts",
            table: "company_briefing_update_jobs",
            type: intType,
            nullable: false,
            defaultValue: 5);

        migrationBuilder.AddColumn<string>(
            name: "last_error_code",
            table: "company_briefing_update_jobs",
            type: string256Type,
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "last_error_details",
            table: "company_briefing_update_jobs",
            type: string12000Type,
            maxLength: 12000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "last_failure_at",
            table: "company_briefing_update_jobs",
            type: dateTimeType,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "started_at",
            table: "company_briefing_update_jobs",
            type: dateTimeType,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "completed_at",
            table: "company_briefing_update_jobs",
            type: dateTimeType,
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
