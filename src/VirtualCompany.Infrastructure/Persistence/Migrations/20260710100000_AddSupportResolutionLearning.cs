using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260710100000_AddSupportResolutionLearning")]
public partial class AddSupportResolutionLearning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("root_cause_category", "support_case_resolutions", "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: "other");
        migrationBuilder.AddColumn<string>("action_taken", "support_case_resolutions", "nvarchar(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<string>("reusable_answer", "support_case_resolutions", "nvarchar(4000)", maxLength: 4000, nullable: true);
        migrationBuilder.AddColumn<string>("customer_preference_observations", "support_case_resolutions", "nvarchar(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<string>("relevant_links_json", "support_case_resolutions", "nvarchar(4000)", maxLength: 4000, nullable: true);
        migrationBuilder.AddColumn<bool>("reuse_eligible", "support_case_resolutions", "bit", nullable: false, defaultValue: false);
        migrationBuilder.CreateTable("support_memory_update_jobs", table => new
        {
            id = table.Column<Guid>("uniqueidentifier", nullable: false), company_id = table.Column<Guid>("uniqueidentifier", nullable: false), support_case_id = table.Column<Guid>("uniqueidentifier", nullable: false), event_key = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false), status = table.Column<string>("nvarchar(40)", maxLength: 40, nullable: false), attempt_count = table.Column<int>("int", nullable: false), safe_failure_summary = table.Column<string>("nvarchar(1000)", maxLength: 1000, nullable: true), created_at = table.Column<DateTime>("datetime2", nullable: false), updated_at = table.Column<DateTime>("datetime2", nullable: false), completed_at = table.Column<DateTime>("datetime2", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_support_memory_update_jobs", x => x.id));
        migrationBuilder.CreateIndex("IX_support_memory_update_jobs_company_id_event_key", "support_memory_update_jobs", ["company_id", "event_key"], unique: true);
        migrationBuilder.CreateIndex("IX_support_memory_update_jobs_company_id_status_updated_at", "support_memory_update_jobs", ["company_id", "status", "updated_at"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("support_memory_update_jobs");
        foreach (var column in new[] { "root_cause_category", "action_taken", "reusable_answer", "customer_preference_observations", "relevant_links_json", "reuse_eligible" }) migrationBuilder.DropColumn(column, "support_case_resolutions");
    }
}
