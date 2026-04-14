using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddProactiveMessagePolicyDecisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "proactive_message_policy_decisions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                proactive_message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                recipient = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                source_entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                source_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                originating_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                reason_code = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                reason_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                evaluated_autonomy_level = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                policy_decision_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_proactive_message_policy_decisions", x => x.id);
                table.ForeignKey(
                    name: "FK_proactive_message_policy_decisions_agents_originating_agent_id",
                    column: x => x.originating_agent_id,
                    principalTable: "agents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_proactive_message_policy_decisions_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_proactive_message_policy_decisions_proactive_messages_proactive_message_id",
                    column: x => x.proactive_message_id,
                    principalTable: "proactive_messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_proactive_message_policy_decisions_users_recipient_user_id",
                    column: x => x.recipient_user_id,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_message_policy_decisions_company_id_channel_created_at",
            table: "proactive_message_policy_decisions",
            columns: new[] { "company_id", "channel", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_message_policy_decisions_company_id_outcome_created_at",
            table: "proactive_message_policy_decisions",
            columns: new[] { "company_id", "outcome", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_message_policy_decisions_company_id_recipient_user_id_created_at",
            table: "proactive_message_policy_decisions",
            columns: new[] { "company_id", "recipient_user_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_message_policy_decisions_company_id_source_entity_type_source_entity_id",
            table: "proactive_message_policy_decisions",
            columns: new[] { "company_id", "source_entity_type", "source_entity_id" });

        migrationBuilder.CreateIndex(
            name: "IX_proactive_message_policy_decisions_originating_agent_id",
            table: "proactive_message_policy_decisions",
            column: "originating_agent_id");

        migrationBuilder.CreateIndex(
            name: "IX_proactive_message_policy_decisions_proactive_message_id",
            table: "proactive_message_policy_decisions",
            column: "proactive_message_id");

        migrationBuilder.CreateIndex(
            name: "IX_proactive_message_policy_decisions_recipient_user_id",
            table: "proactive_message_policy_decisions",
            column: "recipient_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "proactive_message_policy_decisions");
    }
}
