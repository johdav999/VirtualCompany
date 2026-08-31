using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodEndCurrencyRevaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_manual_journal_draft_lines_company_id_id",
                table: "manual_journal_draft_lines",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ledger_entry_lines_company_id_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_account_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    monetary_class = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_account_policies", x => x.id);
                    table.CheckConstraint("CK_currency_revaluation_account_class", "monetary_class IN ('cash','receivable','payable','other')");
                    table.ForeignKey(
                        name: "FK_currency_revaluation_account_policies_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_number = table.Column<int>(type: "int", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    functional_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    voucher_series_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    request_identity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    failure_reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    population_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    rate_set_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    proposal_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    population_count = table.Column<int>(type: "int", nullable: false),
                    included_count = table.Column<int>(type: "int", nullable: false),
                    excluded_count = table.Column<int>(type: "int", nullable: false),
                    review_count = table.Column<int>(type: "int", nullable: false),
                    document_balance_total = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    carrying_functional_total = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    revalued_functional_total = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    proposed_adjustment_total = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reversal_ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    superseded_by_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_scheduled = table.Column<bool>(type: "bit", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    posted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reversed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    submitted_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    posted_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reversed_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_runs", x => x.id);
                    table.UniqueConstraint("AK_currency_revaluation_runs_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_currency_revaluation_runs_counts", "population_count >= 0 AND included_count >= 0 AND excluded_count >= 0 AND review_count >= 0");
                    table.CheckConstraint("CK_currency_revaluation_runs_status", "status IN ('draft','needs_review','awaiting_approval','posted','reversed','superseded','failed')");
                    table.ForeignKey(
                        name: "FK_currency_revaluation_runs_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_runs_currency_revaluation_runs_company_id_superseded_by_run_id",
                        columns: x => new { x.company_id, x.superseded_by_run_id },
                        principalTable: "currency_revaluation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_runs_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_runs_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_runs_ledger_entries_company_id_reversal_ledger_entry_id",
                        columns: x => new { x.company_id, x.reversal_ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    days_before_period_end = table.Column<int>(type: "int", nullable: false),
                    automatic_reversal = table.Column<bool>(type: "bit", nullable: false),
                    voucher_series_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_evaluated_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_schedules", x => x.id);
                    table.CheckConstraint("CK_currency_revaluation_schedule_days", "days_before_period_end >= 0 AND days_before_period_end <= 31");
                    table.ForeignKey(
                        name: "FK_currency_revaluation_schedules_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_population_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    population_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    monetary_class = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    account_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    normal_balance = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    document_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    functional_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    document_balance = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    carrying_functional_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    revalued_functional_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    adjustment_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    exchange_rate_conversion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    period_end_rate = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    review_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_population_items", x => x.id);
                    table.UniqueConstraint("AK_currency_revaluation_population_items_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_currency_revaluation_population_status", "status IN ('included','excluded','needs_review')");
                    table.ForeignKey(
                        name: "FK_currency_revaluation_population_items_currency_revaluation_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "currency_revaluation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_population_items_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                        columns: x => new { x.company_id, x.exchange_rate_conversion_id },
                        principalTable: "exchange_rate_conversions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_population_items_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_reconciliations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reconciliation_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    population_count = table.Column<int>(type: "int", nullable: false),
                    carrying_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    revalued_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    proposed_adjustment = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    proposal_line_adjustment = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    difference = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    is_reconciled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_reconciliations", x => x.id);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_reconciliations_currency_revaluation_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "currency_revaluation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_proposal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    population_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    line_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    debit_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    credit_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_proposal_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_proposal_lines_currency_revaluation_population_items_company_id_population_item_id",
                        columns: x => new { x.company_id, x.population_item_id },
                        principalTable: "currency_revaluation_population_items",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_currency_revaluation_proposal_lines_currency_revaluation_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "currency_revaluation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_proposal_lines_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_rate_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    population_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    exchange_rate_conversion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    functional_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    effective_rate = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate_set_identity = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    observation_identity = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_rate_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_rate_bindings_currency_revaluation_population_items_company_id_population_item_id",
                        columns: x => new { x.company_id, x.population_item_id },
                        principalTable: "currency_revaluation_population_items",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_currency_revaluation_rate_bindings_currency_revaluation_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "currency_revaluation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_rate_bindings_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                        columns: x => new { x.company_id, x.exchange_rate_conversion_id },
                        principalTable: "exchange_rate_conversions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currency_revaluation_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    population_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    evidence_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    occurred_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currency_revaluation_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_currency_revaluation_reviews_currency_revaluation_population_items_company_id_population_item_id",
                        columns: x => new { x.company_id, x.population_item_id },
                        principalTable: "currency_revaluation_population_items",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_currency_revaluation_reviews_currency_revaluation_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "currency_revaluation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_account_policies_company_id_finance_account_id",
                table: "currency_revaluation_account_policies",
                columns: new[] { "company_id", "finance_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_population_items_company_id_exchange_rate_conversion_id",
                table: "currency_revaluation_population_items",
                columns: new[] { "company_id", "exchange_rate_conversion_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_population_items_company_id_finance_account_id_document_currency",
                table: "currency_revaluation_population_items",
                columns: new[] { "company_id", "finance_account_id", "document_currency" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_population_items_company_id_run_id_population_key",
                table: "currency_revaluation_population_items",
                columns: new[] { "company_id", "run_id", "population_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_population_items_company_id_status",
                table: "currency_revaluation_population_items",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_proposal_lines_company_id_finance_account_id",
                table: "currency_revaluation_proposal_lines",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_proposal_lines_company_id_population_item_id",
                table: "currency_revaluation_proposal_lines",
                columns: new[] { "company_id", "population_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_proposal_lines_company_id_run_id_sequence",
                table: "currency_revaluation_proposal_lines",
                columns: new[] { "company_id", "run_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_rate_bindings_company_id_exchange_rate_conversion_id",
                table: "currency_revaluation_rate_bindings",
                columns: new[] { "company_id", "exchange_rate_conversion_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_rate_bindings_company_id_population_item_id",
                table: "currency_revaluation_rate_bindings",
                columns: new[] { "company_id", "population_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_rate_bindings_company_id_run_id_population_item_id",
                table: "currency_revaluation_rate_bindings",
                columns: new[] { "company_id", "run_id", "population_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_reconciliations_company_id_run_id_reconciliation_type",
                table: "currency_revaluation_reconciliations",
                columns: new[] { "company_id", "run_id", "reconciliation_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_reviews_company_id_population_item_id",
                table: "currency_revaluation_reviews",
                columns: new[] { "company_id", "population_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_reviews_company_id_run_id_occurred_utc",
                table: "currency_revaluation_reviews",
                columns: new[] { "company_id", "run_id", "occurred_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_runs_company_id_approval_request_id",
                table: "currency_revaluation_runs",
                columns: new[] { "company_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_runs_company_id_fiscal_period_id_run_number",
                table: "currency_revaluation_runs",
                columns: new[] { "company_id", "fiscal_period_id", "run_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_runs_company_id_fiscal_period_id_status_updated_utc",
                table: "currency_revaluation_runs",
                columns: new[] { "company_id", "fiscal_period_id", "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_runs_company_id_ledger_entry_id",
                table: "currency_revaluation_runs",
                columns: new[] { "company_id", "ledger_entry_id" },
                unique: true,
                filter: "[ledger_entry_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_runs_company_id_request_identity",
                table: "currency_revaluation_runs",
                columns: new[] { "company_id", "request_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_runs_company_id_reversal_ledger_entry_id",
                table: "currency_revaluation_runs",
                columns: new[] { "company_id", "reversal_ledger_entry_id" },
                unique: true,
                filter: "[reversal_ledger_entry_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_runs_company_id_superseded_by_run_id",
                table: "currency_revaluation_runs",
                columns: new[] { "company_id", "superseded_by_run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_revaluation_schedules_company_id",
                table: "currency_revaluation_schedules",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "currency_revaluation_account_policies");

            migrationBuilder.DropTable(
                name: "currency_revaluation_proposal_lines");

            migrationBuilder.DropTable(
                name: "currency_revaluation_rate_bindings");

            migrationBuilder.DropTable(
                name: "currency_revaluation_reconciliations");

            migrationBuilder.DropTable(
                name: "currency_revaluation_reviews");

            migrationBuilder.DropTable(
                name: "currency_revaluation_schedules");

            migrationBuilder.DropTable(
                name: "currency_revaluation_population_items");

            migrationBuilder.DropTable(
                name: "currency_revaluation_runs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_manual_journal_draft_lines_company_id_id",
                table: "manual_journal_draft_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ledger_entry_lines_company_id_id",
                table: "ledger_entry_lines");
        }
    }
}
