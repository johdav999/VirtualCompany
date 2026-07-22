using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddStandardMailboxTransport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_mailbox_connections_provider", "mailbox_connections");
        migrationBuilder.DropCheckConstraint("CK_email_ingestion_runs_provider", "email_ingestion_runs");

        migrationBuilder.AddColumn<string>("authenticated_username", "mailbox_connections", "nvarchar(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>("authentication_type", "mailbox_connections", "nvarchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<int>("capability_flags", "mailbox_connections", "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("imap_host", "mailbox_connections", "nvarchar(253)", maxLength: 253, nullable: true);
        migrationBuilder.AddColumn<int>("imap_port", "mailbox_connections", "int", nullable: true);
        migrationBuilder.AddColumn<string>("imap_tls_mode", "mailbox_connections", "nvarchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<DateTime>("last_health_check_at", "mailbox_connections", "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("profile_key", "mailbox_connections", "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("smtp_host", "mailbox_connections", "nvarchar(253)", maxLength: 253, nullable: true);
        migrationBuilder.AddColumn<int>("smtp_port", "mailbox_connections", "int", nullable: true);
        migrationBuilder.AddColumn<string>("smtp_tls_mode", "mailbox_connections", "nvarchar(32)", maxLength: 32, nullable: true);

        migrationBuilder.CreateTable(
            name: "mailbox_folder_sync_cursors",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                mailbox_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                folder_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                uid_validity = table.Column<long>(type: "bigint", nullable: true),
                last_processed_uid = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                highest_mod_sequence = table.Column<long>(type: "bigint", nullable: true),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                last_successful_sync_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_mailbox_folder_sync_cursors", x => x.id);
                table.CheckConstraint("CK_mailbox_folder_sync_cursors_status", "status IN ('active', 'reconciliation_required')");
                table.ForeignKey(
                    "FK_mailbox_folder_sync_cursors_mailbox_connections_company_id_mailbox_connection_id",
                    x => new { x.company_id, x.mailbox_connection_id },
                    "mailbox_connections",
                    new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_mailbox_connections_company_id_profile_key", "mailbox_connections", new[] { "company_id", "profile_key" });
        migrationBuilder.CreateIndex("IX_mailbox_folder_sync_cursors_company_id", "mailbox_folder_sync_cursors", "company_id");
        migrationBuilder.CreateIndex(
            "IX_mailbox_folder_sync_cursors_company_id_mailbox_connection_id_folder_id",
            "mailbox_folder_sync_cursors",
            new[] { "company_id", "mailbox_connection_id", "folder_id" },
            unique: true);

        migrationBuilder.AddCheckConstraint(
            "CK_mailbox_connections_provider",
            "mailbox_connections",
            "provider IN ('gmail', 'microsoft365', 'standard_email')");
        migrationBuilder.AddCheckConstraint(
            "CK_email_ingestion_runs_provider",
            "email_ingestion_runs",
            "provider IN ('gmail', 'microsoft365', 'standard_email')");
        migrationBuilder.AddCheckConstraint("CK_mailbox_connections_authentication_type", "mailbox_connections", "authentication_type IS NULL OR authentication_type IN ('oauth2', 'application_password')");
        migrationBuilder.AddCheckConstraint("CK_mailbox_connections_imap_tls_mode", "mailbox_connections", "imap_tls_mode IS NULL OR imap_tls_mode IN ('implicit_tls', 'starttls')");
        migrationBuilder.AddCheckConstraint("CK_mailbox_connections_smtp_tls_mode", "mailbox_connections", "smtp_tls_mode IS NULL OR smtp_tls_mode IN ('implicit_tls', 'starttls')");
        migrationBuilder.AddCheckConstraint("CK_mailbox_connections_imap_port", "mailbox_connections", "imap_port IS NULL OR (imap_port >= 1 AND imap_port <= 65535)");
        migrationBuilder.AddCheckConstraint("CK_mailbox_connections_smtp_port", "mailbox_connections", "smtp_port IS NULL OR (smtp_port >= 1 AND smtp_port <= 65535)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_mailbox_connections_provider", "mailbox_connections");
        migrationBuilder.DropCheckConstraint("CK_email_ingestion_runs_provider", "email_ingestion_runs");
        migrationBuilder.DropTable("mailbox_folder_sync_cursors");
        migrationBuilder.DropIndex("IX_mailbox_connections_company_id_profile_key", "mailbox_connections");
        migrationBuilder.DropCheckConstraint("CK_mailbox_connections_authentication_type", "mailbox_connections");
        migrationBuilder.DropCheckConstraint("CK_mailbox_connections_imap_tls_mode", "mailbox_connections");
        migrationBuilder.DropCheckConstraint("CK_mailbox_connections_smtp_tls_mode", "mailbox_connections");
        migrationBuilder.DropCheckConstraint("CK_mailbox_connections_imap_port", "mailbox_connections");
        migrationBuilder.DropCheckConstraint("CK_mailbox_connections_smtp_port", "mailbox_connections");

        migrationBuilder.DropColumn("authenticated_username", "mailbox_connections");
        migrationBuilder.DropColumn("authentication_type", "mailbox_connections");
        migrationBuilder.DropColumn("capability_flags", "mailbox_connections");
        migrationBuilder.DropColumn("imap_host", "mailbox_connections");
        migrationBuilder.DropColumn("imap_port", "mailbox_connections");
        migrationBuilder.DropColumn("imap_tls_mode", "mailbox_connections");
        migrationBuilder.DropColumn("last_health_check_at", "mailbox_connections");
        migrationBuilder.DropColumn("profile_key", "mailbox_connections");
        migrationBuilder.DropColumn("smtp_host", "mailbox_connections");
        migrationBuilder.DropColumn("smtp_port", "mailbox_connections");
        migrationBuilder.DropColumn("smtp_tls_mode", "mailbox_connections");

        migrationBuilder.AddCheckConstraint(
            "CK_mailbox_connections_provider",
            "mailbox_connections",
            "provider IN ('gmail', 'microsoft365')");
        migrationBuilder.AddCheckConstraint(
            "CK_email_ingestion_runs_provider",
            "email_ingestion_runs",
            "provider IN ('gmail', 'microsoft365')");
    }
}
