using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddManualClassificationCashPostingStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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