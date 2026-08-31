using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAccountingAdministrationGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_reportable",
                table: "finance_accounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "lifecycle_reason",
                table: "finance_accounts",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "lifecycle_version",
                table: "finance_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "posting_restriction",
                table: "finance_accounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<Guid>(
                name: "replacement_account_id",
                table: "finance_accounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounting_account_lifecycle_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    change_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    account_class = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    normal_balance = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    is_reportable = table.Column<bool>(type: "bit", nullable: false),
                    posting_restriction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    replacement_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recorded_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_account_lifecycle_history", x => x.id);
                    table.UniqueConstraint("AK_accounting_account_lifecycle_history_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_account_lifecycle_history_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO accounting_account_lifecycle_history
                    (id, company_id, finance_account_id, version, change_type, name, account_class,
                     normal_balance, is_reportable, posting_restriction, effective_from, effective_to,
                     replacement_account_id, reason, actor_user_id, recorded_utc)
                SELECT NEWID(), company_id, id, 1, 'created', name, account_class, normal_balance,
                       1, 'none', COALESCE(effective_from, CAST(opened_at AS date)), effective_to,
                       NULL, 'Existing classified account registered during lifecycle governance migration.',
                       NULL, updated_at
                FROM finance_accounts
                WHERE account_class IS NOT NULL AND normal_balance IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "accounting_commerce_event_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_version = table.Column<long>(type: "bigint", nullable: false),
                    contract_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_system = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    occurred_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    received_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_commerce_event_receipts", x => x.id);
                    table.UniqueConstraint("AK_accounting_commerce_event_receipts_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "accounting_series_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    series_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    transaction_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: true),
                    location_dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    jurisdiction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    policy_pack_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    policy_pack_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    provider_series_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    scope_key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_series_policies", x => x.id);
                    table.UniqueConstraint("AK_accounting_series_policies_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "accounting_voucher_gap_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    voucher_series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    missing_number = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recorded_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_voucher_gap_evidence", x => x.id);
                    table.UniqueConstraint("AK_accounting_voucher_gap_evidence_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_voucher_gap_evidence_accounting_voucher_series_company_id_voucher_series_id",
                        columns: x => new { x.company_id, x.voucher_series_id },
                        principalTable: "accounting_voucher_series",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_accounts_company_id_replacement_account_id",
                table: "finance_accounts",
                columns: new[] { "company_id", "replacement_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_account_lifecycle_history_company_id_finance_account_id_effective_from",
                table: "accounting_account_lifecycle_history",
                columns: new[] { "company_id", "finance_account_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_account_lifecycle_history_company_id_finance_account_id_version",
                table: "accounting_account_lifecycle_history",
                columns: new[] { "company_id", "finance_account_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_commerce_event_receipts_company_id_event_id_event_version",
                table: "accounting_commerce_event_receipts",
                columns: new[] { "company_id", "event_id", "event_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_commerce_event_receipts_company_id_event_type_received_utc",
                table: "accounting_commerce_event_receipts",
                columns: new[] { "company_id", "event_type", "received_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_series_policies_company_id_series_kind_scope_key",
                table: "accounting_series_policies",
                columns: new[] { "company_id", "series_kind", "scope_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_series_policies_company_id_series_kind_series_id_is_active",
                table: "accounting_series_policies",
                columns: new[] { "company_id", "series_kind", "series_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_voucher_gap_evidence_company_id_voucher_series_id_fiscal_year_missing_number",
                table: "accounting_voucher_gap_evidence",
                columns: new[] { "company_id", "voucher_series_id", "fiscal_year", "missing_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_accounts_finance_accounts_company_id_replacement_account_id",
                table: "finance_accounts",
                columns: new[] { "company_id", "replacement_account_id" },
                principalTable: "finance_accounts",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_finance_accounts_finance_accounts_company_id_replacement_account_id",
                table: "finance_accounts");

            migrationBuilder.DropTable(
                name: "accounting_account_lifecycle_history");

            migrationBuilder.DropTable(
                name: "accounting_commerce_event_receipts");

            migrationBuilder.DropTable(
                name: "accounting_series_policies");

            migrationBuilder.DropTable(
                name: "accounting_voucher_gap_evidence");

            migrationBuilder.DropIndex(
                name: "IX_finance_accounts_company_id_replacement_account_id",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "is_reportable",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "lifecycle_reason",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "lifecycle_version",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "posting_restriction",
                table: "finance_accounts");

            migrationBuilder.DropColumn(
                name: "replacement_account_id",
                table: "finance_accounts");
        }
    }
}
