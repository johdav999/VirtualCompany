using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceDomainSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    account_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    opening_balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    opened_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_accounts", x => x.id);
                    table.UniqueConstraint("AK_finance_accounts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_accounts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_counterparties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    counterparty_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_counterparties", x => x.id);
                    table.UniqueConstraint("AK_finance_counterparties_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_counterparties_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_policy_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    invoice_approval_threshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    bill_approval_threshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    require_counterparty_for_transactions = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_policy_configurations", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_policy_configurations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    as_of_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_balances", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_balances_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_balances_finance_accounts_company_id_account_id",
                        columns: x => new { x.company_id, x.account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_bills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_bills", x => x.id);
                    table.UniqueConstraint("AK_finance_bills_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_bills_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_bills_finance_counterparties_company_id_counterparty_id",
                        columns: x => new { x.company_id, x.counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    due_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_invoices", x => x.id);
                    table.UniqueConstraint("AK_finance_invoices_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_finance_invoices_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_invoices_finance_counterparties_company_id_counterparty_id",
                        columns: x => new { x.company_id, x.counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    transaction_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    transaction_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    external_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_transactions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_transactions_finance_accounts_company_id_account_id",
                        columns: x => new { x.company_id, x.account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_transactions_finance_bills_company_id_bill_id",
                        columns: x => new { x.company_id, x.bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_transactions_finance_counterparties_company_id_counterparty_id",
                        columns: x => new { x.company_id, x.counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_transactions_finance_invoices_company_id_invoice_id",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "finance_invoices",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_finance_accounts_company_id_account_type", table: "finance_accounts", columns: new[] { "company_id", "account_type" });
            migrationBuilder.CreateIndex(name: "IX_finance_accounts_company_id_code", table: "finance_accounts", columns: new[] { "company_id", "code" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_finance_balances_company_id_account_id_as_of_at", table: "finance_balances", columns: new[] { "company_id", "account_id", "as_of_at" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_finance_bills_company_id_bill_number", table: "finance_bills", columns: new[] { "company_id", "bill_number" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_finance_bills_company_id_status_due_at", table: "finance_bills", columns: new[] { "company_id", "status", "due_at" });
            migrationBuilder.CreateIndex(name: "IX_finance_counterparties_company_id_counterparty_type", table: "finance_counterparties", columns: new[] { "company_id", "counterparty_type" });
            migrationBuilder.CreateIndex(name: "IX_finance_counterparties_company_id_name", table: "finance_counterparties", columns: new[] { "company_id", "name" });
            migrationBuilder.CreateIndex(name: "IX_finance_invoices_company_id_invoice_number", table: "finance_invoices", columns: new[] { "company_id", "invoice_number" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_finance_invoices_company_id_status_due_at", table: "finance_invoices", columns: new[] { "company_id", "status", "due_at" });
            migrationBuilder.CreateIndex(name: "IX_finance_policy_configurations_company_id", table: "finance_policy_configurations", column: "company_id", unique: true);
            migrationBuilder.CreateIndex(name: "IX_finance_transactions_company_id_account_id_transaction_at", table: "finance_transactions", columns: new[] { "company_id", "account_id", "transaction_at" });
            migrationBuilder.CreateIndex(name: "IX_finance_transactions_company_id_bill_id", table: "finance_transactions", columns: new[] { "company_id", "bill_id" });
            migrationBuilder.CreateIndex(name: "IX_finance_transactions_company_id_counterparty_id_transaction_at", table: "finance_transactions", columns: new[] { "company_id", "counterparty_id", "transaction_at" });
            migrationBuilder.CreateIndex(name: "IX_finance_transactions_company_id_external_reference", table: "finance_transactions", columns: new[] { "company_id", "external_reference" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_finance_transactions_company_id_invoice_id", table: "finance_transactions", columns: new[] { "company_id", "invoice_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "finance_balances");
            migrationBuilder.DropTable(name: "finance_policy_configurations");
            migrationBuilder.DropTable(name: "finance_transactions");
            migrationBuilder.DropTable(name: "finance_accounts");
            migrationBuilder.DropTable(name: "finance_bills");
            migrationBuilder.DropTable(name: "finance_invoices");
            migrationBuilder.DropTable(name: "finance_counterparties");
        }
    }
}
