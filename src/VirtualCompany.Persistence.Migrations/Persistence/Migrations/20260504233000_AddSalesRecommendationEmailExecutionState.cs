using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260504233000_AddSalesRecommendationEmailExecutionState")]
public partial class AddSalesRecommendationEmailExecutionState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "execution_attempt_count",
            table: "sales_agent_recommendations",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "last_execution_error_code",
            table: "sales_agent_recommendations",
            type: "nvarchar(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "provider",
            table: "sales_agent_recommendations",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "mailbox_connection_id",
            table: "sales_agent_recommendations",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "provider_thread_id",
            table: "sales_agent_recommendations",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "provider_message_id",
            table: "sales_agent_recommendations",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "provider_draft_id",
            table: "sales_agent_recommendations",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "activity_id",
            table: "sales_agent_recommendations",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "execution_idempotency_key",
            table: "sales_agent_recommendations",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTime>(
            name: "executed_at",
            table: "sales_agent_recommendations",
            type: "datetime2",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE sales_agent_recommendations
            SET execution_idempotency_key = CONCAT('sales-recommendation:', LOWER(CONVERT(nvarchar(32), company_id, 2)), ':', LOWER(CONVERT(nvarchar(32), id, 2)), ':', action_type)
            WHERE execution_idempotency_key = '';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_sales_agent_recommendations_company_id_execution_idempotency_key",
            table: "sales_agent_recommendations",
            columns: new[] { "company_id", "execution_idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_agent_recommendations_company_id_provider_mailbox_connection_id_provider_message_id",
            table: "sales_agent_recommendations",
            columns: new[] { "company_id", "provider", "mailbox_connection_id", "provider_message_id" },
            filter: "[provider_message_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_agent_recommendations_company_id_provider_mailbox_connection_id_provider_draft_id",
            table: "sales_agent_recommendations",
            columns: new[] { "company_id", "provider", "mailbox_connection_id", "provider_draft_id" },
            filter: "[provider_draft_id] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_sales_agent_recommendations_company_id_execution_idempotency_key", table: "sales_agent_recommendations");
        migrationBuilder.DropIndex(name: "IX_sales_agent_recommendations_company_id_provider_mailbox_connection_id_provider_message_id", table: "sales_agent_recommendations");
        migrationBuilder.DropIndex(name: "IX_sales_agent_recommendations_company_id_provider_mailbox_connection_id_provider_draft_id", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "execution_attempt_count", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "last_execution_error_code", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "provider", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "mailbox_connection_id", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "provider_thread_id", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "provider_message_id", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "provider_draft_id", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "activity_id", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "execution_idempotency_key", table: "sales_agent_recommendations");
        migrationBuilder.DropColumn(name: "executed_at", table: "sales_agent_recommendations");
    }
}
