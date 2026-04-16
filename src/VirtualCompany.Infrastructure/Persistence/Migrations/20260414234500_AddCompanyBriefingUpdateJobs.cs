using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddCompanyBriefingUpdateJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "company_briefing_update_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "TEXT", nullable: false),
                company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                trigger_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                briefing_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                event_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                correlation_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                attempt_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                next_attempt_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                last_error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                final_failed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                source_metadata_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'{}'")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_company_briefing_update_jobs", x => x.id);
                table.ForeignKey(
                    name: "fk_company_briefing_update_jobs_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "id",
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