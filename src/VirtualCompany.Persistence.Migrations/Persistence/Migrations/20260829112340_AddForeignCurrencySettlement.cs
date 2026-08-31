using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignCurrencySettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ledger_entry_lines_document_amount",
                table: "ledger_entry_lines");

            migrationBuilder.AddColumn<decimal>(
                name: "allocated_functional_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "allocated_payment_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "bank_functional_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "document_outstanding_after",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "fee_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "fee_functional_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "functional_currency",
                table: "payment_allocations",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "functional_outstanding_after",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_currency",
                table: "payment_allocations",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "realized_gain_loss_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reversal_idempotency_key",
                table: "payment_allocations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reversal_ledger_entry_id",
                table: "payment_allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reversal_reason",
                table: "payment_allocations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reversed_at",
                table: "payment_allocations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reversed_by_user_id",
                table: "payment_allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_functional_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "settlement_conversion_rounding_residual",
                table: "payment_allocations",
                type: "decimal(28,18)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "settlement_exchange_rate_conversion_id",
                table: "payment_allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "settlement_functional_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "settlement_ledger_entry_id",
                table: "payment_allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "settlement_rate",
                table: "payment_allocations",
                type: "decimal(28,18)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "settlement_rate_date",
                table: "payment_allocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_rate_identity",
                table: "payment_allocations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_status",
                table: "payment_allocations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "payment_allocations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "write_off_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "write_off_functional_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [payment_allocations]
                SET [allocated_payment_amount] = [allocated_amount],
                    [payment_currency] = [currency],
                    [settlement_status] = N'legacy_unavailable',
                    [version] = 1
                WHERE [allocated_payment_amount] IS NULL
                   OR [payment_currency] IS NULL
                   OR [settlement_status] IS NULL
                   OR [version] IS NULL;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "allocated_payment_amount",
                table: "payment_allocations",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "payment_currency",
                table: "payment_allocations",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(3)",
                oldMaxLength: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "settlement_status",
                table: "payment_allocations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "version",
                table: "payment_allocations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_reversal_idempotency_key",
                table: "payment_allocations",
                columns: new[] { "company_id", "reversal_idempotency_key" },
                unique: true,
                filter: "[reversal_idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_reversal_ledger_entry_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "reversal_ledger_entry_id" },
                unique: true,
                filter: "[reversal_ledger_entry_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_settlement_exchange_rate_conversion_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "settlement_exchange_rate_conversion_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_settlement_ledger_entry_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "settlement_ledger_entry_id" },
                unique: true,
                filter: "[settlement_ledger_entry_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_settlement_status_settlement_rate_date",
                table: "payment_allocations",
                columns: new[] { "company_id", "settlement_status", "settlement_rate_date" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_allocations_fee_non_negative",
                table: "payment_allocations",
                sql: "fee_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_allocations_payment_amount_positive",
                table: "payment_allocations",
                sql: "allocated_payment_amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_allocations_write_off_non_negative",
                table: "payment_allocations",
                sql: "write_off_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ledger_entry_lines_document_amount",
                table: "ledger_entry_lines",
                sql: "CAST(document_debit_amount AS NUMERIC) >= 0 AND CAST(document_credit_amount AS NUMERIC) >= 0 AND (((CAST(document_debit_amount AS NUMERIC) > 0 AND NOT(CAST(document_credit_amount AS NUMERIC) > 0)) OR (CAST(document_credit_amount AS NUMERIC) > 0 AND NOT(CAST(document_debit_amount AS NUMERIC) > 0))) OR (CAST(document_debit_amount AS NUMERIC) = 0 AND CAST(document_credit_amount AS NUMERIC) = 0))");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_allocations_exchange_rate_conversions_company_id_settlement_exchange_rate_conversion_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "settlement_exchange_rate_conversion_id" },
                principalTable: "exchange_rate_conversions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_allocations_ledger_entries_company_id_reversal_ledger_entry_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "reversal_ledger_entry_id" },
                principalTable: "ledger_entries",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_allocations_ledger_entries_company_id_settlement_ledger_entry_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "settlement_ledger_entry_id" },
                principalTable: "ledger_entries",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ledger_entry_lines_document_amount",
                table: "ledger_entry_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_allocations_exchange_rate_conversions_company_id_settlement_exchange_rate_conversion_id",
                table: "payment_allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_allocations_ledger_entries_company_id_reversal_ledger_entry_id",
                table: "payment_allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_allocations_ledger_entries_company_id_settlement_ledger_entry_id",
                table: "payment_allocations");

            migrationBuilder.DropIndex(
                name: "IX_payment_allocations_company_id_reversal_idempotency_key",
                table: "payment_allocations");

            migrationBuilder.DropIndex(
                name: "IX_payment_allocations_company_id_reversal_ledger_entry_id",
                table: "payment_allocations");

            migrationBuilder.DropIndex(
                name: "IX_payment_allocations_company_id_settlement_exchange_rate_conversion_id",
                table: "payment_allocations");

            migrationBuilder.DropIndex(
                name: "IX_payment_allocations_company_id_settlement_ledger_entry_id",
                table: "payment_allocations");

            migrationBuilder.DropIndex(
                name: "IX_payment_allocations_company_id_settlement_status_settlement_rate_date",
                table: "payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_allocations_fee_non_negative",
                table: "payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_allocations_payment_amount_positive",
                table: "payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_allocations_write_off_non_negative",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "allocated_functional_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "allocated_payment_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "bank_functional_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "document_outstanding_after",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "fee_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "fee_functional_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "functional_currency",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "functional_outstanding_after",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "payment_currency",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "realized_gain_loss_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "reversal_idempotency_key",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "reversal_ledger_entry_id",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "reversal_reason",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "reversed_at",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "reversed_by_user_id",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "rounding_functional_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_conversion_rounding_residual",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_exchange_rate_conversion_id",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_functional_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_ledger_entry_id",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_rate",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_rate_date",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_rate_identity",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "settlement_status",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "version",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "write_off_amount",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "write_off_functional_amount",
                table: "payment_allocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ledger_entry_lines_document_amount",
                table: "ledger_entry_lines",
                sql: "CAST(document_debit_amount AS NUMERIC) >= 0 AND CAST(document_credit_amount AS NUMERIC) >= 0 AND ((CAST(document_debit_amount AS NUMERIC) > 0 AND NOT(CAST(document_credit_amount AS NUMERIC) > 0)) OR (CAST(document_credit_amount AS NUMERIC) > 0 AND NOT(CAST(document_debit_amount AS NUMERIC) > 0)))");
        }
    }
}
