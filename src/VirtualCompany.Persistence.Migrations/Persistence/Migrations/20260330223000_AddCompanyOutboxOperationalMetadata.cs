using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCompanyOutboxOperationalMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_company_outbox_messages_ProcessedUtc_AvailableUtc",
                table: "company_outbox_messages");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "company_outbox_messages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptUtc",
                table: "company_outbox_messages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                table: "company_outbox_messages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredUtc",
                table: "company_outbox_messages",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.Sql("""
                UPDATE company_outbox_messages
                SET OccurredUtc = CreatedUtc,
                    MessageType = COALESCE(NULLIF(LTRIM(RTRIM(MessageType)), ''), Topic)
                WHERE OccurredUtc <> CreatedUtc
                   OR MessageType IS NULL
                   OR LTRIM(RTRIM(MessageType)) = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_company_outbox_messages_CompanyId_Topic_IdempotencyKey",
                table: "company_outbox_messages",
                columns: new[] { "CompanyId", "Topic", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_company_outbox_messages_ProcessedUtc_AvailableUtc_AttemptCount",
                table: "company_outbox_messages",
                columns: new[] { "ProcessedUtc", "AvailableUtc", "AttemptCount" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_company_outbox_messages_CompanyId_Topic_IdempotencyKey", table: "company_outbox_messages");
            migrationBuilder.DropIndex(name: "IX_company_outbox_messages_ProcessedUtc_AvailableUtc_AttemptCount", table: "company_outbox_messages");

            migrationBuilder.DropColumn(name: "IdempotencyKey", table: "company_outbox_messages");
            migrationBuilder.DropColumn(name: "LastAttemptUtc", table: "company_outbox_messages");
            migrationBuilder.DropColumn(name: "MessageType", table: "company_outbox_messages");
            migrationBuilder.DropColumn(name: "OccurredUtc", table: "company_outbox_messages");

            migrationBuilder.CreateIndex(
                name: "IX_company_outbox_messages_ProcessedUtc_AvailableUtc",
                table: "company_outbox_messages",
                columns: new[] { "ProcessedUtc", "AvailableUtc" });
        }
    }
}
