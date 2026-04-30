using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Migration("20260425100000_RepairFinancialStatementSnapshotTables")]
    [DbContext(typeof(VirtualCompanyDbContext))]
    public partial class RepairFinancialStatementSnapshotTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF OBJECT_ID(N'[financial_statement_snapshots]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [financial_statement_snapshots] (
                            [id] uniqueidentifier NOT NULL,
                            [company_id] uniqueidentifier NOT NULL,
                            [fiscal_period_id] uniqueidentifier NOT NULL,
                            [statement_type] nvarchar(32) NOT NULL,
                            [source_period_start_at] datetime2 NOT NULL,
                            [source_period_end_at] datetime2 NOT NULL,
                            [version_number] int NOT NULL,
                            [balances_checksum] nvarchar(128) NOT NULL,
                            [generated_at] datetime2 NOT NULL,
                            [currency] nvarchar(3) NOT NULL,
                            CONSTRAINT [PK_financial_statement_snapshots] PRIMARY KEY ([id]),
                            CONSTRAINT [CK_financial_statement_snapshots_statement_type] CHECK (statement_type IN ('balance_sheet', 'cash_flow', 'profit_and_loss')),
                            CONSTRAINT [FK_financial_statement_snapshots_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_financial_statement_snapshots_finance_fiscal_periods_company_id_fiscal_period_id] FOREIGN KEY ([company_id], [fiscal_period_id]) REFERENCES [finance_fiscal_periods] ([company_id], [id]) ON DELETE NO ACTION
                        );
                    END
                    """);

                migrationBuilder.Sql(
                    """
                    IF OBJECT_ID(N'[financial_statement_snapshot_lines]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [financial_statement_snapshot_lines] (
                            [id] uniqueidentifier NOT NULL,
                            [company_id] uniqueidentifier NOT NULL,
                            [snapshot_id] uniqueidentifier NOT NULL,
                            [finance_account_id] uniqueidentifier NULL,
                            [line_code] nvarchar(64) NOT NULL,
                            [line_name] nvarchar(160) NOT NULL,
                            [line_order] int NOT NULL,
                            [report_section] nvarchar(64) NOT NULL,
                            [line_classification] nvarchar(64) NOT NULL,
                            [amount] decimal(18,2) NOT NULL,
                            [currency] nvarchar(3) NOT NULL,
                            CONSTRAINT [PK_financial_statement_snapshot_lines] PRIMARY KEY ([id]),
                            CONSTRAINT [CK_financial_statement_snapshot_lines_report_section] CHECK (report_section IN ('balance_sheet_assets', 'balance_sheet_equity', 'balance_sheet_liabilities', 'cash_flow_financing_activities', 'cash_flow_investing_activities', 'cash_flow_operating_activities', 'cash_flow_supplemental_disclosures', 'profit_and_loss_cost_of_sales', 'profit_and_loss_operating_expenses', 'profit_and_loss_other_income_expense', 'profit_and_loss_revenue', 'profit_and_loss_taxes')),
                            CONSTRAINT [CK_financial_statement_snapshot_lines_line_classification] CHECK (line_classification IN ('cash_disbursement', 'cash_receipt', 'contra_revenue', 'cost_of_sales', 'current_asset', 'current_liability', 'depreciation_and_amortization', 'equity', 'financing_cash_inflow', 'financing_cash_outflow', 'income_tax', 'investing_cash_inflow', 'investing_cash_outflow', 'non_cash_adjustment', 'non_current_asset', 'non_current_liability', 'non_operating_expense', 'non_operating_income', 'operating_expense', 'revenue', 'supplemental_disclosure', 'working_capital')),
                            CONSTRAINT [FK_financial_statement_snapshot_lines_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION,
                            CONSTRAINT [FK_financial_statement_snapshot_lines_finance_accounts_company_id_finance_account_id] FOREIGN KEY ([company_id], [finance_account_id]) REFERENCES [finance_accounts] ([company_id], [id]) ON DELETE NO ACTION,
                            CONSTRAINT [FK_financial_statement_snapshot_lines_financial_statement_snapshots_snapshot_id] FOREIGN KEY ([snapshot_id]) REFERENCES [financial_statement_snapshots] ([id]) ON DELETE CASCADE
                        );
                    END
                    """);

                migrationBuilder.Sql(
                    """
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_version_number' AND object_id = OBJECT_ID(N'[financial_statement_snapshots]'))
                    BEGIN
                        CREATE UNIQUE INDEX [IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_version_number]
                        ON [financial_statement_snapshots] ([company_id], [statement_type], [fiscal_period_id], [version_number]);
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_generated_at' AND object_id = OBJECT_ID(N'[financial_statement_snapshots]'))
                    BEGIN
                        CREATE INDEX [IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_generated_at]
                        ON [financial_statement_snapshots] ([company_id], [statement_type], [fiscal_period_id], [generated_at]);
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_financial_statement_snapshot_lines_company_id_snapshot_id_line_order' AND object_id = OBJECT_ID(N'[financial_statement_snapshot_lines]'))
                    BEGIN
                        CREATE INDEX [IX_financial_statement_snapshot_lines_company_id_snapshot_id_line_order]
                        ON [financial_statement_snapshot_lines] ([company_id], [snapshot_id], [line_order]);
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_financial_statement_snapshot_lines_company_id_finance_account_id' AND object_id = OBJECT_ID(N'[financial_statement_snapshot_lines]'))
                    BEGIN
                        CREATE INDEX [IX_financial_statement_snapshot_lines_company_id_finance_account_id]
                        ON [financial_statement_snapshot_lines] ([company_id], [finance_account_id]);
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_financial_statement_snapshot_lines_snapshot_id_line_code' AND object_id = OBJECT_ID(N'[financial_statement_snapshot_lines]'))
                    BEGIN
                        CREATE UNIQUE INDEX [IX_financial_statement_snapshot_lines_snapshot_id_line_code]
                        ON [financial_statement_snapshot_lines] ([snapshot_id], [line_code]);
                    END
                    """);

                return;
            }

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(
                    """
                    CREATE TABLE IF NOT EXISTS "financial_statement_snapshots" (
                        "id" TEXT NOT NULL CONSTRAINT "PK_financial_statement_snapshots" PRIMARY KEY,
                        "company_id" TEXT NOT NULL,
                        "fiscal_period_id" TEXT NOT NULL,
                        "statement_type" TEXT NOT NULL,
                        "source_period_start_at" TEXT NOT NULL,
                        "source_period_end_at" TEXT NOT NULL,
                        "version_number" INTEGER NOT NULL,
                        "balances_checksum" TEXT NOT NULL,
                        "generated_at" TEXT NOT NULL,
                        "currency" TEXT NOT NULL,
                        CONSTRAINT "FK_financial_statement_snapshots_companies_company_id" FOREIGN KEY ("company_id") REFERENCES "companies" ("id") ON DELETE CASCADE,
                        CONSTRAINT "FK_financial_statement_snapshots_finance_fiscal_periods_company_id_fiscal_period_id" FOREIGN KEY ("company_id", "fiscal_period_id") REFERENCES "finance_fiscal_periods" ("company_id", "id") ON DELETE RESTRICT
                    );

                    CREATE TABLE IF NOT EXISTS "financial_statement_snapshot_lines" (
                        "id" TEXT NOT NULL CONSTRAINT "PK_financial_statement_snapshot_lines" PRIMARY KEY,
                        "company_id" TEXT NOT NULL,
                        "snapshot_id" TEXT NOT NULL,
                        "finance_account_id" TEXT NULL,
                        "line_code" TEXT NOT NULL,
                        "line_name" TEXT NOT NULL,
                        "line_order" INTEGER NOT NULL,
                        "report_section" TEXT NOT NULL,
                        "line_classification" TEXT NOT NULL,
                        "amount" TEXT NOT NULL,
                        "currency" TEXT NOT NULL,
                        CONSTRAINT "FK_financial_statement_snapshot_lines_companies_company_id" FOREIGN KEY ("company_id") REFERENCES "companies" ("id") ON DELETE CASCADE,
                        CONSTRAINT "FK_financial_statement_snapshot_lines_finance_accounts_company_id_finance_account_id" FOREIGN KEY ("company_id", "finance_account_id") REFERENCES "finance_accounts" ("company_id", "id") ON DELETE RESTRICT,
                        CONSTRAINT "FK_financial_statement_snapshot_lines_financial_statement_snapshots_snapshot_id" FOREIGN KEY ("snapshot_id") REFERENCES "financial_statement_snapshots" ("id") ON DELETE CASCADE
                    );

                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_version_number" ON "financial_statement_snapshots" ("company_id", "statement_type", "fiscal_period_id", "version_number");
                    CREATE INDEX IF NOT EXISTS "IX_financial_statement_snapshots_company_id_statement_type_fiscal_period_id_generated_at" ON "financial_statement_snapshots" ("company_id", "statement_type", "fiscal_period_id", "generated_at");
                    CREATE INDEX IF NOT EXISTS "IX_financial_statement_snapshot_lines_company_id_snapshot_id_line_order" ON "financial_statement_snapshot_lines" ("company_id", "snapshot_id", "line_order");
                    CREATE INDEX IF NOT EXISTS "IX_financial_statement_snapshot_lines_company_id_finance_account_id" ON "financial_statement_snapshot_lines" ("company_id", "finance_account_id");
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_financial_statement_snapshot_lines_snapshot_id_line_code" ON "financial_statement_snapshot_lines" ("snapshot_id", "line_code");
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
