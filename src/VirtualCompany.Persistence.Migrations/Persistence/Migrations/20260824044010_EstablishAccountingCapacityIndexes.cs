using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstablishAccountingCapacityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Some databases received this table before its migration was added to
            // the authoritative history. Converge those databases without losing
            // the ability to create the schema from an empty database.
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[trial_balance_snapshots]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[trial_balance_snapshots] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [fiscal_period_id] uniqueidentifier NOT NULL,
                        [finance_account_id] uniqueidentifier NOT NULL,
                        [balance_amount] decimal(18,2) NOT NULL,
                        [currency] nvarchar(3) NOT NULL,
                        [created_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_trial_balance_snapshots] PRIMARY KEY ([id]),
                        CONSTRAINT [FK_trial_balance_snapshots_companies_company_id]
                            FOREIGN KEY ([company_id]) REFERENCES [dbo].[companies] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_trial_balance_snapshots_finance_accounts_company_id_finance_account_id]
                            FOREIGN KEY ([company_id], [finance_account_id])
                            REFERENCES [dbo].[finance_accounts] ([company_id], [id]),
                        CONSTRAINT [FK_trial_balance_snapshots_finance_fiscal_periods_company_id_fiscal_period_id]
                            FOREIGN KEY ([company_id], [fiscal_period_id])
                            REFERENCES [dbo].[finance_fiscal_periods] ([company_id], [id])
                    );
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID(N'[dbo].[trial_balance_snapshots]')
                      AND [name] = N'IX_trial_balance_snapshots_company_id_finance_account_id')
                BEGIN
                    CREATE INDEX [IX_trial_balance_snapshots_company_id_finance_account_id]
                        ON [dbo].[trial_balance_snapshots] ([company_id], [finance_account_id]);
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID(N'[dbo].[trial_balance_snapshots]')
                      AND [name] = N'IX_trial_balance_snapshots_company_id_fiscal_period_id_finance_account_id')
                BEGIN
                    CREATE UNIQUE INDEX [IX_trial_balance_snapshots_company_id_fiscal_period_id_finance_account_id]
                        ON [dbo].[trial_balance_snapshots] ([company_id], [fiscal_period_id], [finance_account_id]);
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_company_id_fiscal_period_id_status_entry_at_entry_number",
                table: "ledger_entries",
                columns: new[] { "company_id", "fiscal_period_id", "status", "entry_at", "entry_number" });

            migrationBuilder.DropIndex(
                name: "IX_ledger_entry_lines_company_id_finance_account_id",
                table: "ledger_entry_lines");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_lines_company_id_finance_account_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "finance_account_id" })
                .Annotation("SqlServer:Include", new[] { "ledger_entry_id", "debit_amount", "credit_amount" });

            migrationBuilder.CreateIndex(
                name: "IX_background_executions_company_id_status_created_at",
                table: "background_executions",
                columns: new[] { "company_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_export_jobs_company_id_status_expires_at",
                table: "accounting_export_jobs",
                columns: new[] { "company_id", "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ledger_entry_lines_company_id_finance_account_id",
                table: "ledger_entry_lines");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_lines_company_id_finance_account_id",
                table: "ledger_entry_lines",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_company_id_fiscal_period_id_status_entry_at_entry_number",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_background_executions_company_id_status_created_at",
                table: "background_executions");

            migrationBuilder.DropIndex(
                name: "IX_accounting_export_jobs_company_id_status_expires_at",
                table: "accounting_export_jobs");

            migrationBuilder.DropTable(
                name: "trial_balance_snapshots");
        }
    }
}
