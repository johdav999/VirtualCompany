using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementNativeLedgerKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_approval_requests_CompanyId_Id",
                table: "approval_requests",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.AddColumn<string>(
                name: "dimension_facts_json",
                table: "ledger_entry_lines",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_facts_json",
                table: "ledger_entry_lines",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approval_request_id",
                table: "ledger_entries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "base_currency",
                table: "ledger_entries",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_reason",
                table: "ledger_entries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "document_date",
                table: "ledger_entries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "ledger_entries",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "original_ledger_entry_id",
                table: "ledger_entries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_facts_json",
                table: "ledger_entries",
                type: "nvarchar(max)",
                maxLength: 16000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_pack_key",
                table: "ledger_entries",
                type: "nvarchar(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_pack_version",
                table: "ledger_entries",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "posted_by_user_id",
                table: "ledger_entries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "posting_date",
                table: "ledger_entries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "posting_type",
                table: "ledger_entries",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "ledger_entries",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "source_version",
                table: "ledger_entries",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voucher_fiscal_year",
                table: "ledger_entries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "voucher_sequence_number",
                table: "ledger_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voucher_series_id",
                table: "ledger_entries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "account_class",
                table: "finance_accounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "control_account_role",
                table: "finance_accounts",
                type: "nvarchar(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                table: "finance_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_to",
                table: "finance_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_posting_enabled",
                table: "finance_accounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "normal_balance",
                table: "finance_accounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "restrict_manual_posting",
                table: "finance_accounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "accounting_voucher_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    number_prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_voucher_series", x => x.id);
                    table.UniqueConstraint("AK_accounting_voucher_series_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_voucher_series_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ledger_posting_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_posting_identities", x => x.id);
                    table.ForeignKey(
                        name: "FK_ledger_posting_identities_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ledger_posting_identities_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_voucher_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    voucher_series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    last_allocated_number = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_voucher_sequences", x => x.id);
                    table.UniqueConstraint("AK_accounting_voucher_sequences_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_voucher_sequences_accounting_voucher_series_company_id_voucher_series_id",
                        columns: x => new { x.company_id, x.voucher_series_id },
                        principalTable: "accounting_voucher_series",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_voucher_sequences_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_idempotency_key",
                table: "ledger_entries",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_original_ledger_entry_id",
                table: "ledger_entries",
                columns: new[] { "company_id", "original_ledger_entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id_source_version_posting_type",
                table: "ledger_entries",
                columns: new[] { "company_id", "source_type", "source_id", "source_version", "posting_type" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_voucher_series_id_voucher_fiscal_year_voucher_sequence_number",
                table: "ledger_entries",
                columns: new[] { "company_id", "voucher_series_id", "voucher_fiscal_year", "voucher_sequence_number" },
                unique: true,
                filter: "voucher_series_id IS NOT NULL AND voucher_fiscal_year IS NOT NULL AND voucher_sequence_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_finance_accounts_company_id_account_class_is_posting_enabled",
                table: "finance_accounts",
                columns: new[] { "company_id", "account_class", "is_posting_enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_voucher_sequences_company_id_voucher_series_id_fiscal_year",
                table: "accounting_voucher_sequences",
                columns: new[] { "company_id", "voucher_series_id", "fiscal_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_voucher_series_company_id_code",
                table: "accounting_voucher_series",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_posting_identities_company_id_action_source_type_source_id_source_version",
                table: "ledger_posting_identities",
                columns: new[] { "company_id", "action", "source_type", "source_id", "source_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_posting_identities_company_id_idempotency_key",
                table: "ledger_posting_identities",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_posting_identities_company_id_ledger_entry_id",
                table: "ledger_posting_identities",
                columns: new[] { "company_id", "ledger_entry_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_accounting_voucher_series_company_id_voucher_series_id",
                table: "ledger_entries",
                columns: new[] { "company_id", "voucher_series_id" },
                principalTable: "accounting_voucher_series",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_approval_requests_company_id_approval_request_id",
                table: "ledger_entries",
                columns: new[] { "company_id", "approval_request_id" },
                principalTable: "approval_requests",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_ledger_entries_company_id_original_ledger_entry_id",
                table: "ledger_entries",
                columns: new[] { "company_id", "original_ledger_entry_id" },
                principalTable: "ledger_entries",
                principalColumns: new[] { "company_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entries_approval_requests_company_id_approval_request_id",
                table: "ledger_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entries_accounting_voucher_series_company_id_voucher_series_id",
                table: "ledger_entries");

            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entries_ledger_entries_company_id_original_ledger_entry_id",
                table: "ledger_entries");

            migrationBuilder.DropTable(
                name: "accounting_voucher_sequences");

            migrationBuilder.DropTable(
                name: "ledger_posting_identities");

            migrationBuilder.DropTable(
                name: "accounting_voucher_series");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_idempotency_key",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_original_ledger_entry_id",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_source_type_source_id_source_version_posting_type",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_voucher_series_id_voucher_fiscal_year_voucher_sequence_number",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_finance_accounts_company_id_account_class_is_posting_enabled",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "dimension_facts_json",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "tax_facts_json",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "approval_request_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "base_currency",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "correction_reason",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "document_date",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "original_ledger_entry_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "policy_facts_json",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "policy_pack_key",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "policy_pack_version",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "posted_by_user_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "posting_date",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "posting_type",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "voucher_fiscal_year",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "voucher_sequence_number",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "voucher_series_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "account_class",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "control_account_role",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "effective_from",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "effective_to",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "is_posting_enabled",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "normal_balance",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "restrict_manual_posting",
                table: "finance_accounts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_approval_requests_CompanyId_Id",
                table: "approval_requests");
        }
    }
}
