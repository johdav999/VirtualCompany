using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddBankTransactionsAndMappings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string3Type = isPostgres ? "character varying(3)" : "nvarchar(3)";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string120Type = isPostgres ? "character varying(120)" : "nvarchar(120)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string160Type = isPostgres ? "character varying(160)" : "nvarchar(160)";
            var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
            var string240Type = isPostgres ? "character varying(240)" : "nvarchar(240)";

            migrationBuilder.CreateTable(
                name: "company_bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    finance_account_id = table.Column<Guid>(type: guidType, nullable: false),
                    display_name = table.Column<string>(type: string120Type, maxLength: 120, nullable: false),
                    bank_name = table.Column<string>(type: string120Type, maxLength: 120, nullable: false),
                    masked_account_number = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false),
                    external_code = table.Column<string>(type: string64Type, maxLength: 64, nullable: true),
                    is_primary = table.Column<bool>(nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_bank_accounts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_bank_accounts_finance_accounts_finance_account_id",
                        column: x => x.finance_account_id,
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    bank_account_id = table.Column<Guid>(type: guidType, nullable: false),
                    booking_date = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    value_date = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false),
                    reference_text = table.Column<string>(type: string240Type, maxLength: 240, nullable: false),
                    counterparty = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                    status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    reconciled_amount = table.Column<decimal>(type: decimalType, nullable: false),
                    external_reference = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                    import_source = table.Column<string>(type: string64Type, maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transactions", x => x.id);
                    table.CheckConstraint("CK_bank_transactions_status", "status IN ('unreconciled', 'partially_reconciled', 'reconciled')");
                    table.CheckConstraint("CK_bank_transactions_reconciled_amount", "reconciled_amount >= 0 AND reconciled_amount <= ABS(amount)");
                    table.ForeignKey(
                        name: "FK_bank_transactions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_transactions_company_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "company_bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_transaction_payment_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: guidType, nullable: false),
                    payment_id = table.Column<Guid>(type: guidType, nullable: false),
                    allocated_amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transaction_payment_links", x => x.id);
                    table.CheckConstraint("CK_bank_transaction_payment_links_allocated_amount", "allocated_amount > 0");
                    table.ForeignKey(
                        name: "FK_bank_transaction_payment_links_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_transaction_payment_links_bank_transactions_bank_transaction_id",
                        column: x => x.bank_transaction_id,
                        principalTable: "bank_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_transaction_payment_links_finance_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "finance_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_transaction_cash_ledger_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: guidType, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: guidType, nullable: false),
                    idempotency_key = table.Column<string>(type: string160Type, maxLength: 160, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transaction_cash_ledger_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_transaction_cash_ledger_links_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_transaction_cash_ledger_links_bank_transactions_bank_transaction_id",
                        column: x => x.bank_transaction_id,
                        principalTable: "bank_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_transaction_cash_ledger_links_ledger_entries_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_company_bank_accounts_company_id", table: "company_bank_accounts", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_company_bank_accounts_company_id_display_name", table: "company_bank_accounts", columns: new[] { "company_id", "display_name" });
            migrationBuilder.CreateIndex(name: "IX_company_bank_accounts_company_id_external_code", table: "company_bank_accounts", columns: new[] { "company_id", "external_code" }, unique: true, filter: isPostgres ? "\"external_code\" IS NOT NULL" : "[external_code] IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_company_bank_accounts_finance_account_id", table: "company_bank_accounts", column: "finance_account_id");

            migrationBuilder.CreateIndex(name: "IX_bank_transactions_company_id", table: "bank_transactions", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_bank_transactions_company_id_bank_account_id_booking_date", table: "bank_transactions", columns: new[] { "company_id", "bank_account_id", "booking_date" });
            migrationBuilder.CreateIndex(name: "IX_bank_transactions_company_id_status_booking_date", table: "bank_transactions", columns: new[] { "company_id", "status", "booking_date" });
            migrationBuilder.CreateIndex(name: "IX_bank_transactions_company_id_booking_date", table: "bank_transactions", columns: new[] { "company_id", "booking_date" });
            migrationBuilder.CreateIndex(name: "IX_bank_transactions_company_id_amount", table: "bank_transactions", columns: new[] { "company_id", "amount" });
            migrationBuilder.CreateIndex(name: "IX_bank_transactions_company_id_external_reference", table: "bank_transactions", columns: new[] { "company_id", "external_reference" }, unique: true, filter: isPostgres ? "\"external_reference\" IS NOT NULL" : "[external_reference] IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_bank_transactions_bank_account_id", table: "bank_transactions", column: "bank_account_id");

            migrationBuilder.CreateIndex(name: "IX_bank_transaction_payment_links_company_id_bank_transaction_id", table: "bank_transaction_payment_links", columns: new[] { "company_id", "bank_transaction_id" });
            migrationBuilder.CreateIndex(name: "IX_bank_transaction_payment_links_company_id_payment_id", table: "bank_transaction_payment_links", columns: new[] { "company_id", "payment_id" });
            migrationBuilder.CreateIndex(name: "IX_bank_transaction_payment_links_company_id_bank_transaction_id_payment_id", table: "bank_transaction_payment_links", columns: new[] { "company_id", "bank_transaction_id", "payment_id" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_bank_transaction_payment_links_bank_transaction_id", table: "bank_transaction_payment_links", column: "bank_transaction_id");
            migrationBuilder.CreateIndex(name: "IX_bank_transaction_payment_links_payment_id", table: "bank_transaction_payment_links", column: "payment_id");

            migrationBuilder.CreateIndex(name: "IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id", table: "bank_transaction_cash_ledger_links", columns: new[] { "company_id", "bank_transaction_id" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_bank_transaction_cash_ledger_links_company_id_idempotency_key", table: "bank_transaction_cash_ledger_links", columns: new[] { "company_id", "idempotency_key" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_bank_transaction_cash_ledger_links_bank_transaction_id", table: "bank_transaction_cash_ledger_links", column: "bank_transaction_id");
            migrationBuilder.CreateIndex(name: "IX_bank_transaction_cash_ledger_links_ledger_entry_id", table: "bank_transaction_cash_ledger_links", column: "ledger_entry_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "bank_transaction_cash_ledger_links");
            migrationBuilder.DropTable(name: "bank_transaction_payment_links");
            migrationBuilder.DropTable(name: "bank_transactions");
            migrationBuilder.DropTable(name: "company_bank_accounts");
        }
    }
}