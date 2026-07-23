using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260414234500_AddCompanyBriefingUpdateJobs")]

public partial class AddCompanyBriefingUpdateJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var isSqlite = ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite";
        var guidType = isPostgres ? "uuid" : isSqlite ? "TEXT" : "uniqueidentifier";
        var intType = isPostgres ? "integer" : isSqlite ? "INTEGER" : "int";
        var dateTimeType = isPostgres ? "timestamp with time zone" : isSqlite ? "TEXT" : "datetime2";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : isSqlite ? "'{}'" : "N'{}'";
        var textType = isPostgres ? "text" : isSqlite ? "TEXT" : "nvarchar(max)";
        var string32Type = isPostgres ? "character varying(32)" : isSqlite ? "TEXT" : "nvarchar(32)";
        var string100Type = isPostgres ? "character varying(100)" : isSqlite ? "TEXT" : "nvarchar(100)";
        var string128Type = isPostgres ? "character varying(128)" : isSqlite ? "TEXT" : "nvarchar(128)";
        var string300Type = isPostgres ? "character varying(300)" : isSqlite ? "TEXT" : "nvarchar(300)";
        var string4000Type = isPostgres ? "character varying(4000)" : isSqlite ? "TEXT" : "nvarchar(4000)";
        var companyPrincipalColumn = isSqlite ? "id" : "Id";

        migrationBuilder.CreateTable(
            name: "company_briefing_update_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                trigger_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                briefing_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: true),
                event_type = table.Column<string>(type: string100Type, maxLength: 100, nullable: true),
                correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                idempotency_key = table.Column<string>(type: string300Type, maxLength: 300, nullable: false),
                status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                attempt_count = table.Column<int>(type: intType, nullable: false, defaultValue: 0),
                next_attempt_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                last_error = table.Column<string>(type: string4000Type, maxLength: 4000, nullable: true),
                final_failed_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                source_metadata_json = table.Column<string>(type: textType, nullable: false, defaultValueSql: jsonDefault)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_company_briefing_update_jobs", x => x.id);
                table.ForeignKey(
                    name: "fk_company_briefing_update_jobs_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: companyPrincipalColumn,
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_company_briefing_update_jobs_company_id_event_type_created_at",
            table: "company_briefing_update_jobs",
            columns: new[] { "company_id", "event_type", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_company_briefing_update_jobs_company_id_idempotency_key",
            table: "company_briefing_update_jobs",
            columns: new[] { "company_id", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_company_briefing_update_jobs_company_id_status_created_at",
            table: "company_briefing_update_jobs",
            columns: new[] { "company_id", "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_company_briefing_update_jobs_status_next_attempt_at_created_at",
            table: "company_briefing_update_jobs",
            columns: new[] { "status", "next_attempt_at", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "company_briefing_update_jobs");
    }
}
