using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260421153000_AddManualClassificationCashPostingStatus")]
    public partial class AddManualClassificationCashPostingStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1
                        FROM sys.check_constraints
                        WHERE [name] = N'CK_bank_transaction_posting_states_matching_status'
                            AND [parent_object_id] = OBJECT_ID(N'[bank_transaction_posting_states]')
                    )
                    BEGIN
                        ALTER TABLE [bank_transaction_posting_states] DROP CONSTRAINT [CK_bank_transaction_posting_states_matching_status];
                    END;

                    ALTER TABLE [bank_transaction_posting_states]
                    ADD CONSTRAINT [CK_bank_transaction_posting_states_matching_status]
                    CHECK (matching_status IN ('unknown', 'matched', 'manually_classified', 'unmatched'));
                    """);

                return;
            }

            migrationBuilder.DropCheckConstraint(
                name: "CK_bank_transaction_posting_states_matching_status",
                table: "bank_transaction_posting_states");

            migrationBuilder.AddCheckConstraint(
                name: "CK_bank_transaction_posting_states_matching_status",
                table: "bank_transaction_posting_states",
                sql: "matching_status IN ('unknown', 'matched', 'manually_classified', 'unmatched')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1
                        FROM sys.check_constraints
                        WHERE [name] = N'CK_bank_transaction_posting_states_matching_status'
                            AND [parent_object_id] = OBJECT_ID(N'[bank_transaction_posting_states]')
                    )
                    BEGIN
                        ALTER TABLE [bank_transaction_posting_states] DROP CONSTRAINT [CK_bank_transaction_posting_states_matching_status];
                    END;

                    ALTER TABLE [bank_transaction_posting_states]
                    ADD CONSTRAINT [CK_bank_transaction_posting_states_matching_status]
                    CHECK (matching_status IN ('unknown', 'matched', 'unmatched'));
                    """);

                return;
            }

            migrationBuilder.DropCheckConstraint(
                name: "CK_bank_transaction_posting_states_matching_status",
                table: "bank_transaction_posting_states");

            migrationBuilder.AddCheckConstraint(
                name: "CK_bank_transaction_posting_states_matching_status",
                table: "bank_transaction_posting_states",
                sql: "matching_status IN ('unknown', 'matched', 'unmatched')");
        }
    }
}
