using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodAccountingAuthorityAndProviderExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_authority_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    authority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    target_authority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    change_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    opening_balances_reconciled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    trial_balance_reconciled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    source_mappings_reconciled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    conflict_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    validation_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    completed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_authority_periods", x => x.id);
                    table.UniqueConstraint("AK_accounting_authority_periods_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_accounting_authority_periods_authority", "[authority] IN ('internal_ledger', 'external_provider', 'migration')");
                    table.CheckConstraint("CK_accounting_authority_periods_conflicts", "[conflict_count] >= 0");
                    table.CheckConstraint("CK_accounting_authority_periods_dates", "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
                    table.CheckConstraint("CK_accounting_authority_periods_target_authority", "[target_authority] IS NULL OR [target_authority] IN ('internal_ledger', 'external_provider')");
                    table.ForeignKey(
                        name: "FK_accounting_authority_periods_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [accounting_authority_periods]
                    ([id], [company_id], [effective_from], [effective_to], [authority], [target_authority], [provider_key],
                     [change_reason], [opening_balances_reconciled], [trial_balance_reconciled], [source_mappings_reconciled],
                     [conflict_count], [validation_summary], [changed_by_user_id], [completed_by_user_id], [created_utc],
                     [updated_utc], [completed_utc], [version])
                SELECT NEWID(), [company_id], [policy_pack_effective_from], NULL, [authority], NULL, NULL,
                       'Initial period authority backfilled from the accounting configuration.', 0, 0, 0,
                       0, NULL, [updated_by_user_id], NULL, [created_utc], [updated_utc], NULL, 1
                FROM [accounting_configurations];
                """);

            migrationBuilder.CreateTable(
                name: "accounting_provider_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    authority_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    stable_identity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    write_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    failure_category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    provider_external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reconciled_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reconciled_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_exports", x => x.id);
                    table.CheckConstraint("CK_accounting_provider_exports_attempt_count", "[attempt_count] >= 0");
                    table.CheckConstraint("CK_accounting_provider_exports_status", "[status] IN ('awaiting_approval', 'approved', 'executing', 'exported', 'failed', 'reconciliation_required', 'cancelled')");
                    table.ForeignKey(
                        name: "FK_accounting_provider_exports_accounting_authority_periods_company_id_authority_period_id",
                        columns: x => new { x.company_id, x.authority_period_id },
                        principalTable: "accounting_authority_periods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_provider_exports_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_exports_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_authority_periods_company_id_authority_effective_to",
                table: "accounting_authority_periods",
                columns: new[] { "company_id", "authority", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_authority_periods_company_id_effective_from",
                table: "accounting_authority_periods",
                columns: new[] { "company_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_authority_periods_company_id_provider_key_effective_from",
                table: "accounting_authority_periods",
                columns: new[] { "company_id", "provider_key", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_exports_company_id_authority_period_id",
                table: "accounting_provider_exports",
                columns: new[] { "company_id", "authority_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_exports_company_id_ledger_entry_id_provider_key_action",
                table: "accounting_provider_exports",
                columns: new[] { "company_id", "ledger_entry_id", "provider_key", "action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_exports_company_id_provider_key_status_updated_utc",
                table: "accounting_provider_exports",
                columns: new[] { "company_id", "provider_key", "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_exports_company_id_stable_identity",
                table: "accounting_provider_exports",
                columns: new[] { "company_id", "stable_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_exports_company_id_write_request_id",
                table: "accounting_provider_exports",
                columns: new[] { "company_id", "write_request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_exports");

            migrationBuilder.DropTable(
                name: "accounting_authority_periods");
        }
    }
}
