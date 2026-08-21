using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

/// <summary>
/// Compatibility bridge for databases created by AddCashPostingTraceabilityBackfillSupport,
/// whose executable migration predated the check constraints captured in its target model.
/// The following convergence migration safely replaces this constraint with the expanded set.
/// </summary>
[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260820135900_EnsureBankPostingStateConstraintBeforeConvergence")]
public sealed class EnsureBankPostingStateConstraintBeforeConvergence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[bank_transaction_posting_states]', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM sys.check_constraints
                   WHERE parent_object_id = OBJECT_ID(N'[bank_transaction_posting_states]')
                     AND [name] = N'CK_bank_transaction_posting_states_posting_state'
               )
            BEGIN
                IF EXISTS
                (
                    SELECT 1
                    FROM [__EFMigrationsHistory]
                    WHERE [MigrationId] = N'20260820140042_ConvergeBankReconciliationOnNativeLedger'
                )
                BEGIN
                    ALTER TABLE [bank_transaction_posting_states] WITH CHECK
                    ADD CONSTRAINT [CK_bank_transaction_posting_states_posting_state]
                    CHECK ([posting_state] IN ('pending', 'posted', 'skipped_unmatched', 'conflict', 'suspense', 'corrected'));
                END
                ELSE
                BEGIN
                    ALTER TABLE [bank_transaction_posting_states] WITH CHECK
                    ADD CONSTRAINT [CK_bank_transaction_posting_states_posting_state]
                    CHECK ([posting_state] IN ('pending', 'posted', 'skipped_unmatched', 'conflict'));
                END
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Compatibility guard only. Removing the active constraint would weaken restored databases.
    }
}
