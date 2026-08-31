using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCurrencyAccountingFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "conversion_rounding_residual",
                table: "supplier_bill_accounting_profiles",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_provenance",
                table: "supplier_bill_accounting_profiles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "exchange_rate_conversion_id",
                table: "supplier_bill_accounting_profiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exchange_rate_date",
                table: "supplier_bill_accounting_profiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exchange_rate_identity",
                table: "supplier_bill_accounting_profiles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exchange_rate_purpose",
                table: "supplier_bill_accounting_profiles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "conversion_rounding_residual",
                table: "ledger_entry_lines",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "document_credit_amount",
                table: "ledger_entry_lines",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "document_currency",
                table: "ledger_entry_lines",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "document_debit_amount",
                table: "ledger_entry_lines",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "ledger_entry_lines",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exchange_rate_conversion_id",
                table: "ledger_entry_lines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exchange_rate_date",
                table: "ledger_entry_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exchange_rate_identity",
                table: "ledger_entry_lines",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_allocation_activity",
                table: "customer_statement_snapshots",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_closing_balance",
                table: "customer_statement_snapshots",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_credit_activity",
                table: "customer_statement_snapshots",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "functional_currency",
                table: "customer_statement_snapshots",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "functional_evidence_status",
                table: "customer_statement_snapshots",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "functional_invoice_activity",
                table: "customer_statement_snapshots",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_opening_balance",
                table: "customer_statement_snapshots",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_provenance",
                table: "customer_statement_items",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "customer_statement_items",
                type: "decimal(28,12)",
                precision: 28,
                scale: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exchange_rate_date",
                table: "customer_statement_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exchange_rate_identity",
                table: "customer_statement_items",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_credit_amount",
                table: "customer_statement_items",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "functional_currency",
                table: "customer_statement_items",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_debit_amount",
                table: "customer_statement_items",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_running_balance",
                table: "customer_statement_items",
                type: "decimal(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "conversion_rounding_residual",
                table: "customer_invoice_accounting_profiles",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_provenance",
                table: "customer_invoice_accounting_profiles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "exchange_rate_conversion_id",
                table: "customer_invoice_accounting_profiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exchange_rate_date",
                table: "customer_invoice_accounting_profiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exchange_rate_identity",
                table: "customer_invoice_accounting_profiles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exchange_rate_purpose",
                table: "customer_invoice_accounting_profiles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE line
                SET document_debit_amount = line.debit_amount,
                    document_credit_amount = line.credit_amount,
                    document_currency = UPPER(line.currency),
                    exchange_rate = 1,
                    exchange_rate_date = entry.document_date,
                    exchange_rate_identity = LOWER(CONCAT('identity:', line.currency, ':', CONVERT(char(10), entry.document_date, 23), ':1'))
                FROM ledger_entry_lines AS line
                INNER JOIN ledger_entries AS entry
                    ON entry.company_id = line.company_id AND entry.id = line.ledger_entry_id;

                UPDATE profile
                SET currency_provenance = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN 'base_currency_identity'
                        ELSE 'legacy_unverified_rate'
                    END,
                    exchange_rate_date = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN CAST(invoice.issued_at AS date)
                        ELSE NULL
                    END,
                    exchange_rate_purpose = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN 'transaction_date'
                        ELSE NULL
                    END,
                    exchange_rate_identity = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN LOWER(CONCAT('identity:', profile.document_currency, ':', CONVERT(char(10), CAST(invoice.issued_at AS date), 23), ':1'))
                        ELSE NULL
                    END,
                    conversion_rounding_residual = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN 0
                        ELSE NULL
                    END
                FROM customer_invoice_accounting_profiles AS profile
                INNER JOIN finance_invoices AS invoice
                    ON invoice.company_id = profile.company_id AND invoice.id = profile.invoice_id;

                UPDATE profile
                SET currency_provenance = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN 'base_currency_identity'
                        ELSE 'legacy_unverified_rate'
                    END,
                    exchange_rate_date = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN CAST(bill.received_at AS date)
                        ELSE NULL
                    END,
                    exchange_rate_purpose = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN 'transaction_date'
                        ELSE NULL
                    END,
                    exchange_rate_identity = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN LOWER(CONCAT('identity:', profile.document_currency, ':', CONVERT(char(10), CAST(bill.received_at AS date), 23), ':1'))
                        ELSE NULL
                    END,
                    conversion_rounding_residual = CASE
                        WHEN UPPER(profile.document_currency) = UPPER(profile.base_currency) AND profile.exchange_rate = 1
                            THEN 0
                        ELSE NULL
                    END
                FROM supplier_bill_accounting_profiles AS profile
                INNER JOIN finance_bills AS bill
                    ON bill.company_id = profile.company_id AND bill.id = profile.bill_id;

                UPDATE customer_statement_snapshots
                SET functional_evidence_status = 'legacy_unavailable';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_document_currency_exchange_rate_date",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "document_currency", "exchange_rate_date" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_exchange_rate_conversion_id",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "exchange_rate_conversion_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_lines_company_id_document_currency_exchange_rate_date",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "document_currency", "exchange_rate_date" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_lines_company_id_exchange_rate_conversion_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "exchange_rate_conversion_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ledger_entry_lines_document_amount",
                table: "ledger_entry_lines",
                sql: "CAST(document_debit_amount AS NUMERIC) >= 0 AND CAST(document_credit_amount AS NUMERIC) >= 0 AND ((CAST(document_debit_amount AS NUMERIC) > 0 AND NOT(CAST(document_credit_amount AS NUMERIC) > 0)) OR (CAST(document_credit_amount AS NUMERIC) > 0 AND NOT(CAST(document_debit_amount AS NUMERIC) > 0)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ledger_entry_lines_exchange_rate",
                table: "ledger_entry_lines",
                sql: "exchange_rate IS NULL OR exchange_rate > 0");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_accounting_profiles_company_id_document_currency_exchange_rate_date",
                table: "customer_invoice_accounting_profiles",
                columns: new[] { "company_id", "document_currency", "exchange_rate_date" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_accounting_profiles_company_id_exchange_rate_conversion_id",
                table: "customer_invoice_accounting_profiles",
                columns: new[] { "company_id", "exchange_rate_conversion_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_customer_invoice_accounting_profiles_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                table: "customer_invoice_accounting_profiles",
                columns: new[] { "company_id", "exchange_rate_conversion_id" },
                principalTable: "exchange_rate_conversions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entry_lines_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "exchange_rate_conversion_id" },
                principalTable: "exchange_rate_conversions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_bill_accounting_profiles_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                table: "supplier_bill_accounting_profiles",
                columns: new[] { "company_id", "exchange_rate_conversion_id" },
                principalTable: "exchange_rate_conversions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customer_invoice_accounting_profiles_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entry_lines_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                table: "ledger_entry_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_bill_accounting_profiles_exchange_rate_conversions_company_id_exchange_rate_conversion_id",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_document_currency_exchange_rate_date",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropIndex(
                name: "IX_supplier_bill_accounting_profiles_company_id_exchange_rate_conversion_id",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entry_lines_company_id_document_currency_exchange_rate_date",
                table: "ledger_entry_lines");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entry_lines_company_id_exchange_rate_conversion_id",
                table: "ledger_entry_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ledger_entry_lines_document_amount",
                table: "ledger_entry_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ledger_entry_lines_exchange_rate",
                table: "ledger_entry_lines");

            migrationBuilder.DropIndex(
                name: "IX_customer_invoice_accounting_profiles_company_id_document_currency_exchange_rate_date",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropIndex(
                name: "IX_customer_invoice_accounting_profiles_company_id_exchange_rate_conversion_id",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "conversion_rounding_residual",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "currency_provenance",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_conversion_id",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_date",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_identity",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_purpose",
                table: "supplier_bill_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "conversion_rounding_residual",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "document_credit_amount",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "document_currency",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "document_debit_amount",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "exchange_rate_conversion_id",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "exchange_rate_date",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "exchange_rate_identity",
                table: "ledger_entry_lines");

            migrationBuilder.DropColumn(
                name: "functional_allocation_activity",
                table: "customer_statement_snapshots");

            migrationBuilder.DropColumn(
                name: "functional_closing_balance",
                table: "customer_statement_snapshots");

            migrationBuilder.DropColumn(
                name: "functional_credit_activity",
                table: "customer_statement_snapshots");

            migrationBuilder.DropColumn(
                name: "functional_currency",
                table: "customer_statement_snapshots");

            migrationBuilder.DropColumn(
                name: "functional_evidence_status",
                table: "customer_statement_snapshots");

            migrationBuilder.DropColumn(
                name: "functional_invoice_activity",
                table: "customer_statement_snapshots");

            migrationBuilder.DropColumn(
                name: "functional_opening_balance",
                table: "customer_statement_snapshots");

            migrationBuilder.DropColumn(
                name: "currency_provenance",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "exchange_rate_date",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "exchange_rate_identity",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "functional_credit_amount",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "functional_currency",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "functional_debit_amount",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "functional_running_balance",
                table: "customer_statement_items");

            migrationBuilder.DropColumn(
                name: "conversion_rounding_residual",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "currency_provenance",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_conversion_id",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_date",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_identity",
                table: "customer_invoice_accounting_profiles");

            migrationBuilder.DropColumn(
                name: "exchange_rate_purpose",
                table: "customer_invoice_accounting_profiles");
        }
    }
}
