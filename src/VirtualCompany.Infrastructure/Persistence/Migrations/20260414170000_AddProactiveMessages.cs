using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddProactiveMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "proactive_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                recipient = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                source_entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                source_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                originating_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                notification_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                sent_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                policy_decision_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                policy_decision_reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_proactive_messages", x => x.id);
                table.ForeignKey(
                    name: "FK_proactive_messages_agents_originating_agent_id",
                    column: x => x.originating_agent_id,
                    principalTable: "agents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_proactive_messages_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_proactive_messages_company_notifications_notification_id",
                    column: x => x.notification_id,
                    principalTable: "company_notifications",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_proactive_messages_users_recipient_user_id",
                    column: x => x.recipient_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_messages_company_id_channel_sent_at",
            table: "proactive_messages",
            columns: new[] { "company_id", "channel", "sent_at" });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_messages_company_id_recipient_user_id_sent_at",
            table: "proactive_messages",
            columns: new[] { "company_id", "recipient_user_id", "sent_at" });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_messages_company_id_source_entity_type_source_entity_id",
            table: "proactive_messages",
            columns: new[] { "company_id", "source_entity_type", "source_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_messages_notification_id",
            table: "proactive_messages",
            column: "notification_id");

        migrationBuilder.CreateIndex(
            name: "IX_proactive_messages_originating_agent_id",
            table: "proactive_messages",
            column: "originating_agent_id");

        migrationBuilder.CreateIndex(
            name: "IX_proactive_messages_recipient_user_id",
            table: "proactive_messages",
            column: "recipient_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "proactive_messages");
    }
}
