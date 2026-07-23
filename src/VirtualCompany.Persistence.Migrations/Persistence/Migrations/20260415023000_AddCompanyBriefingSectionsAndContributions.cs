using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260415023000_AddCompanyBriefingSectionsAndContributions")]
public partial class AddCompanyBriefingSectionsAndContributions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var boolType = isPostgres ? "boolean" : "bit";
        var decimalType = isPostgres ? "numeric(5,4)" : "decimal(5,4)";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
        var string100Type = isPostgres ? "character varying(100)" : "nvarchar(100)";
        var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
        var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
        var string256Type = isPostgres ? "character varying(256)" : "nvarchar(256)";
        var string300Type = isPostgres ? "character varying(300)" : "nvarchar(300)";
        var string2000Type = isPostgres ? "character varying(2000)" : "nvarchar(2000)";
        var string2048Type = isPostgres ? "character varying(2048)" : "nvarchar(2048)";
        var longTextType = isPostgres ? "text" : "nvarchar(max)";

        migrationBuilder.CreateTable(
            name: "company_briefing_sections",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                briefing_id = table.Column<Guid>(type: guidType, nullable: false),
                section_key = table.Column<string>(type: string256Type, maxLength: 256, nullable: false),
                title = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                grouping_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                grouping_key = table.Column<string>(type: string256Type, maxLength: 256, nullable: false),
                company_entity_id = table.Column<Guid>(type: guidType, nullable: true),
                workflow_instance_id = table.Column<Guid>(type: guidType, nullable: true),
                task_id = table.Column<Guid>(type: guidType, nullable: true),
                event_correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                narrative = table.Column<string>(type: longTextType, nullable: false),
                is_conflicting = table.Column<bool>(type: boolType, nullable: false, defaultValue: false),
                conflict_summary = table.Column<string>(type: string2000Type, maxLength: 2000, nullable: true),
                source_refs_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_briefing_sections", x => x.id);
                table.ForeignKey("FK_company_briefing_sections_company_briefings_briefing_id", x => x.briefing_id, "company_briefings", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_company_briefing_sections_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateTable(
            name: "company_briefing_contributions",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                section_id = table.Column<Guid>(type: guidType, nullable: false),
                agent_id = table.Column<Guid>(type: guidType, nullable: false),
                source_entity_type = table.Column<string>(type: string100Type, maxLength: 100, nullable: false),
                source_entity_id = table.Column<Guid>(type: guidType, nullable: false),
                source_label = table.Column<string>(type: string300Type, maxLength: 300, nullable: false),
                source_status = table.Column<string>(type: string100Type, maxLength: 100, nullable: true),
                source_route = table.Column<string>(type: string2048Type, maxLength: 2048, nullable: true),
                contributed_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                confidence_score = table.Column<decimal>(type: decimalType, nullable: true),
                confidence_metadata_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                company_entity_id = table.Column<Guid>(type: guidType, nullable: true),
                workflow_instance_id = table.Column<Guid>(type: guidType, nullable: true),
                task_id = table.Column<Guid>(type: guidType, nullable: true),
                event_correlation_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                topic = table.Column<string>(type: string300Type, maxLength: 300, nullable: false),
                narrative = table.Column<string>(type: longTextType, nullable: false),
                assessment = table.Column<string>(type: string200Type, maxLength: 200, nullable: true),
                metadata_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_briefing_contributions", x => x.id);
                table.ForeignKey("FK_company_briefing_contributions_company_briefing_sections_section_id", x => x.section_id, "company_briefing_sections", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_company_briefing_contributions_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex("IX_company_briefing_sections_briefing_id_section_key", "company_briefing_sections", new[] { "briefing_id", "section_key" }, unique: true);
        migrationBuilder.CreateIndex("IX_company_briefing_sections_company_id_briefing_id", "company_briefing_sections", new[] { "company_id", "briefing_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_sections_company_id_company_entity_id", "company_briefing_sections", new[] { "company_id", "company_entity_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_sections_company_id_event_correlation_id", "company_briefing_sections", new[] { "company_id", "event_correlation_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_sections_company_id_grouping_type_grouping_key", "company_briefing_sections", new[] { "company_id", "grouping_type", "grouping_key" });
        migrationBuilder.CreateIndex("IX_company_briefing_sections_company_id_task_id", "company_briefing_sections", new[] { "company_id", "task_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_sections_company_id_workflow_instance_id", "company_briefing_sections", new[] { "company_id", "workflow_instance_id" });

        migrationBuilder.CreateIndex("IX_company_briefing_contributions_company_id_agent_id_contributed_at", "company_briefing_contributions", new[] { "company_id", "agent_id", "contributed_at" });
        migrationBuilder.CreateIndex("IX_company_briefing_contributions_company_id_company_entity_id", "company_briefing_contributions", new[] { "company_id", "company_entity_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_contributions_company_id_event_correlation_id", "company_briefing_contributions", new[] { "company_id", "event_correlation_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_contributions_company_id_section_id", "company_briefing_contributions", new[] { "company_id", "section_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_contributions_company_id_source_entity_type_source_entity_id", "company_briefing_contributions", new[] { "company_id", "source_entity_type", "source_entity_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_contributions_company_id_task_id", "company_briefing_contributions", new[] { "company_id", "task_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_contributions_company_id_workflow_instance_id", "company_briefing_contributions", new[] { "company_id", "workflow_instance_id" });
        migrationBuilder.CreateIndex("IX_company_briefing_contributions_section_id", "company_briefing_contributions", "section_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "company_briefing_contributions");
        migrationBuilder.DropTable(name: "company_briefing_sections");
    }
}
