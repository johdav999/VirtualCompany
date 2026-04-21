using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceCounterpartyMasterData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";

            if (isPostgres)
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE finance_counterparties ADD COLUMN IF NOT EXISTS payment_terms character varying(64) NULL;
                    ALTER TABLE finance_counterparties ADD COLUMN IF NOT EXISTS tax_id character varying(64) NULL;
                    ALTER TABLE finance_counterparties ADD COLUMN IF NOT EXISTS credit_limit numeric(18,2) NULL;
                    ALTER TABLE finance_counterparties ADD COLUMN IF NOT EXISTS preferred_payment_method character varying(64) NULL;
                    ALTER TABLE finance_counterparties ADD COLUMN IF NOT EXISTS default_account_mapping character varying(64) NULL;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE finance_counterparties
                    SET
                        payment_terms = COALESCE(NULLIF(payment_terms, ''), 'Net30'),
                        credit_limit = COALESCE(credit_limit, 0),
                        preferred_payment_method = COALESCE(NULLIF(preferred_payment_method, ''), 'bank_transfer'),
                        default_account_mapping = COALESCE(NULLIF(default_account_mapping, ''), CASE WHEN LOWER(counterparty_type) = 'customer' THEN '1100' ELSE '2000' END)
                    WHERE payment_terms IS NULL
                        OR payment_terms = ''
                        OR credit_limit IS NULL
                        OR preferred_payment_method IS NULL
                        OR preferred_payment_method = ''
                        OR default_account_mapping IS NULL
                        OR default_account_mapping = '';
                    """);

                migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_finance_counterparties_company_id_name" ON finance_counterparties (company_id, name);""");
                migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_finance_counterparties_company_id_counterparty_type_name" ON finance_counterparties (company_id, counterparty_type, name);""");
                migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_finance_counterparties_company_id_email" ON finance_counterparties (company_id, email);""");
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH('finance_counterparties', 'payment_terms') IS NULL
                    BEGIN
                        ALTER TABLE [finance_counterparties] ADD [payment_terms] nvarchar(64) NULL;
                    END
                    IF COL_LENGTH('finance_counterparties', 'tax_id') IS NULL
                    BEGIN
                        ALTER TABLE [finance_counterparties] ADD [tax_id] nvarchar(64) NULL;
                    END
                    IF COL_LENGTH('finance_counterparties', 'credit_limit') IS NULL
                    BEGIN
                        ALTER TABLE [finance_counterparties] ADD [credit_limit] decimal(18,2) NULL;
                    END
                    IF COL_LENGTH('finance_counterparties', 'preferred_payment_method') IS NULL
                    BEGIN
                        ALTER TABLE [finance_counterparties] ADD [preferred_payment_method] nvarchar(64) NULL;
                    END
                    IF COL_LENGTH('finance_counterparties', 'default_account_mapping') IS NULL
                    BEGIN
                        ALTER TABLE [finance_counterparties] ADD [default_account_mapping] nvarchar(64) NULL;
                    END
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE [finance_counterparties]
                    SET
                        [payment_terms] = COALESCE(NULLIF([payment_terms], N''), N'Net30'),
                        [credit_limit] = COALESCE([credit_limit], 0),
                        [preferred_payment_method] = COALESCE(NULLIF([preferred_payment_method], N''), N'bank_transfer'),
                        [default_account_mapping] = COALESCE(NULLIF([default_account_mapping], N''), CASE WHEN LOWER([counterparty_type]) = N'customer' THEN N'1100' ELSE N'2000' END)
                    WHERE [payment_terms] IS NULL
                        OR [payment_terms] = N''
                        OR [credit_limit] IS NULL
                        OR [preferred_payment_method] IS NULL
                        OR [preferred_payment_method] = N''
                        OR [default_account_mapping] IS NULL
                        OR [default_account_mapping] = N'';
                    """);

                migrationBuilder.Sql(
                    """
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_name' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
                    BEGIN
                        CREATE INDEX [IX_finance_counterparties_company_id_name]
                        ON [finance_counterparties] ([company_id], [name]);
                    END
                    """);

                migrationBuilder.Sql(
                    """
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_counterparty_type_name' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
                    BEGIN
                        CREATE INDEX [IX_finance_counterparties_company_id_counterparty_type_name]
                        ON [finance_counterparties] ([company_id], [counterparty_type], [name]);
                    END
                    """);

                migrationBuilder.Sql(
                    """
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_email' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
                    BEGIN
                        CREATE INDEX [IX_finance_counterparties_company_id_email]
                        ON [finance_counterparties] ([company_id], [email]);
                    END
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";

            if (isPostgres)
            {
                migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_finance_counterparties_company_id_email";""");
                migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_finance_counterparties_company_id_counterparty_type_name";""");
                migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_finance_counterparties_company_id_name";""");
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_email' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
                    BEGIN
                        DROP INDEX [IX_finance_counterparties_company_id_email] ON [finance_counterparties];
                    END
                    """);

                migrationBuilder.Sql(
                    """
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_counterparty_type_name' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
                    BEGIN
                        DROP INDEX [IX_finance_counterparties_company_id_counterparty_type_name] ON [finance_counterparties];
                    END
                    """);

                migrationBuilder.Sql(
                    """
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_finance_counterparties_company_id_name' AND [object_id] = OBJECT_ID(N'[finance_counterparties]'))
                    BEGIN
                        DROP INDEX [IX_finance_counterparties_company_id_name] ON [finance_counterparties];
                    END
                    """);
                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH('finance_counterparties', 'payment_terms') IS NOT NULL ALTER TABLE [finance_counterparties] DROP COLUMN [payment_terms];
                    IF COL_LENGTH('finance_counterparties', 'tax_id') IS NOT NULL ALTER TABLE [finance_counterparties] DROP COLUMN [tax_id];
                    IF COL_LENGTH('finance_counterparties', 'credit_limit') IS NOT NULL ALTER TABLE [finance_counterparties] DROP COLUMN [credit_limit];
                    IF COL_LENGTH('finance_counterparties', 'preferred_payment_method') IS NOT NULL ALTER TABLE [finance_counterparties] DROP COLUMN [preferred_payment_method];
                    IF COL_LENGTH('finance_counterparties', 'default_account_mapping') IS NOT NULL ALTER TABLE [finance_counterparties] DROP COLUMN [default_account_mapping];
                    """);
            }

            if (isPostgres)
            {
                migrationBuilder.Sql("""ALTER TABLE finance_counterparties DROP COLUMN IF EXISTS payment_terms;""");
                migrationBuilder.Sql("""ALTER TABLE finance_counterparties DROP COLUMN IF EXISTS tax_id;""");
                migrationBuilder.Sql("""ALTER TABLE finance_counterparties DROP COLUMN IF EXISTS credit_limit;""");
                migrationBuilder.Sql("""ALTER TABLE finance_counterparties DROP COLUMN IF EXISTS preferred_payment_method;""");
                migrationBuilder.Sql("""ALTER TABLE finance_counterparties DROP COLUMN IF EXISTS default_account_mapping;""");
            }
        }
    }
}