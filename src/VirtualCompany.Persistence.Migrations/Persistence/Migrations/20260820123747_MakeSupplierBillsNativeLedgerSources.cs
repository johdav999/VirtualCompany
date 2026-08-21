using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeSupplierBillsNativeLedgerSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_bill_accounting_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    voucher_series_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    document_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    base_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    recoverable_tax_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    non_recoverable_tax_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    gross_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    cost_base_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    recoverable_tax_base_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    gross_base_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    rounding_base_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    payable_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tax_treatment = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    policy_pack_key = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    policy_pack_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    policy_definition_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_document_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    original_bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    blocking_reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    blocking_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_bill_accounting_profiles", x => x.id);
                    table.UniqueConstraint("AK_supplier_bill_accounting_profiles_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_supplier_bill_accounting_amounts", "net_amount >= 0 AND recoverable_tax_amount >= 0 AND non_recoverable_tax_amount >= 0 AND gross_amount > 0 AND gross_base_amount > 0");
                    table.CheckConstraint("CK_supplier_bill_accounting_exchange_rate", "exchange_rate > 0");
                    table.CheckConstraint("CK_supplier_bill_accounting_status", "status IN ('not_ready','awaiting_approval','ready_to_post','posted','reversed','blocked')");
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_profiles_approval_requests_company_id_approval_request_id",
                        columns: x => new { x.company_id, x.approval_request_id },
                        principalTable: "approval_requests",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_profiles_finance_accounts_company_id_payable_account_id",
                        columns: x => new { x.company_id, x.payable_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_profiles_finance_bills_company_id_bill_id",
                        columns: x => new { x.company_id, x.bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_profiles_finance_bills_company_id_original_bill_id",
                        columns: x => new { x.company_id, x.original_bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_profiles_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_bill_accounting_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    cost_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tax_rule_key = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    tax_method = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tax_treatment = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tax_rate = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    net_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    recoverable_tax_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    non_recoverable_tax_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    gross_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    cost_base_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    recoverable_tax_base_amount = table.Column<decimal>(type: "decimal(19,6)", nullable: false),
                    recoverable_tax_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_bill_accounting_lines", x => x.id);
                    table.UniqueConstraint("AK_supplier_bill_accounting_lines_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_supplier_bill_accounting_line_amounts", "net_amount >= 0 AND tax_amount >= 0 AND recoverable_tax_amount >= 0 AND non_recoverable_tax_amount >= 0 AND gross_amount > 0");
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_lines_finance_accounts_company_id_cost_account_id",
                        columns: x => new { x.company_id, x.cost_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_lines_finance_accounts_company_id_recoverable_tax_account_id",
                        columns: x => new { x.company_id, x.recoverable_tax_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_bill_accounting_lines_supplier_bill_accounting_profiles_company_id_profile_id",
                        columns: x => new { x.company_id, x.profile_id },
                        principalTable: "supplier_bill_accounting_profiles",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_lines_company_id_cost_account_id",
                table: "supplier_bill_accounting_lines",
                columns: new[] { "company_id", "cost_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_lines_company_id_profile_id_sequence",
                table: "supplier_bill_accounting_lines",
                columns: new[] { "company_id", "profile_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_lines_company_id_recoverable_tax_account_id",
                table: "supplier_bill_accounting_lines",
                columns: new[] { "company_id", "recoverable_tax_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_approval_request_id",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "approval_request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_bill_id",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "bill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_ledger_entry_id",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "ledger_entry_id" },
                unique: true,
                filter: "[ledger_entry_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_original_bill_id",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "original_bill_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_payable_account_id",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "payable_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_status_updated_utc",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "status", "updated_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_bill_accounting_lines");

            migrationBuilder.DropTable(
                name: "supplier_bill_accounting_profiles");
        }
    }
}
