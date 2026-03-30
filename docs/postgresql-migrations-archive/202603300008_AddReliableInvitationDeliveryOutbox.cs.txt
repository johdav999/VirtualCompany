using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddReliableInvitationDeliveryOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DeliveryError",
            table: "company_invitations",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DeliveryStatus",
            table: "company_invitations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "pending");

        migrationBuilder.AddColumn<DateTime>(
            name: "DeliveredUtc",
            table: "company_invitations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastDeliveryAttemptUtc",
            table: "company_invitations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastDeliveredTokenHash",
            table: "company_invitations",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastDeliveryCorrelationId",
            table: "company_invitations",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "AvailableUtc",
            table: "company_outbox_messages",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "NOW()");

        migrationBuilder.AddColumn<int>(
            name: "AttemptCount",
            table: "company_outbox_messages",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "ClaimedUtc",
            table: "company_outbox_messages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ClaimToken",
            table: "company_outbox_messages",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CorrelationId",
            table: "company_outbox_messages",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastError",
            table: "company_outbox_messages",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE company_outbox_messages
            SET "AvailableUtc" = "CreatedUtc"
            WHERE "AvailableUtc" > "CreatedUtc";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_company_invitations_CompanyId_DeliveryStatus",
            table: "company_invitations",
            columns: new[] { "CompanyId", "DeliveryStatus" });

        migrationBuilder.CreateIndex(
            name: "IX_company_outbox_messages_ProcessedUtc_AvailableUtc",
            table: "company_outbox_messages",
            columns: new[] { "ProcessedUtc", "AvailableUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_company_outbox_messages_ProcessedUtc_ClaimedUtc",
            table: "company_outbox_messages",
            columns: new[] { "ProcessedUtc", "ClaimedUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_company_invitations_CompanyId_DeliveryStatus",
            table: "company_invitations");

        migrationBuilder.DropIndex(
            name: "IX_company_outbox_messages_ProcessedUtc_AvailableUtc",
            table: "company_outbox_messages");

        migrationBuilder.DropIndex(
            name: "IX_company_outbox_messages_ProcessedUtc_ClaimedUtc",
            table: "company_outbox_messages");

        migrationBuilder.DropColumn(name: "DeliveryError", table: "company_invitations");
        migrationBuilder.DropColumn(name: "DeliveryStatus", table: "company_invitations");
        migrationBuilder.DropColumn(name: "DeliveredUtc", table: "company_invitations");
        migrationBuilder.DropColumn(name: "LastDeliveryAttemptUtc", table: "company_invitations");
        migrationBuilder.DropColumn(name: "LastDeliveredTokenHash", table: "company_invitations");
        migrationBuilder.DropColumn(name: "LastDeliveryCorrelationId", table: "company_invitations");

        migrationBuilder.DropColumn(name: "AvailableUtc", table: "company_outbox_messages");
        migrationBuilder.DropColumn(name: "AttemptCount", table: "company_outbox_messages");
        migrationBuilder.DropColumn(name: "ClaimedUtc", table: "company_outbox_messages");
        migrationBuilder.DropColumn(name: "ClaimToken", table: "company_outbox_messages");
        migrationBuilder.DropColumn(name: "CorrelationId", table: "company_outbox_messages");
        migrationBuilder.DropColumn(name: "LastError", table: "company_outbox_messages");
    }
}