using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddMailboxConnectionPersistence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mailbox_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    email_address = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    mailbox_external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    encrypted_access_token = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    encrypted_refresh_token = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    encrypted_credential_envelope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    access_token_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    granted_scopes_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    configured_folders_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    provider_metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    last_successful_scan_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_connections", x => x.id);
                    table.UniqueConstraint("AK_mailbox_connections_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_mailbox_connections_provider", "provider IN ('gmail', 'microsoft365')");
                    table.CheckConstraint("CK_mailbox_connections_status", "status IN ('pending', 'active', 'token_expired', 'revoked', 'failed', 'disconnected')");
                    table.ForeignKey(
                        name: "FK_mailbox_connections_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mailbox_connections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_ingestion_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mailbox_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    triggered_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    scan_from_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    scan_to_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    scanned_message_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    detected_candidate_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    failure_details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_ingestion_runs", x => x.id);
                    table.CheckConstraint("CK_email_ingestion_runs_detected_candidate_count_nonnegative", "detected_candidate_count >= 0");
                    table.CheckConstraint("CK_email_ingestion_runs_provider", "provider IN ('gmail', 'microsoft365')");
                    table.CheckConstraint("CK_email_ingestion_runs_scanned_message_count_nonnegative", "scanned_message_count >= 0");
                    table.ForeignKey(
                        name: "FK_email_ingestion_runs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_ingestion_runs_mailbox_connections_mailbox_connection_id",
                        column: x => x.mailbox_connection_id,
                        principalTable: "mailbox_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_ingestion_runs_users_triggered_by_user_id",
                        column: x => x.triggered_by_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_ingestion_runs_company_id",
                table: "email_ingestion_runs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_email_ingestion_runs_company_id_mailbox_connection_id_started_at",
                table: "email_ingestion_runs",
                columns: new[] { "company_id", "mailbox_connection_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_email_ingestion_runs_company_id_provider_started_at",
                table: "email_ingestion_runs",
                columns: new[] { "company_id", "provider", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_email_ingestion_runs_company_id_triggered_by_user_id_started_at",
                table: "email_ingestion_runs",
                columns: new[] { "company_id", "triggered_by_user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_email_ingestion_runs_mailbox_connection_id",
                table: "email_ingestion_runs",
                column: "mailbox_connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_email_ingestion_runs_triggered_by_user_id",
                table: "email_ingestion_runs",
                column: "triggered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_connections_company_id",
                table: "mailbox_connections",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_connections_company_id_provider_email_address",
                table: "mailbox_connections",
                columns: new[] { "company_id", "provider", "email_address" });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_connections_company_id_status",
                table: "mailbox_connections",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_connections_company_id_user_id",
                table: "mailbox_connections",
                columns: new[] { "company_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_connections_user_id",
                table: "mailbox_connections",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "email_ingestion_runs");
            migrationBuilder.DropTable(name: "mailbox_connections");
        }
    }
}
