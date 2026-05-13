using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260504212000_AddSalesEmailIngestionLinkMetadata")]
    public partial class AddSalesEmailIngestionLinkMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "confidence",
                table: "sales_email_links",
                type: "decimal(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "detected_intent",
                table: "sales_email_links",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_thread_id",
                table: "sales_email_links",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ignore_reason",
                table: "sales_email_links",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internet_message_id",
                table: "sales_email_links",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "link_kind",
                table: "sales_email_links",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "message");

            migrationBuilder.AddColumn<Guid>(
                name: "mailbox_connection_id",
                table: "sales_email_links",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_or_service_interest",
                table: "sales_email_links",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "sales_email_links",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rationale",
                table: "sales_email_links",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.DropIndex(
                name: "IX_sales_email_links_company_id_external_message_id",
                table: "sales_email_links");

            migrationBuilder.CreateIndex(
                name: "IX_sales_email_links_company_provider_mailbox_message_kind",
                table: "sales_email_links",
                columns: new[] { "company_id", "provider", "mailbox_connection_id", "external_message_id", "link_kind" },
                unique: true,
                filter: "[provider] IS NOT NULL AND [mailbox_connection_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_email_links_company_provider_mailbox_thread_kind",
                table: "sales_email_links",
                columns: new[] { "company_id", "provider", "mailbox_connection_id", "external_thread_id", "link_kind" });

            migrationBuilder.AddForeignKey(
                name: "FK_sales_email_links_mailbox_connections_company_id_mailbox_connection_id",
                table: "sales_email_links",
                columns: new[] { "company_id", "mailbox_connection_id" },
                principalTable: "mailbox_connections",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_email_links_mailbox_connections_company_id_mailbox_connection_id",
                table: "sales_email_links");

            migrationBuilder.DropIndex(name: "IX_sales_email_links_company_provider_mailbox_message_kind", table: "sales_email_links");
            migrationBuilder.DropIndex(name: "IX_sales_email_links_company_provider_mailbox_thread_kind", table: "sales_email_links");

            migrationBuilder.DropColumn(name: "confidence", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "detected_intent", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "external_thread_id", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "ignore_reason", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "internet_message_id", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "link_kind", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "mailbox_connection_id", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "product_or_service_interest", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "provider", table: "sales_email_links");
            migrationBuilder.DropColumn(name: "rationale", table: "sales_email_links");

            migrationBuilder.CreateIndex(
                name: "IX_sales_email_links_company_id_external_message_id",
                table: "sales_email_links",
                columns: new[] { "company_id", "external_message_id" });
        }
    }
}
