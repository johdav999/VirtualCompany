using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413210000_AddNotificationInbox")]
public partial class AddNotificationInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
        var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
        var string100Type = isPostgres ? "character varying(100)" : "nvarchar(100)";
        var string2048Type = isPostgres ? "character varying(2048)" : "nvarchar(2048)";
        var string300Type = isPostgres ? "character varying(300)" : "nvarchar(300)";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";

        migrationBuilder.DropIndex(name: "IX_company_notifications_company_id_user_id_briefing_id_channel", table: "company_notifications");
        migrationBuilder.AlterColumn<Guid>(name: "briefing_id", table: "company_notifications", type: guidType, nullable: true, oldClrType: typeof(Guid), oldType: guidType);
        migrationBuilder.AddColumn<string>(name: "notification_type", table: "company_notifications", type: string64Type, maxLength: 64, nullable: false, defaultValue: "briefing_available");
        migrationBuilder.AddColumn<string>(name: "priority", table: "company_notifications", type: string32Type, maxLength: 32, nullable: false, defaultValue: "normal");
        migrationBuilder.AddColumn<string>(name: "related_entity_type", table: "company_notifications", type: string100Type, maxLength: 100, nullable: false, defaultValue: "company_briefing");
        migrationBuilder.AddColumn<Guid>(name: "related_entity_id", table: "company_notifications", type: guidType, nullable: true);
        migrationBuilder.AddColumn<string>(name: "action_url", table: "company_notifications", type: string2048Type, maxLength: 2048, nullable: true);
        migrationBuilder.AddColumn<string>(name: "metadata_json", table: "company_notifications", type: jsonType, nullable: false, defaultValueSql: jsonDefault);
        migrationBuilder.AddColumn<string>(name: "dedupe_key", table: "company_notifications", type: string300Type, maxLength: 300, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<DateTime>(name: "read_at", table: "company_notifications", type: dateTimeType, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "actioned_at", table: "company_notifications", type: dateTimeType, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "actioned_by_user_id", table: "company_notifications", type: guidType, nullable: true);

        migrationBuilder.Sql(isPostgres
            ? """
              UPDATE company_notifications
              SET status = 'unread',
                  related_entity_id = briefing_id,
                  dedupe_key = 'briefing-available:' || briefing_id::text || ':' || user_id::text
              WHERE dedupe_key = '';
              """
            : """
              UPDATE [company_notifications]
              SET [status] = N'unread',
                  [related_entity_id] = [briefing_id],
                  [dedupe_key] = CONCAT(N'briefing-available:', CONVERT(nvarchar(36), [briefing_id]), N':', CONVERT(nvarchar(36), [user_id]))
              WHERE [dedupe_key] = N'';
              """);

        migrationBuilder.CreateIndex("IX_company_notifications_company_id_user_id_status_created_at", "company_notifications", new[] { "company_id", "user_id", "status", "created_at" });
        migrationBuilder.CreateIndex("IX_company_notifications_company_id_user_id_notification_type_created_at", "company_notifications", new[] { "company_id", "user_id", "notification_type", "created_at" });
        migrationBuilder.CreateIndex("IX_company_notifications_company_id_user_id_priority_status_created_at", "company_notifications", new[] { "company_id", "user_id", "priority", "status", "created_at" });
        migrationBuilder.CreateIndex("IX_company_notifications_company_id_user_id_dedupe_key", "company_notifications", new[] { "company_id", "user_id", "dedupe_key" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";

        migrationBuilder.DropIndex(name: "IX_company_notifications_company_id_user_id_status_created_at", table: "company_notifications");
        migrationBuilder.DropIndex(name: "IX_company_notifications_company_id_user_id_notification_type_created_at", table: "company_notifications");
        migrationBuilder.DropIndex(name: "IX_company_notifications_company_id_user_id_priority_status_created_at", table: "company_notifications");
        migrationBuilder.DropIndex(name: "IX_company_notifications_company_id_user_id_dedupe_key", table: "company_notifications");
        migrationBuilder.DropColumn(name: "notification_type", table: "company_notifications");
        migrationBuilder.DropColumn(name: "priority", table: "company_notifications");
        migrationBuilder.DropColumn(name: "related_entity_type", table: "company_notifications");
        migrationBuilder.DropColumn(name: "related_entity_id", table: "company_notifications");
        migrationBuilder.DropColumn(name: "action_url", table: "company_notifications");
        migrationBuilder.DropColumn(name: "metadata_json", table: "company_notifications");
        migrationBuilder.DropColumn(name: "dedupe_key", table: "company_notifications");
        migrationBuilder.DropColumn(name: "read_at", table: "company_notifications");
        migrationBuilder.DropColumn(name: "actioned_at", table: "company_notifications");
        migrationBuilder.DropColumn(name: "actioned_by_user_id", table: "company_notifications");
        migrationBuilder.AlterColumn<Guid>(name: "briefing_id", table: "company_notifications", type: guidType, nullable: false, oldClrType: typeof(Guid), oldType: guidType, oldNullable: true);
        migrationBuilder.CreateIndex("IX_company_notifications_company_id_user_id_briefing_id_channel", "company_notifications", new[] { "company_id", "user_id", "briefing_id", "channel" }, unique: true);
    }
}