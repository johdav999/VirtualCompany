using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddCashPostingTraceabilityBackfillSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var intType = isPostgres ? "integer" : "int";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string512Type = isPostgres ? "character varying(512)" : "nvarchar(512)";

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
                        name: "FK_bank_transaction_posting_states_bank_transactions_bank_transaction_id",
                        column: x => x.bank_transaction_id,
                        principalTable: "bank_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_transaction_posting_states_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
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
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_cash_ledger_links_ledger_entries_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payment_cash_ledger_links_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "finance_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
        }
    }
}