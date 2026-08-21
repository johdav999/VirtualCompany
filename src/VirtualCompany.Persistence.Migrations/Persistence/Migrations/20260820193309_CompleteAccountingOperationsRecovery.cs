using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAccountingOperationsRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bank_statement_import_rows_companies_company_id",
                table: "bank_statement_import_rows");

            migrationBuilder.CreateTable(
                name: "accounting_migration_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    phase = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    scanned_count = table.Column<int>(type: "int", nullable: false),
                    updated_count = table.Column<int>(type: "int", nullable: false),
                    conflict_count = table.Column<int>(type: "int", nullable: false),
                    report_count = table.Column<int>(type: "int", nullable: false),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    requested_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_migration_runs", x => x.id);
                    table.UniqueConstraint("AK_accounting_migration_runs_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_migration_runs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_cutover_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    migration_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    opening_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_debit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_credit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    receivables_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    payables_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    bank_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    suspense_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_fact_line_count = table.Column<int>(type: "int", nullable: false),
                    provider_reference_count = table.Column<int>(type: "int", nullable: false),
                    evidence_link_count = table.Column<int>(type: "int", nullable: false),
                    snapshot_count = table.Column<int>(type: "int", nullable: false),
                    issue_count = table.Column<int>(type: "int", nullable: false),
                    details_json = table.Column<string>(type: "nvarchar(max)", maxLength: 32000, nullable: false),
                    checksum = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    generated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_cutover_reports", x => x.id);
                    table.UniqueConstraint("AK_accounting_cutover_reports_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_cutover_reports_accounting_migration_runs_company_id_migration_run_id",
                        columns: x => new { x.company_id, x.migration_run_id },
                        principalTable: "accounting_migration_runs",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_cutover_reports_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_cutover_reports_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_migration_conflicts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    migration_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    operator_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    resolution_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_migration_conflicts", x => x.id);
                    table.UniqueConstraint("AK_accounting_migration_conflicts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_migration_conflicts_accounting_migration_runs_company_id_migration_run_id",
                        columns: x => new { x.company_id, x.migration_run_id },
                        principalTable: "accounting_migration_runs",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_migration_conflicts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_migration_conflicts_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_cutover_reports_company_id_fiscal_period_id_generated_utc",
                table: "accounting_cutover_reports",
                columns: new[] { "company_id", "fiscal_period_id", "generated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_cutover_reports_company_id_migration_run_id_fiscal_period_id",
                table: "accounting_cutover_reports",
                columns: new[] { "company_id", "migration_run_id", "fiscal_period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_migration_conflicts_company_id_fiscal_period_id",
                table: "accounting_migration_conflicts",
                columns: new[] { "company_id", "fiscal_period_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_migration_conflicts_company_id_migration_run_id_entity_type_entity_id_reason_code",
                table: "accounting_migration_conflicts",
                columns: new[] { "company_id", "migration_run_id", "entity_type", "entity_id", "reason_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_migration_conflicts_company_id_status_updated_utc",
                table: "accounting_migration_conflicts",
                columns: new[] { "company_id", "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_migration_runs_company_id_idempotency_key",
                table: "accounting_migration_runs",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_migration_runs_company_id_target_version",
                table: "accounting_migration_runs",
                columns: new[] { "company_id", "target_version" },
                unique: true,
                filter: "status IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_migration_runs_company_id_target_version_status",
                table: "accounting_migration_runs",
                columns: new[] { "company_id", "target_version", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_migration_runs_status_lease_expires_utc_requested_utc",
                table: "accounting_migration_runs",
                columns: new[] { "status", "lease_expires_utc", "requested_utc" });

            migrationBuilder.AddForeignKey(
                name: "FK_bank_statement_import_rows_companies_company_id",
                table: "bank_statement_import_rows",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bank_statement_import_rows_companies_company_id",
                table: "bank_statement_import_rows");

            migrationBuilder.DropTable(
                name: "accounting_cutover_reports");

            migrationBuilder.DropTable(
                name: "accounting_migration_conflicts");

            migrationBuilder.DropTable(
                name: "accounting_migration_runs");

            migrationBuilder.AddForeignKey(
                name: "FK_bank_statement_import_rows_companies_company_id",
                table: "bank_statement_import_rows",
                column: "company_id",
                principalTable: "companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
