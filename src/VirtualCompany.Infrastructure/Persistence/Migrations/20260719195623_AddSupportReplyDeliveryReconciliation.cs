using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportReplyDeliveryReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "delivery_status",
                table: "support_reply_drafts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "last_delivery_attempt_at",
                table: "support_reply_drafts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [support_reply_drafts]
                SET [delivery_status] = CASE
                        WHEN [sent_at] IS NOT NULL THEN 'sent'
                        WHEN [send_failure_summary] IS NOT NULL THEN 'failed'
                        ELSE 'pending'
                    END,
                    [last_delivery_attempt_at] = CASE
                        WHEN [sent_at] IS NOT NULL THEN [sent_at]
                        WHEN [send_failure_summary] IS NOT NULL THEN [updated_at]
                        ELSE NULL
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_support_reply_drafts_company_id_delivery_status",
                table: "support_reply_drafts",
                columns: new[] { "company_id", "delivery_status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_support_reply_drafts_delivery_status",
                table: "support_reply_drafts",
                sql: "[delivery_status] IN ('pending', 'sent', 'failed', 'reconciliation_required')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_support_reply_drafts_company_id_delivery_status",
                table: "support_reply_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_support_reply_drafts_delivery_status",
                table: "support_reply_drafts");

            migrationBuilder.DropColumn(
                name: "delivery_status",
                table: "support_reply_drafts");

            migrationBuilder.DropColumn(
                name: "last_delivery_attempt_at",
                table: "support_reply_drafts");
        }
    }
}
