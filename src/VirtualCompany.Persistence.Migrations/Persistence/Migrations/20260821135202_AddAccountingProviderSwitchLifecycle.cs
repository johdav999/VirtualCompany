using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_provider_switches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    source_provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    target_kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    target_provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    effective_fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    migration_strategy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    responsible_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    responsible_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    blocked_from_status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cancelled_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status_changed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    blocked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switches", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switches_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switches_cancellation", "([status] = 'cancelled' AND [cancelled_at] IS NOT NULL AND [cancelled_by_user_id] IS NOT NULL AND [cancellation_reason] IS NOT NULL) OR ([status] <> 'cancelled' AND [cancelled_at] IS NULL AND [cancelled_by_user_id] IS NULL AND [cancellation_reason] IS NULL)");
                    table.CheckConstraint("CK_accounting_provider_switches_distinct_endpoints", "NOT ([source_kind] = [target_kind] AND COALESCE([source_provider_key], '') = COALESCE([target_provider_key], ''))");
                    table.CheckConstraint("CK_accounting_provider_switches_source_endpoint", "([source_kind] = 'internal' AND [source_provider_key] IS NULL) OR ([source_kind] = 'external' AND [source_provider_key] IS NOT NULL)");
                    table.CheckConstraint("CK_accounting_provider_switches_status", "[status] IN ('draft', 'assessing', 'ready_for_planning', 'plan_awaiting_approval', 'preparing_target', 'rehearsal_passed', 'scheduled', 'source_frozen', 'reconciling', 'activation_awaiting_approval', 'target_authoritative', 'monitoring', 'completed', 'blocked', 'cancelled', 'recovery')");
                    table.CheckConstraint("CK_accounting_provider_switches_strategy", "[migration_strategy] IN ('opening_balances_and_open_items', 'current_fiscal_year', 'full_history')");
                    table.CheckConstraint("CK_accounting_provider_switches_target_endpoint", "([target_kind] = 'internal' AND [target_provider_key] IS NULL) OR ([target_kind] = 'external' AND [target_provider_key] IS NOT NULL)");
                    table.CheckConstraint("CK_accounting_provider_switches_version", "[version] > 0");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switches_agents_company_id_responsible_agent_id",
                        columns: x => new { x.company_id, x.responsible_agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switches_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switches_finance_fiscal_periods_effective_fiscal_period_id",
                        column: x => x.effective_fiscal_period_id,
                        principalTable: "finance_fiscal_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switches_company_id_correlation_id",
                table: "accounting_provider_switches",
                columns: new[] { "company_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switches_company_id_effective_fiscal_period_id",
                table: "accounting_provider_switches",
                columns: new[] { "company_id", "effective_fiscal_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switches_company_id_responsible_agent_id",
                table: "accounting_provider_switches",
                columns: new[] { "company_id", "responsible_agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switches_company_id_status_updated_at",
                table: "accounting_provider_switches",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switches_effective_fiscal_period_id",
                table: "accounting_provider_switches",
                column: "effective_fiscal_period_id");

            migrationBuilder.CreateIndex(
                name: "UX_accounting_provider_switches_company_active",
                table: "accounting_provider_switches",
                column: "company_id",
                unique: true,
                filter: "[status] <> 'completed' AND [status] <> 'cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switches");
        }
    }
}
