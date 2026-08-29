using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankConnectivityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    institution_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    institution_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    connected_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    health_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    reason_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    consent_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_health_checked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    suspended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    disconnected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_connections", x => x.id);
                    table.UniqueConstraint("AK_bank_connections_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_connections_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_connections_users_connected_by_user_id",
                        column: x => x.connected_by_user_id,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "bank_connection_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    before_state = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    after_state = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_connection_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_connection_audit_events_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "bank_connection_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    encrypted_envelope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    encryption_key_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_connection_credentials", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_connection_credentials_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_consent_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    institution_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    state_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    nonce_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    provider_session_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    return_uri = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    is_renewal = table.Column<bool>(type: "bit", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_consent_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_consent_sessions_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "bank_consent_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    provider_consent_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    effective_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_consent_versions", x => x.id);
                    table.UniqueConstraint("AK_bank_consent_versions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_consent_versions_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_discovered_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_account_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    masked_account_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ownership_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ownership_summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_available = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    first_discovered_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_discovered_accounts", x => x.id);
                    table.UniqueConstraint("AK_bank_discovered_accounts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_discovered_accounts_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_connection_capability_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consent_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    capability = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_connection_capability_grants", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_connection_capability_grants_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_connection_capability_grants_bank_consent_versions_company_id_consent_version_id",
                        columns: x => new { x.company_id, x.consent_version_id },
                        principalTable: "bank_consent_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_consent_revocation_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consent_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    safe_failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_consent_revocation_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_consent_revocation_tasks_bank_connections_company_id_connection_id",
                        columns: x => new { x.company_id, x.connection_id },
                        principalTable: "bank_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_consent_revocation_tasks_bank_consent_versions_company_id_consent_version_id",
                        columns: x => new { x.company_id, x.consent_version_id },
                        principalTable: "bank_consent_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_account_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    discovered_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    mapped_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_current = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_account_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_account_mappings_bank_discovered_accounts_company_id_discovered_account_id",
                        columns: x => new { x.company_id, x.discovered_account_id },
                        principalTable: "bank_discovered_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_account_mappings_company_bank_accounts_company_id_company_bank_account_id",
                        columns: x => new { x.company_id, x.company_bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bank_account_mappings_company_id_company_bank_account_id",
                table: "bank_account_mappings",
                columns: new[] { "company_id", "company_bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_account_mappings_company_id_discovered_account_id",
                table: "bank_account_mappings",
                columns: new[] { "company_id", "discovered_account_id" },
                unique: true,
                filter: "[is_current] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_bank_account_mappings_company_id_discovered_account_id_version",
                table: "bank_account_mappings",
                columns: new[] { "company_id", "discovered_account_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_connection_audit_events_company_id_connection_id_created_at",
                table: "bank_connection_audit_events",
                columns: new[] { "company_id", "connection_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_connection_audit_events_company_id_correlation_id",
                table: "bank_connection_audit_events",
                columns: new[] { "company_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_connection_capability_grants_company_id_connection_id",
                table: "bank_connection_capability_grants",
                columns: new[] { "company_id", "connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_connection_capability_grants_company_id_consent_version_id_capability",
                table: "bank_connection_capability_grants",
                columns: new[] { "company_id", "consent_version_id", "capability" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_connection_credentials_company_id_connection_id",
                table: "bank_connection_credentials",
                columns: new[] { "company_id", "connection_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_connections_company_id_consent_expires_at",
                table: "bank_connections",
                columns: new[] { "company_id", "consent_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_connections_company_id_provider_key_institution_id",
                table: "bank_connections",
                columns: new[] { "company_id", "provider_key", "institution_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_connections_company_id_status",
                table: "bank_connections",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_connections_connected_by_user_id",
                table: "bank_connections",
                column: "connected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_revocation_tasks_company_id_connection_id",
                table: "bank_consent_revocation_tasks",
                columns: new[] { "company_id", "connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_revocation_tasks_company_id_consent_version_id",
                table: "bank_consent_revocation_tasks",
                columns: new[] { "company_id", "consent_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_revocation_tasks_status_next_attempt_at",
                table: "bank_consent_revocation_tasks",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_sessions_company_id_connection_id",
                table: "bank_consent_sessions",
                columns: new[] { "company_id", "connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_sessions_company_id_expires_at",
                table: "bank_consent_sessions",
                columns: new[] { "company_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_sessions_state_hash",
                table: "bank_consent_sessions",
                column: "state_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_versions_company_id_connection_id_version",
                table: "bank_consent_versions",
                columns: new[] { "company_id", "connection_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_consent_versions_company_id_provider_consent_id",
                table: "bank_consent_versions",
                columns: new[] { "company_id", "provider_consent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_discovered_accounts_company_id_connection_id_provider_account_id",
                table: "bank_discovered_accounts",
                columns: new[] { "company_id", "connection_id", "provider_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_discovered_accounts_company_id_ownership_status",
                table: "bank_discovered_accounts",
                columns: new[] { "company_id", "ownership_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_account_mappings");

            migrationBuilder.DropTable(
                name: "bank_connection_audit_events");

            migrationBuilder.DropTable(
                name: "bank_connection_capability_grants");

            migrationBuilder.DropTable(
                name: "bank_connection_credentials");

            migrationBuilder.DropTable(
                name: "bank_consent_revocation_tasks");

            migrationBuilder.DropTable(
                name: "bank_consent_sessions");

            migrationBuilder.DropTable(
                name: "bank_discovered_accounts");

            migrationBuilder.DropTable(
                name: "bank_consent_versions");

            migrationBuilder.DropTable(
                name: "bank_connections");
        }
    }
}
