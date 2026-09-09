using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsPresenterTenantIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teams_tenant_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    entra_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    teams_app_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bot_application_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approved_media_route = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    required_permissions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    granted_permissions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    consent_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    permission_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    policy_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    consent_verified_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    consent_verified_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    permissions_verified_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    policy_approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    policy_approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    disabled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    disabled_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams_tenant_registrations", x => x.id);
                    table.UniqueConstraint("AK_teams_tenant_registrations_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_teams_tenant_registration_consent", "consent_status IN ('pending','verified','revoked')");
                    table.CheckConstraint("CK_teams_tenant_registration_permission", "permission_status IN ('pending','verified','missing','excess','revoked')");
                    table.CheckConstraint("CK_teams_tenant_registration_policy", "policy_status IN ('pending','attested','revoked')");
                    table.CheckConstraint("CK_teams_tenant_registration_status", "status IN ('pending_consent','pending_policy','ready','blocked','disabled','revoked')");
                    table.ForeignKey(
                        name: "FK_teams_tenant_registrations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teams_admin_consent_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    state_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams_admin_consent_sessions", x => x.id);
                    table.UniqueConstraint("AK_teams_admin_consent_sessions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_teams_admin_consent_sessions_teams_tenant_registrations_company_id_registration_id",
                        columns: x => new { x.company_id, x.registration_id },
                        principalTable: "teams_tenant_registrations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_teams_admin_consent_sessions_company_id_registration_id",
                table: "teams_admin_consent_sessions",
                columns: new[] { "company_id", "registration_id" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_admin_consent_sessions_expires_at_consumed_at",
                table: "teams_admin_consent_sessions",
                columns: new[] { "expires_at", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_admin_consent_sessions_state_hash",
                table: "teams_admin_consent_sessions",
                column: "state_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_tenant_registrations_company_id",
                table: "teams_tenant_registrations",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_tenant_registrations_entra_tenant_id",
                table: "teams_tenant_registrations",
                column: "entra_tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_tenant_registrations_status_updated_at",
                table: "teams_tenant_registrations",
                columns: new[] { "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teams_admin_consent_sessions");

            migrationBuilder.DropTable(
                name: "teams_tenant_registrations");
        }
    }
}
