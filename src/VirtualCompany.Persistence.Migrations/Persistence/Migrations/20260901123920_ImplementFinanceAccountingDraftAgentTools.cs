using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementFinanceAccountingDraftAgentTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_references_json",
                table: "manual_journal_drafts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "finance_advanced_reconciliation_groups",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposal_hash",
                table: "finance_advanced_reconciliation_groups",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_advanced_reconciliation_groups_company_id_idempotency_key",
                table: "finance_advanced_reconciliation_groups",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_finance_advanced_reconciliation_groups_company_id_idempotency_key",
                table: "finance_advanced_reconciliation_groups");

            migrationBuilder.DropColumn(
                name: "source_references_json",
                table: "manual_journal_drafts");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "finance_advanced_reconciliation_groups");

            migrationBuilder.DropColumn(
                name: "proposal_hash",
                table: "finance_advanced_reconciliation_groups");
        }
    }
}
