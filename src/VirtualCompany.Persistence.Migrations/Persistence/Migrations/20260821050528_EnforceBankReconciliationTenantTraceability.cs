using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceBankReconciliationTenantTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_bank_transactions_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_ledger_entries_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_payment_links_bank_transactions_bank_transaction_id",
                table: "bank_transaction_payment_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_payment_links_finance_payments_payment_id",
                table: "bank_transaction_payment_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_posting_states_bank_transactions_bank_transaction_id",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropForeignKey(
                name: "FK_company_bank_accounts_finance_accounts_finance_account_id",
                table: "company_bank_accounts");

            // AddFinancePayments created this relationship with the legacy principal name
            // "payments". Databases rebuilt from a newer model snapshot can instead have
            // the conventional "finance_payments" name, so accept either upgrade path.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE [name] = N'FK_payment_cash_ledger_links_finance_payments_payment_id'
                      AND [parent_object_id] = OBJECT_ID(N'[payment_cash_ledger_links]'))
                BEGIN
                    ALTER TABLE [payment_cash_ledger_links]
                        DROP CONSTRAINT [FK_payment_cash_ledger_links_finance_payments_payment_id];
                END
                ELSE IF EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE [name] = N'FK_payment_cash_ledger_links_payments_payment_id'
                      AND [parent_object_id] = OBJECT_ID(N'[payment_cash_ledger_links]'))
                BEGIN
                    ALTER TABLE [payment_cash_ledger_links]
                        DROP CONSTRAINT [FK_payment_cash_ledger_links_payments_payment_id];
                END
                ELSE
                BEGIN
                    THROW 51000, 'The payment cash-ledger payment foreign key was not found.', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_payment_cash_ledger_links_ledger_entries_ledger_entry_id",
                table: "payment_cash_ledger_links");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_payment_cash_ledger_links_ledger_entry_id] ON [payment_cash_ledger_links];");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_payment_cash_ledger_links_payment_id] ON [payment_cash_ledger_links];");

            migrationBuilder.DropIndex(
                name: "IX_company_bank_accounts_finance_account_id",
                table: "company_bank_accounts");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_bank_transaction_posting_states_bank_transaction_id] ON [bank_transaction_posting_states];");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_payment_links_bank_transaction_id",
                table: "bank_transaction_payment_links");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_payment_links_payment_id",
                table: "bank_transaction_payment_links");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_cash_ledger_links_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_cash_ledger_links_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.CreateIndex(
                name: "IX_payment_cash_ledger_links_company_id_ledger_entry_id",
                table: "payment_cash_ledger_links",
                columns: new[] { "company_id", "ledger_entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_bank_accounts_company_id_finance_account_id",
                table: "company_bank_accounts",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links",
                columns: new[] { "company_id", "bank_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links",
                columns: new[] { "company_id", "bank_transaction_id", "ledger_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links",
                columns: new[] { "company_id", "ledger_entry_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_bank_transactions_company_id_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links",
                columns: new[] { "company_id", "bank_transaction_id" },
                principalTable: "bank_transactions",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_ledger_entries_company_id_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links",
                columns: new[] { "company_id", "ledger_entry_id" },
                principalTable: "ledger_entries",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_payment_links_bank_transactions_company_id_bank_transaction_id",
                table: "bank_transaction_payment_links",
                columns: new[] { "company_id", "bank_transaction_id" },
                principalTable: "bank_transactions",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_payment_links_finance_payments_company_id_payment_id",
                table: "bank_transaction_payment_links",
                columns: new[] { "company_id", "payment_id" },
                principalTable: "finance_payments",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_posting_states_bank_transactions_company_id_bank_transaction_id",
                table: "bank_transaction_posting_states",
                columns: new[] { "company_id", "bank_transaction_id" },
                principalTable: "bank_transactions",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_company_bank_accounts_finance_accounts_company_id_finance_account_id",
                table: "company_bank_accounts",
                columns: new[] { "company_id", "finance_account_id" },
                principalTable: "finance_accounts",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_cash_ledger_links_finance_payments_company_id_payment_id",
                table: "payment_cash_ledger_links",
                columns: new[] { "company_id", "payment_id" },
                principalTable: "finance_payments",
                principalColumns: new[] { "company_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_cash_ledger_links_ledger_entries_company_id_ledger_entry_id",
                table: "payment_cash_ledger_links",
                columns: new[] { "company_id", "ledger_entry_id" },
                principalTable: "ledger_entries",
                principalColumns: new[] { "company_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_bank_transactions_company_id_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_ledger_entries_company_id_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_payment_links_bank_transactions_company_id_bank_transaction_id",
                table: "bank_transaction_payment_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_payment_links_finance_payments_company_id_payment_id",
                table: "bank_transaction_payment_links");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transaction_posting_states_bank_transactions_company_id_bank_transaction_id",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropForeignKey(
                name: "FK_company_bank_accounts_finance_accounts_company_id_finance_account_id",
                table: "company_bank_accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_cash_ledger_links_finance_payments_company_id_payment_id",
                table: "payment_cash_ledger_links");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_cash_ledger_links_ledger_entries_company_id_ledger_entry_id",
                table: "payment_cash_ledger_links");

            migrationBuilder.DropIndex(
                name: "IX_payment_cash_ledger_links_company_id_ledger_entry_id",
                table: "payment_cash_ledger_links");

            migrationBuilder.DropIndex(
                name: "IX_company_bank_accounts_company_id_finance_account_id",
                table: "company_bank_accounts");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.DropIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links");

            migrationBuilder.CreateIndex(
                name: "IX_payment_cash_ledger_links_ledger_entry_id",
                table: "payment_cash_ledger_links",
                column: "ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_cash_ledger_links_payment_id",
                table: "payment_cash_ledger_links",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_bank_accounts_finance_account_id",
                table: "company_bank_accounts",
                column: "finance_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_posting_states_bank_transaction_id",
                table: "bank_transaction_posting_states",
                column: "bank_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_payment_links_bank_transaction_id",
                table: "bank_transaction_payment_links",
                column: "bank_transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_payment_links_payment_id",
                table: "bank_transaction_payment_links",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_cash_ledger_links_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links",
                column: "bank_transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_cash_ledger_links_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links",
                column: "ledger_entry_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_bank_transactions_bank_transaction_id",
                table: "bank_transaction_cash_ledger_links",
                column: "bank_transaction_id",
                principalTable: "bank_transactions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_cash_ledger_links_ledger_entries_ledger_entry_id",
                table: "bank_transaction_cash_ledger_links",
                column: "ledger_entry_id",
                principalTable: "ledger_entries",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_payment_links_bank_transactions_bank_transaction_id",
                table: "bank_transaction_payment_links",
                column: "bank_transaction_id",
                principalTable: "bank_transactions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_payment_links_finance_payments_payment_id",
                table: "bank_transaction_payment_links",
                column: "payment_id",
                principalTable: "finance_payments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transaction_posting_states_bank_transactions_bank_transaction_id",
                table: "bank_transaction_posting_states",
                column: "bank_transaction_id",
                principalTable: "bank_transactions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_company_bank_accounts_finance_accounts_finance_account_id",
                table: "company_bank_accounts",
                column: "finance_account_id",
                principalTable: "finance_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_cash_ledger_links_finance_payments_payment_id",
                table: "payment_cash_ledger_links",
                column: "payment_id",
                principalTable: "finance_payments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_cash_ledger_links_ledger_entries_ledger_entry_id",
                table: "payment_cash_ledger_links",
                column: "ledger_entry_id",
                principalTable: "ledger_entries",
                principalColumn: "id");
        }
    }
}
