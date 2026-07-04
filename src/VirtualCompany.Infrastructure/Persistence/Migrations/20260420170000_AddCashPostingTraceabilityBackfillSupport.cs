using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260420170000_AddCashPostingTraceabilityBackfillSupport")]
    public partial class AddCashPostingTraceabilityBackfillSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var intType = isPostgres ? "integer" : "int";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string500Type = isPostgres ? "character varying(500)" : "nvarchar(500)";
            var string512Type = isPostgres ? "character varying(512)" : "nvarchar(512)";
            var companyPrincipalColumn = isPostgres ? "id" : "Id";

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: guidType, nullable: false),
                    entry_number = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    entry_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    description = table.Column<string>(type: string500Type, maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.UniqueConstraint("AK_ledger_entries_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_ledger_entries_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: companyPrincipalColumn,
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ledger_entries_finance_fiscal_periods_company_id_fiscal_period_id",
                        columns: x => new { x.company_id, x.fiscal_period_id },
                        principalTable: "finance_fiscal_periods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_fiscal_period_id_entry_at",
                table: "ledger_entries",
                columns: new[] { "company_id", "fiscal_period_id", "entry_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_entry_number",
                table: "ledger_entries",
                columns: new[] { "company_id", "entry_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_status_entry_at",
                table: "ledger_entries",
                columns: new[] { "company_id", "status", "entry_at" });

            migrationBuilder.CreateTable(
                name: "ledger_entry_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: guidType, nullable: false),
                    finance_account_id = table.Column<Guid>(type: guidType, nullable: false),
                    debit_amount = table.Column<decimal>(type: decimalType, nullable: false),
                    credit_amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: isPostgres ? "character varying(3)" : "nvarchar(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: string500Type, maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entry_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_ledger_entry_lines_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: companyPrincipalColumn,
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ledger_entry_lines_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_entry_lines_ledger_entries_company_id_ledger_entry_id",
                        columns: x => new { x.company_id, x.ledger_entry_id },
                        principalTable: "ledger_entries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_lines_company_id_finance_account_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_lines_company_id_ledger_entry_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "ledger_entry_id" });

            migrationBuilder.CreateTable(
                name: "bank_transaction_posting_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    bank_transaction_id = table.Column<Guid>(type: guidType, nullable: false),
                    matching_status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    posting_state = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    linked_payment_count = table.Column<int>(type: intType, nullable: false),
                    last_evaluated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    unmatched_reason = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                    conflict_code = table.Column<string>(type: string64Type, maxLength: 64, nullable: true),
                    conflict_details = table.Column<string>(type: string512Type, maxLength: 512, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_transaction_posting_states", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_transaction_posting_states_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: companyPrincipalColumn,
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_cash_ledger_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    payment_id = table.Column<Guid>(type: guidType, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: guidType, nullable: false),
                    source_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: string128Type, maxLength: 128, nullable: false),
                    posted_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_cash_ledger_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_cash_ledger_links_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: companyPrincipalColumn,
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_cash_ledger_links_ledger_entries_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_posting_states_company_id_bank_transaction_id",
                table: "bank_transaction_posting_states",
                columns: new[] { "company_id", "bank_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_transaction_posting_states_company_id_matching_status_posting_state",
                table: "bank_transaction_posting_states",
                columns: new[] { "company_id", "matching_status", "posting_state" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_cash_ledger_links_company_id_payment_id_ledger_entry_id",
                table: "payment_cash_ledger_links",
                columns: new[] { "company_id", "payment_id", "ledger_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_cash_ledger_links_company_id_payment_id_source_type_source_id_posted_at",
                table: "payment_cash_ledger_links",
                columns: new[] { "company_id", "payment_id", "source_type", "source_id", "posted_at" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_transaction_posting_states");

            migrationBuilder.DropTable(
                name: "payment_cash_ledger_links");

            migrationBuilder.DropTable(
                name: "ledger_entry_lines");

            migrationBuilder.DropTable(
                name: "ledger_entries");
        }
    }
}
