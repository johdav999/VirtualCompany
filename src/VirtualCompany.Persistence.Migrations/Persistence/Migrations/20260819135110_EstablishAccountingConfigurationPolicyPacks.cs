using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260819135110_EstablishAccountingConfigurationPolicyPacks")]
public partial class EstablishAccountingConfigurationPolicyPacks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "accounting_configurations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                base_currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                fiscal_year_start_month = table.Column<int>(type: "int", nullable: false),
                fiscal_year_start_day = table.Column<int>(type: "int", nullable: false),
                authority = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                setup_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                policy_pack_key = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                policy_pack_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                policy_pack_effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                rounding_precision = table.Column<int>(type: "int", nullable: false),
                rounding_mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounting_configurations", x => x.id);
                table.UniqueConstraint("AK_accounting_configurations_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_accounting_configurations_authority", "[authority] IN ('internal_ledger', 'external_provider', 'migration')");
                table.CheckConstraint("CK_accounting_configurations_fiscal_year_start_day", "[fiscal_year_start_day] >= 1 AND [fiscal_year_start_day] <= 31");
                table.CheckConstraint("CK_accounting_configurations_fiscal_year_start_month", "[fiscal_year_start_month] >= 1 AND [fiscal_year_start_month] <= 12");
                table.CheckConstraint("CK_accounting_configurations_rounding_precision", "[rounding_precision] >= 0 AND [rounding_precision] <= 6");
                table.CheckConstraint("CK_accounting_configurations_setup_state", "[setup_state] IN ('incomplete', 'ready')");
                table.ForeignKey("FK_accounting_configurations_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "accounting_configuration_account_roles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                accounting_configuration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                role_key = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounting_configuration_account_roles", x => x.id);
                table.ForeignKey(
                    "FK_accounting_configuration_account_roles_accounting_configurations_company_id_accounting_configuration_id",
                    x => new { x.company_id, x.accounting_configuration_id },
                    "accounting_configurations", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    "FK_accounting_configuration_account_roles_finance_accounts_company_id_finance_account_id",
                    x => new { x.company_id, x.finance_account_id },
                    "finance_accounts", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "accounting_policy_pack_selections",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                accounting_configuration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                pack_key = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                pack_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                definition_hash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                is_statutory_compliance_validated = table.Column<bool>(type: "bit", nullable: false),
                effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                selected_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                selected_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounting_policy_pack_selections", x => x.id);
                table.ForeignKey(
                    "FK_accounting_policy_pack_selections_accounting_configurations_company_id_accounting_configuration_id",
                    x => new { x.company_id, x.accounting_configuration_id },
                    "accounting_configurations", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_accounting_configuration_account_roles_company_id_accounting_configuration_id_role_key", "accounting_configuration_account_roles", new[] { "company_id", "accounting_configuration_id", "role_key" }, unique: true);
        migrationBuilder.CreateIndex("IX_accounting_configuration_account_roles_company_id_finance_account_id", "accounting_configuration_account_roles", new[] { "company_id", "finance_account_id" });
        migrationBuilder.CreateIndex("IX_accounting_configurations_company_id", "accounting_configurations", "company_id", unique: true);
        migrationBuilder.CreateIndex("IX_accounting_configurations_company_id_policy_pack_key_policy_pack_version", "accounting_configurations", new[] { "company_id", "policy_pack_key", "policy_pack_version" });
        migrationBuilder.CreateIndex("IX_accounting_policy_pack_selections_company_id_accounting_configuration_id_effective_from", "accounting_policy_pack_selections", new[] { "company_id", "accounting_configuration_id", "effective_from" }, unique: true);
        migrationBuilder.CreateIndex("IX_accounting_policy_pack_selections_company_id_effective_to", "accounting_policy_pack_selections", new[] { "company_id", "effective_to" }, unique: true, filter: "[effective_to] IS NULL");
        migrationBuilder.CreateIndex("IX_accounting_policy_pack_selections_company_id_pack_key_pack_version", "accounting_policy_pack_selections", new[] { "company_id", "pack_key", "pack_version" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("accounting_configuration_account_roles");
        migrationBuilder.DropTable("accounting_policy_pack_selections");
        migrationBuilder.DropTable("accounting_configurations");
    }
}
