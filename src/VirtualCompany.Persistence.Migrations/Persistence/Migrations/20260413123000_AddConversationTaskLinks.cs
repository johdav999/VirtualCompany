using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413123000_AddConversationTaskLinks")]
public partial class AddConversationTaskLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var guidType = isPostgres ? "uuid" : "uniqueidentifier";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var linkTypeColumn = isPostgres ? "character varying(64)" : "nvarchar(64)";

        migrationBuilder.CreateTable(
            name: "conversation_task_links",
            columns: table => new
            {
                id = table.Column<Guid>(type: guidType, nullable: false),
                company_id = table.Column<Guid>(type: guidType, nullable: false),
                conversation_id = table.Column<Guid>(type: guidType, nullable: false),
                message_id = table.Column<Guid>(type: guidType, nullable: true),
                task_id = table.Column<Guid>(type: guidType, nullable: false),
                link_type = table.Column<string>(type: linkTypeColumn, maxLength: 64, nullable: false),
                created_by_user_id = table.Column<Guid>(type: guidType, nullable: false),
                created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_conversation_task_links", x => x.id);
                table.ForeignKey(
                    name: "FK_conversation_task_links_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_conversation_task_links_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalTable: "conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_conversation_task_links_messages_message_id",
                    column: x => x.message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_conversation_task_links_tasks_task_id",
                    column: x => x.task_id,
                    principalTable: "tasks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_conversation_task_links_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_conversation_task_links_company_id_conversation_id",
            table: "conversation_task_links",
            columns: new[] { "company_id", "conversation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_conversation_task_links_company_id_conversation_id_task_id_message_id",
            table: "conversation_task_links",
            columns: new[] { "company_id", "conversation_id", "task_id", "message_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_conversation_task_links_company_id_task_id",
            table: "conversation_task_links",
            columns: new[] { "company_id", "task_id" });

        migrationBuilder.CreateIndex(
            name: "IX_conversation_task_links_conversation_id",
            table: "conversation_task_links",
            column: "conversation_id");

        migrationBuilder.CreateIndex(
            name: "IX_conversation_task_links_created_by_user_id",
            table: "conversation_task_links",
            column: "created_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_conversation_task_links_message_id",
            table: "conversation_task_links",
            column: "message_id");

        migrationBuilder.CreateIndex(
            name: "IX_conversation_task_links_task_id",
            table: "conversation_task_links",
            column: "task_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "conversation_task_links");
    }
}
