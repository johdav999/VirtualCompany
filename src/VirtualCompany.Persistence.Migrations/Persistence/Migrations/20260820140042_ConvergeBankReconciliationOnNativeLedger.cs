using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvergeBankReconciliationOnNativeLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_bank_transaction_posting_states_posting_state",
                table: "bank_transaction_posting_states");

            migrationBuilder.AddCheckConstraint(
                name: "CK_bank_transaction_posting_states_posting_state",
                table: "bank_transaction_posting_states",
                sql: "posting_state IN ('pending', 'posted', 'skipped_unmatched', 'conflict', 'suspense', 'corrected')");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transactions_company_bank_accounts_bank_account_id",
                table: "bank_transactions");

            migrationBuilder.DropIndex(
                name: "IX_bank_transactions_bank_account_id",
                table: "bank_transactions");

            migrationBuilder.AddColumn<string>(
                name: "row_content_hash",
                table: "bank_transactions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "row_identity",
                table: "bank_transactions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_version",
                table: "bank_transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "handling_mode",
                table: "bank_transaction_posting_states",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reclassified_ledger_entry_id",
                table: "bank_transaction_posting_states",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_reason",
                table: "bank_transaction_posting_states",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "bank_transaction_posting_states",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_user_id",
                table: "bank_transaction_posting_states",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_version",
                table: "bank_transaction_posting_states",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "suspense_ledger_entry_id",
                table: "bank_transaction_posting_states",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_company_bank_accounts_company_id_id",
                table: "company_bank_accounts",
                columns: new[] { "company_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_bank_transactions_company_id_id",
                table: "bank_transactions",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateTable(
                name: "bank_reconciliation_follow_ups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolution_ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_reconciliation_follow_ups", x => x.id);
                    table.CheckConstraint("CK_bank_reconciliation_follow_ups_status", "status IN ('open', 'resolved')");
                    table.ForeignKey(
                        name: "FK_bank_reconciliation_follow_ups_bank_transactions_company_id_bank_transaction_id",
                        columns: x => new { x.company_id, x.bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_bank_reconciliation_follow_ups_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_reconciliation_follow_ups_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    statement_identity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    imported_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    imported_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_statement_imports", x => x.id);
                    table.UniqueConstraint("AK_bank_statement_imports_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_statement_imports_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_statement_imports_company_bank_accounts_company_id_bank_account_id",
                        columns: x => new { x.company_id, x.bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_import_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_statement_import_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_identity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    row_content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_statement_import_rows", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_rows_bank_statement_imports_company_id_bank_statement_import_id",
                        columns: x => new { x.company_id, x.bank_statement_import_id },
                        principalTable: "bank_statement_imports",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_rows_bank_transactions_company_id_bank_transaction_id",
                        columns: x => new { x.company_id, x.bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_rows_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bank_transactions_company_id_bank_account_id_import_source_row_identity",
                table: "bank_transactions",
                columns: new[] { "company_id", "bank_account_id", "import_source", "row_identity" },
                unique: true,
                filter: "import_source IS NOT NULL AND row_identity IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_bank_reconciliation_follow_ups_company_id_bank_transaction_id",
                table: "bank_reconciliation_follow_ups",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "IX_bank_reconciliation_follow_ups_company_id_bank_transaction_id_status",
                table: "bank_reconciliation_follow_ups",
                columns: new[] { "company_id", "bank_transaction_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_reconciliation_follow_ups_company_id_ledger_entry_id",
                table: "bank_reconciliation_follow_ups",
                columns: new[] { "company_id", "ledger_entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_rows_company_id_bank_statement_import_id_row_identity",
                table: "bank_statement_import_rows",
                columns: new[] { "company_id", "bank_statement_import_id", "row_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_rows_company_id_bank_transaction_id",
                table: "bank_statement_import_rows",
                columns: new[] { "company_id", "bank_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_imports_company_id_bank_account_id_source_key_statement_identity",
                table: "bank_statement_imports",
                columns: new[] { "company_id", "bank_account_id", "source_key", "statement_identity" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transactions_company_bank_accounts_company_id_bank_account_id",
                table: "bank_transactions",
                columns: new[] { "company_id", "bank_account_id" },
                principalTable: "company_bank_accounts",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_bank_transaction_posting_states_posting_state",
                table: "bank_transaction_posting_states");

            migrationBuilder.Sql("UPDATE bank_transaction_posting_states SET posting_state = 'posted' WHERE posting_state IN ('suspense', 'corrected');");

            migrationBuilder.AddCheckConstraint(
                name: "CK_bank_transaction_posting_states_posting_state",
                table: "bank_transaction_posting_states",
                sql: "posting_state IN ('pending', 'posted', 'skipped_unmatched', 'conflict')");

            migrationBuilder.DropForeignKey(
                name: "FK_bank_transactions_company_bank_accounts_company_id_bank_account_id",
                table: "bank_transactions");

            migrationBuilder.DropTable(
                name: "bank_reconciliation_follow_ups");

            migrationBuilder.DropTable(
                name: "bank_statement_import_rows");

            migrationBuilder.DropTable(
                name: "bank_statement_imports");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_company_bank_accounts_company_id_id",
                table: "company_bank_accounts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_bank_transactions_company_id_id",
                table: "bank_transactions");

            migrationBuilder.DropIndex(
                name: "IX_bank_transactions_company_id_bank_account_id_import_source_row_identity",
                table: "bank_transactions");

            migrationBuilder.DropColumn(
                name: "row_content_hash",
                table: "bank_transactions");

            migrationBuilder.DropColumn(
                name: "row_identity",
                table: "bank_transactions");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "bank_transactions");

            migrationBuilder.DropColumn(
                name: "handling_mode",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropColumn(
                name: "reclassified_ledger_entry_id",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropColumn(
                name: "review_reason",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "bank_transaction_posting_states");

            migrationBuilder.DropColumn(
                name: "suspense_ledger_entry_id",
                table: "bank_transaction_posting_states");

            migrationBuilder.CreateIndex(
                name: "IX_bank_transactions_bank_account_id",
                table: "bank_transactions",
                column: "bank_account_id");

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transactions_company_bank_accounts_bank_account_id",
                table: "bank_transactions",
                column: "bank_account_id",
                principalTable: "company_bank_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
