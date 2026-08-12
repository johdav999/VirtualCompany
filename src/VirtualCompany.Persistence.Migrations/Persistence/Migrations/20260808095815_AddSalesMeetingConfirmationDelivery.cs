using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingConfirmationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "confirmation_attempt_count",
                table: "sales_meeting_invitations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_error_code",
                table: "sales_meeting_invitations",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_error_summary",
                table: "sales_meeting_invitations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_idempotency_key",
                table: "sales_meeting_invitations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "confirmation_mailbox_connection_id",
                table: "sales_meeting_invitations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_provider_message_id",
                table: "sales_meeting_invitations",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_provider_thread_id",
                table: "sales_meeting_invitations",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "confirmation_sent_at",
                table: "sales_meeting_invitations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_status",
                table: "sales_meeting_invitations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "not_queued");

            migrationBuilder.Sql(
                """
                UPDATE [sales_meeting_invitations]
                SET [confirmation_idempotency_key] = CONCAT(
                    [idempotency_key],
                    ':confirmation:v1')
                WHERE [confirmation_idempotency_key] IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "confirmation_idempotency_key",
                table: "sales_meeting_invitations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_confirmation_idempotency_key",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "confirmation_idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_confirmation_mailbox_connection_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "confirmation_mailbox_connection_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_sales_meeting_invitations_mailbox_connections_company_id_confirmation_mailbox_connection_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "confirmation_mailbox_connection_id" },
                principalTable: "mailbox_connections",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_meeting_invitations_mailbox_connections_company_id_confirmation_mailbox_connection_id",
                table: "sales_meeting_invitations");

            migrationBuilder.DropIndex(
                name: "IX_sales_meeting_invitations_company_id_confirmation_idempotency_key",
                table: "sales_meeting_invitations");

            migrationBuilder.DropIndex(
                name: "IX_sales_meeting_invitations_company_id_confirmation_mailbox_connection_id",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_attempt_count",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_error_code",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_error_summary",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_idempotency_key",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_mailbox_connection_id",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_provider_message_id",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_provider_thread_id",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_sent_at",
                table: "sales_meeting_invitations");

            migrationBuilder.DropColumn(
                name: "confirmation_status",
                table: "sales_meeting_invitations");
        }
    }
}
