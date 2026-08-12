using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCalendarConnectionsFromMailboxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_meeting_invitations_mailbox_connections_company_id_mailbox_connection_id",
                table: "sales_meeting_invitations");

            migrationBuilder.RenameColumn(
                name: "mailbox_connection_id",
                table: "sales_meeting_invitations",
                newName: "calendar_connection_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_meeting_invitations_company_id_mailbox_connection_id",
                table: "sales_meeting_invitations",
                newName: "IX_sales_meeting_invitations_company_id_calendar_connection_id");

            migrationBuilder.AddColumn<Guid>(
                name: "external_account_connection_id",
                table: "mailbox_connections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_primary_inbound",
                table: "mailbox_connections",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "external_account_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    account_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    external_account_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    credential_purpose_prefix = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    encrypted_access_token = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    encrypted_refresh_token = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    access_token_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    granted_scopes_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    last_error_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_account_connections", x => x.id);
                    table.UniqueConstraint("AK_external_account_connections_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_external_account_connections_provider", "provider IN ('google', 'microsoft365')");
                    table.CheckConstraint("CK_external_account_connections_status", "status IN ('pending', 'active', 'token_expired', 'revoked', 'failed', 'disconnected')");
                    table.ForeignKey(
                        name: "FK_external_account_connections_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_external_account_connections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_account_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    account_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    calendar_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    time_zone_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    capability_flags = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    last_health_check_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_error_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_connections", x => x.id);
                    table.UniqueConstraint("AK_calendar_connections_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_calendar_connections_provider", "provider IN ('google', 'microsoft365')");
                    table.CheckConstraint("CK_calendar_connections_status", "status IN ('pending', 'active', 'token_expired', 'revoked', 'failed', 'disconnected')");
                    table.ForeignKey(
                        name: "FK_calendar_connections_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_calendar_connections_external_account_connections_company_id_external_account_connection_id",
                        columns: x => new { x.company_id, x.external_account_connection_id },
                        principalTable: "external_account_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_calendar_connections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                WITH ranked_mailboxes AS
                (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY company_id, purpose
                            ORDER BY
                                CASE WHEN status = N'active' THEN 0 ELSE 1 END,
                                updated_at DESC,
                                id) AS primary_rank
                    FROM mailbox_connections
                )
                UPDATE mailbox_connections
                SET is_primary_inbound =
                    CASE WHEN ranked_mailboxes.primary_rank = 1
                        THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END
                FROM mailbox_connections
                INNER JOIN ranked_mailboxes
                    ON ranked_mailboxes.id = mailbox_connections.id;

                UPDATE mailbox_connections
                SET capability_flags = 63
                WHERE provider IN (N'gmail', N'microsoft365');

                WITH ranked_accounts AS
                (
                    SELECT
                        mailbox.*,
                        ROW_NUMBER() OVER (
                            PARTITION BY mailbox.company_id, mailbox.provider, mailbox.email_address
                            ORDER BY
                                CASE WHEN mailbox.status = N'active' THEN 0 ELSE 1 END,
                                mailbox.updated_at DESC,
                                mailbox.id) AS account_rank
                    FROM mailbox_connections AS mailbox
                    WHERE mailbox.provider IN (N'gmail', N'microsoft365')
                )
                INSERT INTO external_account_connections
                (
                    id, company_id, user_id, provider, account_email, display_name,
                    external_account_id, credential_purpose_prefix, status,
                    encrypted_access_token, encrypted_refresh_token, access_token_expires_at,
                    granted_scopes_json, created_at, updated_at
                )
                SELECT
                    id,
                    company_id,
                    user_id,
                    CASE WHEN provider = N'gmail' THEN N'google' ELSE N'microsoft365' END,
                    email_address,
                    display_name,
                    mailbox_external_id,
                    CASE WHEN provider = N'gmail' THEN N'mailbox:gmail' ELSE N'mailbox:microsoft365' END,
                    status,
                    encrypted_access_token,
                    encrypted_refresh_token,
                    access_token_expires_at,
                    granted_scopes_json,
                    created_at,
                    updated_at
                FROM ranked_accounts
                WHERE account_rank = 1;

                UPDATE mailbox
                SET external_account_connection_id = account.id
                FROM mailbox_connections AS mailbox
                INNER JOIN external_account_connections AS account
                    ON account.company_id = mailbox.company_id
                    AND account.account_email = mailbox.email_address
                    AND account.provider =
                        CASE WHEN mailbox.provider = N'gmail' THEN N'google' ELSE mailbox.provider END
                WHERE mailbox.provider IN (N'gmail', N'microsoft365');

                INSERT INTO calendar_connections
                (
                    id, company_id, user_id, external_account_connection_id,
                    provider, account_email, display_name, calendar_id,
                    capability_flags, status, created_at, updated_at
                )
                SELECT
                    account.id,
                    account.company_id,
                    account.user_id,
                    account.id,
                    account.provider,
                    account.account_email,
                    account.display_name,
                    N'primary',
                    31,
                    account.status,
                    account.created_at,
                    account.updated_at
                FROM external_account_connections AS account;

                UPDATE invitation
                SET
                    calendar_connection_id = mailbox.external_account_connection_id,
                    provider = CASE WHEN invitation.provider = N'gmail' THEN N'google' ELSE invitation.provider END
                FROM sales_meeting_invitations AS invitation
                INNER JOIN mailbox_connections AS mailbox
                    ON mailbox.company_id = invitation.company_id
                    AND mailbox.id = invitation.calendar_connection_id;
                """);
            migrationBuilder.CreateIndex(
                name: "IX_mailbox_connections_company_id_external_account_connection_id",
                table: "mailbox_connections",
                columns: new[] { "company_id", "external_account_connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_connections_company_id_purpose_is_primary_inbound",
                table: "mailbox_connections",
                columns: new[] { "company_id", "purpose", "is_primary_inbound" },
                unique: true,
                filter: "[is_primary_inbound] = CAST(1 AS bit)");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_connections_company_id_external_account_connection_id_calendar_id",
                table: "calendar_connections",
                columns: new[] { "company_id", "external_account_connection_id", "calendar_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_connections_company_id_status_updated_at",
                table: "calendar_connections",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_connections_user_id",
                table: "calendar_connections",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_account_connections_company_id_provider_account_email",
                table: "external_account_connections",
                columns: new[] { "company_id", "provider", "account_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_account_connections_company_id_status_updated_at",
                table: "external_account_connections",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_external_account_connections_user_id",
                table: "external_account_connections",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_mailbox_connections_external_account_connections_company_id_external_account_connection_id",
                table: "mailbox_connections",
                columns: new[] { "company_id", "external_account_connection_id" },
                principalTable: "external_account_connections",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_meeting_invitations_calendar_connections_company_id_calendar_connection_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "calendar_connection_id" },
                principalTable: "calendar_connections",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mailbox_connections_external_account_connections_company_id_external_account_connection_id",
                table: "mailbox_connections");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_meeting_invitations_calendar_connections_company_id_calendar_connection_id",
                table: "sales_meeting_invitations");

            migrationBuilder.DropTable(
                name: "calendar_connections");

            migrationBuilder.DropTable(
                name: "external_account_connections");

            migrationBuilder.DropIndex(
                name: "IX_mailbox_connections_company_id_external_account_connection_id",
                table: "mailbox_connections");

            migrationBuilder.DropIndex(
                name: "IX_mailbox_connections_company_id_purpose_is_primary_inbound",
                table: "mailbox_connections");

            migrationBuilder.DropColumn(
                name: "external_account_connection_id",
                table: "mailbox_connections");

            migrationBuilder.DropColumn(
                name: "is_primary_inbound",
                table: "mailbox_connections");

            migrationBuilder.RenameColumn(
                name: "calendar_connection_id",
                table: "sales_meeting_invitations",
                newName: "mailbox_connection_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_meeting_invitations_company_id_calendar_connection_id",
                table: "sales_meeting_invitations",
                newName: "IX_sales_meeting_invitations_company_id_mailbox_connection_id");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_meeting_invitations_mailbox_connections_company_id_mailbox_connection_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "mailbox_connection_id" },
                principalTable: "mailbox_connections",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
