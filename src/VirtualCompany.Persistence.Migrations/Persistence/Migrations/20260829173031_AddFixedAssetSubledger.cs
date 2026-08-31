using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssetSubledger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fixed_asset_classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    book_method = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    useful_life_months = table.Column<int>(type: "int", nullable: false),
                    default_residual_percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    cost_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accumulated_depreciation_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    depreciation_expense_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accumulated_impairment_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    impairment_expense_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disposal_gain_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disposal_loss_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    voucher_series_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    definition_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_asset_classes", x => x.id);
                    table.UniqueConstraint("AK_fixed_asset_classes_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_finance_accounts_company_id_accumulated_depreciation_account_id",
                        columns: x => new { x.company_id, x.accumulated_depreciation_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_finance_accounts_company_id_accumulated_impairment_account_id",
                        columns: x => new { x.company_id, x.accumulated_impairment_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_finance_accounts_company_id_cost_account_id",
                        columns: x => new { x.company_id, x.cost_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_finance_accounts_company_id_depreciation_expense_account_id",
                        columns: x => new { x.company_id, x.depreciation_expense_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_finance_accounts_company_id_disposal_gain_account_id",
                        columns: x => new { x.company_id, x.disposal_gain_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_finance_accounts_company_id_disposal_loss_account_id",
                        columns: x => new { x.company_id, x.disposal_loss_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_classes_finance_accounts_company_id_impairment_expense_account_id",
                        columns: x => new { x.company_id, x.impairment_expense_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixed_asset_depreciation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    population_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    posted_item_count = table.Column<int>(type: "int", nullable: false),
                    exception_count = table.Column<int>(type: "int", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_asset_depreciation_runs", x => x.id);
                    table.UniqueConstraint("AK_fixed_asset_depreciation_runs_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_fixed_asset_depreciation_runs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fixed_asset_migration_conflicts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_finance_asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    legacy_snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_asset_migration_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "FK_fixed_asset_migration_conflicts_finance_assets_company_id_legacy_finance_asset_id",
                        columns: x => new { x.company_id, x.legacy_finance_asset_id },
                        principalTable: "finance_assets",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fixed_asset_register_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_class_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_class_version = table.Column<long>(type: "bigint", nullable: false),
                    asset_class_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    asset_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    acquisition_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    improvement_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    residual_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    accumulated_depreciation = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    accumulated_impairment = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    disposal_proceeds = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    disposal_gain_loss = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    useful_life_months = table.Column<int>(type: "int", nullable: false),
                    book_method = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    acquisition_date = table.Column<DateOnly>(type: "date", nullable: false),
                    capitalization_date = table.Column<DateOnly>(type: "date", nullable: true),
                    placed_in_service_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_depreciation_through = table.Column<DateOnly>(type: "date", nullable: true),
                    disposal_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    legacy_finance_asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    custodian = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    location = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    dimension_snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_asset_register_items", x => x.id);
                    table.UniqueConstraint("AK_fixed_asset_register_items_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_fixed_asset_register_items_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fixed_asset_register_items_finance_assets_company_id_legacy_finance_asset_id",
                        columns: x => new { x.company_id, x.legacy_finance_asset_id },
                        principalTable: "finance_assets",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_register_items_fixed_asset_classes_company_id_asset_class_id",
                        columns: x => new { x.company_id, x.asset_class_id },
                        principalTable: "fixed_asset_classes",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_register_items_knowledge_documents_company_id_source_document_id",
                        columns: x => new { x.company_id, x.source_document_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixed_asset_book_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    cost_movement = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    depreciation_movement = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    impairment_movement = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    proceeds = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    gain_loss = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    snapshot_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    depreciation_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    original_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    component_allocation_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_asset_book_events", x => x.id);
                    table.UniqueConstraint("AK_fixed_asset_book_events_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_fixed_asset_book_events_fixed_asset_book_events_company_id_original_event_id",
                        columns: x => new { x.company_id, x.original_event_id },
                        principalTable: "fixed_asset_book_events",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fixed_asset_book_events_fixed_asset_register_items_company_id_asset_id",
                        columns: x => new { x.company_id, x.asset_id },
                        principalTable: "fixed_asset_register_items",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fixed_asset_components",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    residual_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    accumulated_depreciation = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    useful_life_months = table.Column<int>(type: "int", nullable: false),
                    placed_in_service_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_asset_components", x => x.id);
                    table.UniqueConstraint("AK_fixed_asset_components_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_fixed_asset_components_fixed_asset_register_items_company_id_asset_id",
                        columns: x => new { x.company_id, x.asset_id },
                        principalTable: "fixed_asset_register_items",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fixed_asset_depreciation_run_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_version = table.Column<long>(type: "bigint", nullable: false),
                    asset_class_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    opening_cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    opening_accumulated_depreciation = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    opening_accumulated_impairment = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    residual_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    calculation_explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_asset_depreciation_run_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_fixed_asset_depreciation_run_items_fixed_asset_depreciation_runs_company_id_run_id",
                        columns: x => new { x.company_id, x.run_id },
                        principalTable: "fixed_asset_depreciation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fixed_asset_depreciation_run_items_fixed_asset_register_items_company_id_asset_id",
                        columns: x => new { x.company_id, x.asset_id },
                        principalTable: "fixed_asset_register_items",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_book_events_company_id_asset_id_effective_date",
                table: "fixed_asset_book_events",
                columns: new[] { "company_id", "asset_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_book_events_company_id_idempotency_key",
                table: "fixed_asset_book_events",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_book_events_company_id_ledger_entry_id",
                table: "fixed_asset_book_events",
                columns: new[] { "company_id", "ledger_entry_id" },
                unique: true,
                filter: "ledger_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_book_events_company_id_original_event_id",
                table: "fixed_asset_book_events",
                columns: new[] { "company_id", "original_event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_accumulated_depreciation_account_id",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "accumulated_depreciation_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_accumulated_impairment_account_id",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "accumulated_impairment_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_code",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_cost_account_id",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "cost_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_depreciation_expense_account_id",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "depreciation_expense_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_disposal_gain_account_id",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "disposal_gain_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_disposal_loss_account_id",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "disposal_loss_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_impairment_expense_account_id",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "impairment_expense_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_classes_company_id_is_active",
                table: "fixed_asset_classes",
                columns: new[] { "company_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_components_company_id_asset_id_code",
                table: "fixed_asset_components",
                columns: new[] { "company_id", "asset_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_depreciation_run_items_company_id_asset_id",
                table: "fixed_asset_depreciation_run_items",
                columns: new[] { "company_id", "asset_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_depreciation_run_items_company_id_run_id_asset_id",
                table: "fixed_asset_depreciation_run_items",
                columns: new[] { "company_id", "run_id", "asset_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_depreciation_runs_company_id_idempotency_key",
                table: "fixed_asset_depreciation_runs",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_depreciation_runs_company_id_period_start_period_end",
                table: "fixed_asset_depreciation_runs",
                columns: new[] { "company_id", "period_start", "period_end" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_migration_conflicts_company_id_legacy_finance_asset_id",
                table: "fixed_asset_migration_conflicts",
                columns: new[] { "company_id", "legacy_finance_asset_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_migration_conflicts_company_id_status",
                table: "fixed_asset_migration_conflicts",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_register_items_company_id_asset_class_id",
                table: "fixed_asset_register_items",
                columns: new[] { "company_id", "asset_class_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_register_items_company_id_asset_number",
                table: "fixed_asset_register_items",
                columns: new[] { "company_id", "asset_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_register_items_company_id_legacy_finance_asset_id",
                table: "fixed_asset_register_items",
                columns: new[] { "company_id", "legacy_finance_asset_id" },
                unique: true,
                filter: "legacy_finance_asset_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_register_items_company_id_source_document_id",
                table: "fixed_asset_register_items",
                columns: new[] { "company_id", "source_document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_register_items_company_id_source_type_source_id_source_version",
                table: "fixed_asset_register_items",
                columns: new[] { "company_id", "source_type", "source_id", "source_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_asset_register_items_company_id_status_asset_class_id",
                table: "fixed_asset_register_items",
                columns: new[] { "company_id", "status", "asset_class_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fixed_asset_book_events");

            migrationBuilder.DropTable(
                name: "fixed_asset_components");

            migrationBuilder.DropTable(
                name: "fixed_asset_depreciation_run_items");

            migrationBuilder.DropTable(
                name: "fixed_asset_migration_conflicts");

            migrationBuilder.DropTable(
                name: "fixed_asset_depreciation_runs");

            migrationBuilder.DropTable(
                name: "fixed_asset_register_items");

            migrationBuilder.DropTable(
                name: "fixed_asset_classes");
        }
    }
}
