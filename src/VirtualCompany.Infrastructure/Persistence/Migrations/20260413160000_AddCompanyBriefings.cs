using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413160000_AddCompanyBriefings")]
public partial class AddCompanyBriefings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
        var string100Type = isPostgres ? "character varying(100)" : "nvarchar(100)";
        var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
        var string4000Type = isPostgres ? "character varying(4000)" : "nvarchar(4000)";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";
        var boolType = isPostgres ? "boolean" : "bit";

        migrationBuilder.CreateTable(
            name: "company_briefings",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                briefing_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                period_start_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                period_end_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                title = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                summary_body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                structured_payload_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                source_refs_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                message_id = table.Column<Guid>(type: guidType, nullable: true),
                generated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_briefings", x => x.id);
                table.ForeignKey(
                    name: "FK_company_briefings_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_company_briefings_messages_message_id",
                    column: x => x.message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "company_briefing_delivery_preferences",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                user_id = table.Column<Guid>(type: guidType, nullable: false),
                in_app_enabled = table.Column<bool>(type: boolType, nullable: false, defaultValue: true),
                mobile_enabled = table.Column<bool>(type: boolType, nullable: false, defaultValue: false),
                daily_enabled = table.Column<bool>(type: boolType, nullable: false, defaultValue: true),
                weekly_enabled = table.Column<bool>(type: boolType, nullable: false, defaultValue: true),
                preferred_delivery_time = table.Column<TimeOnly>(type: "time", nullable: false, defaultValue: new TimeOnly(8, 0)),
                preferred_timezone = table.Column<string>(type: string100Type, maxLength: 100, nullable: true),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_briefing_delivery_preferences", x => x.id);
                table.ForeignKey("FK_company_briefing_delivery_preferences_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_company_briefing_delivery_preferences_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "company_notifications",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                user_id = table.Column<Guid>(type: guidType, nullable: false),
                briefing_id = table.Column<Guid>(type: guidType, nullable: false),
                channel = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                title = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                body = table.Column<string>(type: string4000Type, maxLength: 4000, nullable: false),
                status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_notifications", x => x.id);
                table.ForeignKey("FK_company_notifications_company_briefings_briefing_id", x => x.briefing_id, "company_briefings", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_company_notifications_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_company_notifications_users_user_id", x => x.user_id, "users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_company_briefings_company_id_briefing_type_generated_at", "company_briefings", new[] { "company_id", "briefing_type", "generated_at" });
        migrationBuilder.CreateIndex("IX_company_briefings_company_id_briefing_type_period_start_at_period_end_at", "company_briefings", new[] { "company_id", "briefing_type", "period_start_at", "period_end_at" }, unique: true);
        migrationBuilder.CreateIndex("IX_company_briefings_message_id", "company_briefings", "message_id", unique: true, filter: isPostgres ? "message_id IS NOT NULL" : "[message_id] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_company_briefing_delivery_preferences_company_id_user_id", "company_briefing_delivery_preferences", new[] { "company_id", "user_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_company_briefing_delivery_preferences_user_id", "company_briefing_delivery_preferences", "user_id");
        migrationBuilder.CreateIndex("IX_company_notifications_briefing_id", "company_notifications", "briefing_id");
        migrationBuilder.CreateIndex("IX_company_notifications_company_id_briefing_id", "company_notifications", new[] { "company_id", "briefing_id" });
        migrationBuilder.CreateIndex("IX_company_notifications_company_id_user_id_briefing_id_channel", "company_notifications", new[] { "company_id", "user_id", "briefing_id", "channel" }, unique: true);
        migrationBuilder.CreateIndex("IX_company_notifications_company_id_user_id_created_at", "company_notifications", new[] { "company_id", "user_id", "created_at" });
        migrationBuilder.CreateIndex("IX_company_notifications_user_id", "company_notifications", "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "company_notifications");
        migrationBuilder.DropTable(name: "company_briefing_delivery_preferences");
        migrationBuilder.DropTable(name: "company_briefings");
    }
}
