using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413120000_AddDirectAgentConversations")]
public partial class AddDirectAgentConversations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";
        var jsonDefault = isPostgres ? "'{}'::jsonb" : "N'{}'";

        migrationBuilder.CreateTable(
            name: "conversations",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                channel_type = table.Column<string>(type: isPostgres ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: false),
                subject = table.Column<string>(type: isPostgres ? "character varying(200)" : "nvarchar(200)", maxLength: 200, nullable: true),
                created_by_user_id = table.Column<Guid>(type: guidType, nullable: false),
                agent_id = table.Column<Guid>(type: guidType, nullable: true),
                metadata_json = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_conversations", x => x.id);
                table.ForeignKey(
                    name: "FK_conversations_agents_agent_id",
                    column: x => x.agent_id,
                    principalTable: "agents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_conversations_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_conversations_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                conversation_id = table.Column<Guid>(type: guidType, nullable: false),
                sender_type = table.Column<string>(type: isPostgres ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: false),
                sender_id = table.Column<Guid>(type: guidType, nullable: true),
                message_type = table.Column<string>(type: isPostgres ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: false),
                body = table.Column<string>(type: isPostgres ? "text" : "nvarchar(max)", nullable: false),
                structured_payload = table.Column<string>(type: jsonType, nullable: false, defaultValueSql: jsonDefault),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_messages", x => x.id);
                table.ForeignKey(
                    name: "FK_messages_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_messages_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_company_id_agent_id",
            table: "conversations",
            columns: new[] { "company_id", "agent_id" });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_company_id_channel_type",
            table: "conversations",
            columns: new[] { "company_id", "channel_type" });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_company_id_channel_type_created_by_user_id_agent_id",
            table: "conversations",
            columns: new[] { "company_id", "channel_type", "created_by_user_id", "agent_id" });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_company_id_updated_at",
            table: "conversations",
            columns: new[] { "company_id", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_conversations_created_by_user_id",
            table: "conversations",
            column: "created_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_messages_company_id_conversation_id_created_at",
            table: "messages",
            columns: new[] { "company_id", "conversation_id", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "messages");

        migrationBuilder.DropTable(
            name: "conversations");
    }
}
