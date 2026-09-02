using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementFinanceAutonomyGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_autonomy_controls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    scope_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    capability_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_controls", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_controls_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    capability_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    latest_version_number = table.Column<int>(type: "int", nullable: false),
                    active_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_grants", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_grants_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_grants_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_autonomy_grants_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_autonomy_grant_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    level = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    allowed_triggers_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    allowed_action_classes_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    allowed_tools_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    maximum_records_per_run = table.Column<int>(type: "int", nullable: false),
                    maximum_amount_per_run = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    maximum_actions_per_run = table.Column<int>(type: "int", nullable: false),
                    schedule_expression = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    timezone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    window_start_local = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    window_end_local = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    evidence_freshness_minutes = table.Column<int>(type: "int", nullable: false),
                    confirmation_behavior = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    escalation_route = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    catalogue_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    capability_policy_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    authority_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    authority_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    review_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    reviewed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    activated_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    revocation_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_autonomy_grant_versions", x => x.id);
                    table.UniqueConstraint("AK_finance_autonomy_grant_versions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_autonomy_grant_versions_finance_autonomy_grants_grant_id",
                        column: x => x.grant_id,
                        principalTable: "finance_autonomy_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_controls_company_id_scope_key",
                table: "finance_autonomy_controls",
                columns: new[] { "company_id", "scope_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_controls_company_id_state",
                table: "finance_autonomy_controls",
                columns: new[] { "company_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_grant_versions_company_id_grant_id_version_number",
                table: "finance_autonomy_grant_versions",
                columns: new[] { "company_id", "grant_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_grant_versions_company_id_status_expires_utc",
                table: "finance_autonomy_grant_versions",
                columns: new[] { "company_id", "status", "expires_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_grant_versions_grant_id",
                table: "finance_autonomy_grant_versions",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_grants_agent_id",
                table: "finance_autonomy_grants",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_autonomy_grants_company_id_agent_id_capability_id",
                table: "finance_autonomy_grants",
                columns: new[] { "company_id", "agent_id", "capability_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_autonomy_controls");

            migrationBuilder.DropTable(
                name: "finance_autonomy_grant_versions");

            migrationBuilder.DropTable(
                name: "finance_autonomy_grants");
        }
    }
}
