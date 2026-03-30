using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddCompanySetupTemplates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "company_setup_templates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                IndustryTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                BusinessTypeTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                defaults_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                metadata_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_setup_templates", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_company_setup_templates_TemplateId",
            table: "company_setup_templates",
            column: "TemplateId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "company_setup_templates");
    }
}
