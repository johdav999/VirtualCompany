using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260415043100_AddBriefingStructuredPriority")]
public partial class AddBriefingStructuredPriority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
        var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
        var string100Type = isPostgres ? "character varying(100)" : "nvarchar(100)";

        migrationBuilder.AddColumn<string>(
            name: "section_type",
            table: "company_briefing_sections",
            type: string64Type,
            maxLength: 64,
            nullable: false,
            defaultValue: "informational");

        migrationBuilder.AddColumn<string>(
            name: "priority_category",
            table: "company_briefing_sections",
            type: string32Type,
            maxLength: 32,
            nullable: false,
            defaultValue: "informational");

        migrationBuilder.AddColumn<int>(
            name: "priority_score",
            table: "company_briefing_sections",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "priority_rule_code",
            table: "company_briefing_sections",
            type: string100Type,
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "company_briefing_severity_rules",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                rule_code = table.Column<string>(type: string100Type, maxLength: 100, nullable: false),
                section_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                entity_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                condition_key = table.Column<string>(type: string100Type, maxLength: 100, nullable: false),
                condition_value = table.Column<string>(type: string100Type, maxLength: 100, nullable: false),
                priority_category = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                priority_score = table.Column<int>(nullable: false),
                sort_order = table.Column<int>(nullable: false, defaultValue: 0),
                status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false, defaultValue: "active"),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_briefing_severity_rules", x => x.id);
                table.ForeignKey("FK_company_briefing_severity_rules_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_company_briefing_sections_company_id_priority_score_section_key",
            table: "company_briefing_sections",
            columns: new[] { "company_id", "priority_score", "section_key" });

        migrationBuilder.CreateIndex(
            name: "IX_company_briefing_severity_rules_company_id_rule_code",
            table: "company_briefing_severity_rules",
            columns: new[] { "company_id", "rule_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_company_briefing_severity_rules_match",
            table: "company_briefing_severity_rules",
            columns: new[] { "company_id", "status", "section_type", "entity_type", "condition_key", "condition_value" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "company_briefing_severity_rules");

        migrationBuilder.DropIndex(
            name: "IX_company_briefing_sections_company_id_priority_score_section_key",
            table: "company_briefing_sections");

        migrationBuilder.DropColumn(name: "section_type", table: "company_briefing_sections");
        migrationBuilder.DropColumn(name: "priority_category", table: "company_briefing_sections");
        migrationBuilder.DropColumn(name: "priority_score", table: "company_briefing_sections");
        migrationBuilder.DropColumn(name: "priority_rule_code", table: "company_briefing_sections");
    }
}