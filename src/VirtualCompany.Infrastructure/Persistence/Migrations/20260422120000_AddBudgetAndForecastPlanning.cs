using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddBudgetAndForecastPlanning : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string3Type = isPostgres ? "character varying(3)" : "nvarchar(3)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var nullCostCenterFilter = isPostgres ? "\"cost_center_id\" IS NULL" : "[cost_center_id] IS NULL";
            var nonNullCostCenterFilter = isPostgres ? "\"cost_center_id\" IS NOT NULL" : "[cost_center_id] IS NOT NULL";

            migrationBuilder.CreateTable(
                name: "budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    finance_account_id = table.Column<Guid>(type: guidType, nullable: false),
                    period_start_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    version = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    cost_center_id = table.Column<Guid>(type: guidType, nullable: true),
                    amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budgets", x => x.id);
                    table.UniqueConstraint("AK_budgets_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_budgets_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_budgets_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "forecasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    finance_account_id = table.Column<Guid>(type: guidType, nullable: false),
                    period_start_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    version = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    cost_center_id = table.Column<Guid>(type: guidType, nullable: true),
                    amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecasts", x => x.id);
                    table.UniqueConstraint("AK_forecasts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_forecasts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_forecasts_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_budgets_company_id_period_start_at", table: "budgets", columns: new[] { "company_id", "period_start_at" });
            migrationBuilder.CreateIndex(name: "IX_budgets_company_id_period_start_at_version", table: "budgets", columns: new[] { "company_id", "period_start_at", "version" });
            migrationBuilder.CreateIndex(name: "IX_budgets_company_id_finance_account_id_version_period_start_at", table: "budgets", columns: new[] { "company_id", "finance_account_id", "version", "period_start_at" });
            migrationBuilder.CreateIndex(
                name: "IX_budgets_company_id_period_start_at_finance_account_id_version_cost_center_id",
                table: "budgets",
                columns: new[] { "company_id", "period_start_at", "finance_account_id", "version", "cost_center_id" },
                unique: true,
                filter: nonNullCostCenterFilter);
            migrationBuilder.CreateIndex(
                name: "IX_budgets_company_id_period_start_at_finance_account_id_version_null_cost_center",
                table: "budgets",
                columns: new[] { "company_id", "period_start_at", "finance_account_id", "version" },
                unique: true,
                filter: nullCostCenterFilter);

            migrationBuilder.CreateIndex(name: "IX_forecasts_company_id_period_start_at", table: "forecasts", columns: new[] { "company_id", "period_start_at" });
            migrationBuilder.CreateIndex(name: "IX_forecasts_company_id_period_start_at_version", table: "forecasts", columns: new[] { "company_id", "period_start_at", "version" });
            migrationBuilder.CreateIndex(name: "IX_forecasts_company_id_finance_account_id_version_period_start_at", table: "forecasts", columns: new[] { "company_id", "finance_account_id", "version", "period_start_at" });
            migrationBuilder.CreateIndex(
                name: "IX_forecasts_company_id_period_start_at_finance_account_id_version_cost_center_id",
                table: "forecasts",
                columns: new[] { "company_id", "period_start_at", "finance_account_id", "version", "cost_center_id" },
                unique: true,
                filter: nonNullCostCenterFilter);
            migrationBuilder.CreateIndex(
                name: "IX_forecasts_company_id_period_start_at_finance_account_id_version_null_cost_center",
                table: "forecasts",
                columns: new[] { "company_id", "period_start_at", "finance_account_id", "version" },
                unique: true,
                filter: nullCostCenterFilter);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "budgets");
            migrationBuilder.DropTable(name: "forecasts");
        }
    }
}