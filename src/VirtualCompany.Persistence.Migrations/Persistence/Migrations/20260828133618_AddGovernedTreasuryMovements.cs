using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernedTreasuryMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_bank_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    adjustment_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterpart_finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    materiality_threshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correction_of_adjustment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    posted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_bank_adjustments", x => x.id);
                    table.UniqueConstraint("AK_finance_bank_adjustments_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_bank_adjustments_kind", "adjustment_kind IN ('bank_fee', 'interest_income', 'interest_expense')");
                    table.CheckConstraint("CK_finance_bank_adjustments_status", "status IN ('needs_review', 'awaiting_bank_evidence', 'in_transit', 'awaiting_approval', 'ready_to_post', 'posted', 'reversed')");
                    table.ForeignKey(
                        name: "FK_finance_bank_adjustments_bank_transactions_company_id_bank_transaction_id",
                        columns: x => new { x.company_id, x.bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_bank_adjustments_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_bank_adjustments_company_bank_accounts_company_id_bank_account_id",
                        columns: x => new { x.company_id, x.bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_bank_adjustments_finance_accounts_company_id_counterpart_finance_account_id",
                        columns: x => new { x.company_id, x.counterpart_finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_bank_adjustments_finance_bank_adjustments_company_id_correction_of_adjustment_id",
                        columns: x => new { x.company_id, x.correction_of_adjustment_id },
                        principalTable: "finance_bank_adjustments",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "finance_card_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    provider_batch_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    control_finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    gross_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    fee_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    materiality_threshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correction_of_settlement_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    posted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_card_settlements", x => x.id);
                    table.UniqueConstraint("AK_finance_card_settlements_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_card_settlements_status", "status IN ('needs_review', 'awaiting_bank_evidence', 'in_transit', 'awaiting_approval', 'ready_to_post', 'posted', 'reversed')");
                    table.ForeignKey(
                        name: "FK_finance_card_settlements_bank_transactions_company_id_bank_transaction_id",
                        columns: x => new { x.company_id, x.bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_card_settlements_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_card_settlements_company_bank_accounts_company_id_bank_account_id",
                        columns: x => new { x.company_id, x.bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_card_settlements_finance_accounts_company_id_control_finance_account_id",
                        columns: x => new { x.company_id, x.control_finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_card_settlements_finance_card_settlements_company_id_correction_of_settlement_id",
                        columns: x => new { x.company_id, x.correction_of_settlement_id },
                        principalTable: "finance_card_settlements",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "finance_payout_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    provider_batch_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    control_finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    gross_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    fee_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    materiality_threshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correction_of_settlement_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    posted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_payout_settlements", x => x.id);
                    table.UniqueConstraint("AK_finance_payout_settlements_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_payout_settlements_status", "status IN ('needs_review', 'awaiting_bank_evidence', 'in_transit', 'awaiting_approval', 'ready_to_post', 'posted', 'reversed')");
                    table.ForeignKey(
                        name: "FK_finance_payout_settlements_bank_transactions_company_id_bank_transaction_id",
                        columns: x => new { x.company_id, x.bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_payout_settlements_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_payout_settlements_company_bank_accounts_company_id_bank_account_id",
                        columns: x => new { x.company_id, x.bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_payout_settlements_finance_accounts_company_id_control_finance_account_id",
                        columns: x => new { x.company_id, x.control_finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_payout_settlements_finance_payout_settlements_company_id_correction_of_settlement_id",
                        columns: x => new { x.company_id, x.correction_of_settlement_id },
                        principalTable: "finance_payout_settlements",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "finance_treasury_source_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_treasury_source_events", x => x.id);
                    table.CheckConstraint("CK_finance_treasury_source_events_type", "source_type IN ('account_transfer', 'bank_adjustment', 'card_settlement', 'payout_settlement')");
                    table.ForeignKey(
                        name: "FK_finance_treasury_source_events_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_treasury_source_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    evidence_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    content_hash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_treasury_source_evidence", x => x.id);
                    table.CheckConstraint("CK_finance_treasury_source_evidence_type", "source_type IN ('account_transfer', 'bank_adjustment', 'card_settlement', 'payout_settlement')");
                    table.ForeignKey(
                        name: "FK_finance_treasury_source_evidence_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_treasury_source_ledger_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    link_role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_treasury_source_ledger_links", x => x.id);
                    table.CheckConstraint("CK_finance_treasury_source_ledger_links_type", "source_type IN ('account_transfer', 'bank_adjustment', 'card_settlement', 'payout_settlement')");
                    table.ForeignKey(
                        name: "FK_finance_treasury_source_ledger_links_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_treasury_source_ledger_links_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "finance_treasury_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_identity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    from_bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    to_bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    fee_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    fee_finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    materiality_threshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    outbound_bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    inbound_bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correction_of_transfer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    posted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_treasury_transfers", x => x.id);
                    table.UniqueConstraint("AK_finance_treasury_transfers_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_treasury_transfers_status", "status IN ('needs_review', 'awaiting_bank_evidence', 'in_transit', 'awaiting_approval', 'ready_to_post', 'posted', 'reversed')");
                    table.ForeignKey(
                        name: "FK_finance_treasury_transfers_bank_transactions_company_id_inbound_bank_transaction_id",
                        columns: x => new { x.company_id, x.inbound_bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_treasury_transfers_bank_transactions_company_id_outbound_bank_transaction_id",
                        columns: x => new { x.company_id, x.outbound_bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_treasury_transfers_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_treasury_transfers_company_bank_accounts_company_id_from_bank_account_id",
                        columns: x => new { x.company_id, x.from_bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_treasury_transfers_company_bank_accounts_company_id_to_bank_account_id",
                        columns: x => new { x.company_id, x.to_bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_treasury_transfers_finance_accounts_company_id_fee_finance_account_id",
                        columns: x => new { x.company_id, x.fee_finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_finance_treasury_transfers_finance_treasury_transfers_company_id_correction_of_transfer_id",
                        columns: x => new { x.company_id, x.correction_of_transfer_id },
                        principalTable: "finance_treasury_transfers",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bank_adjustments_company_id_bank_account_id",
                table: "finance_bank_adjustments",
                columns: new[] { "company_id", "bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bank_adjustments_company_id_bank_transaction_id",
                table: "finance_bank_adjustments",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_bank_adjustments_company_id_correction_of_adjustment_id",
                table: "finance_bank_adjustments",
                columns: new[] { "company_id", "correction_of_adjustment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bank_adjustments_company_id_counterpart_finance_account_id",
                table: "finance_bank_adjustments",
                columns: new[] { "company_id", "counterpart_finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bank_adjustments_company_id_source_identity",
                table: "finance_bank_adjustments",
                columns: new[] { "company_id", "source_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_bank_adjustments_company_id_status_updated_at",
                table: "finance_bank_adjustments",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_card_settlements_company_id_bank_account_id",
                table: "finance_card_settlements",
                columns: new[] { "company_id", "bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_card_settlements_company_id_bank_transaction_id",
                table: "finance_card_settlements",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true,
                filter: "[bank_transaction_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_finance_card_settlements_company_id_control_finance_account_id",
                table: "finance_card_settlements",
                columns: new[] { "company_id", "control_finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_card_settlements_company_id_correction_of_settlement_id",
                table: "finance_card_settlements",
                columns: new[] { "company_id", "correction_of_settlement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_card_settlements_company_id_source_identity",
                table: "finance_card_settlements",
                columns: new[] { "company_id", "source_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_card_settlements_company_id_status_updated_at",
                table: "finance_card_settlements",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_payout_settlements_company_id_bank_account_id",
                table: "finance_payout_settlements",
                columns: new[] { "company_id", "bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_payout_settlements_company_id_bank_transaction_id",
                table: "finance_payout_settlements",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true,
                filter: "[bank_transaction_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_finance_payout_settlements_company_id_control_finance_account_id",
                table: "finance_payout_settlements",
                columns: new[] { "company_id", "control_finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_payout_settlements_company_id_correction_of_settlement_id",
                table: "finance_payout_settlements",
                columns: new[] { "company_id", "correction_of_settlement_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_payout_settlements_company_id_source_identity",
                table: "finance_payout_settlements",
                columns: new[] { "company_id", "source_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_payout_settlements_company_id_status_updated_at",
                table: "finance_payout_settlements",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_source_events_company_id_source_type_source_id_created_at",
                table: "finance_treasury_source_events",
                columns: new[] { "company_id", "source_type", "source_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_source_evidence_company_id_content_hash",
                table: "finance_treasury_source_evidence",
                columns: new[] { "company_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_source_evidence_company_id_source_type_source_id_evidence_type",
                table: "finance_treasury_source_evidence",
                columns: new[] { "company_id", "source_type", "source_id", "evidence_type" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_source_ledger_links_company_id_ledger_entry_id",
                table: "finance_treasury_source_ledger_links",
                columns: new[] { "company_id", "ledger_entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_source_ledger_links_company_id_source_type_source_id_link_role",
                table: "finance_treasury_source_ledger_links",
                columns: new[] { "company_id", "source_type", "source_id", "link_role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_correction_of_transfer_id",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "correction_of_transfer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_fee_finance_account_id",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "fee_finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_from_bank_account_id",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "from_bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_inbound_bank_transaction_id",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "inbound_bank_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_outbound_bank_transaction_id",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "outbound_bank_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_source_identity",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "source_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_status_updated_at",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_treasury_transfers_company_id_to_bank_account_id",
                table: "finance_treasury_transfers",
                columns: new[] { "company_id", "to_bank_account_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_bank_adjustments");

            migrationBuilder.DropTable(
                name: "finance_card_settlements");

            migrationBuilder.DropTable(
                name: "finance_payout_settlements");

            migrationBuilder.DropTable(
                name: "finance_treasury_source_events");

            migrationBuilder.DropTable(
                name: "finance_treasury_source_evidence");

            migrationBuilder.DropTable(
                name: "finance_treasury_source_ledger_links");

            migrationBuilder.DropTable(
                name: "finance_treasury_transfers");
        }
    }
}
