using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260415100000_AddBriefingPreferences")]
public partial class AddBriefingPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonObjectDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var jsonArrayDefault = isPostgres ? "'[]'::jsonb" : "N'[]'";

        migrationBuilder.AddColumn<string>(
            name: "preference_snapshot_json",
            table: "company_briefings",
            type: jsonType,
            nullable: false,
            defaultValueSql: jsonObjectDefault);

        migrationBuilder.CreateTable(
            name: "tenant_briefing_defaults",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                delivery_frequency = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                included_focus_areas_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonArrayDefault),
                priority_threshold = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant_briefing_defaults", x => x.id);
                table.ForeignKey(
                    "FK_tenant_briefing_defaults_companies_company_id",
                    x => x.company_id,
                    "companies",
                    "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_briefing_preferences",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                user_id = table.Column<Guid>(type: guidType, nullable: false),
                delivery_frequency = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                included_focus_areas_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonArrayDefault),
                priority_threshold = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_briefing_preferences", x => x.id);
                table.ForeignKey(
                    "FK_user_briefing_preferences_companies_company_id",
                    x => x.company_id,
                    "companies",
                    "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    "FK_user_briefing_preferences_users_user_id",
                    x => x.user_id,
                    "users",
                    "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tenant_briefing_defaults_company_id",
            table: "tenant_briefing_defaults",
            column: "company_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_briefing_preferences_company_id_user_id",
            table: "user_briefing_preferences",
            columns: new[] { "company_id", "user_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_briefing_preferences_user_id",
            table: "user_briefing_preferences",
            column: "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "tenant_briefing_defaults");
        migrationBuilder.DropTable(name: "user_briefing_preferences");
        migrationBuilder.DropColumn(name: "preference_snapshot_json", table: "company_briefings");
    }
}