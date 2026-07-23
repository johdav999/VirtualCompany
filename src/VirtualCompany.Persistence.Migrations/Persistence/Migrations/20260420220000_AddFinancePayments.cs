using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260420220000_AddFinancePayments")]
    public partial class AddFinancePayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string3Type = isPostgres ? "character varying(3)" : "nvarchar(3)";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string200Type = isPostgres ? "character varying(200)" : "nvarchar(200)";
            var companyPrincipalColumn = isPostgres ? "id" : "Id";

            migrationBuilder.CreateTable(
                name: "finance_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    payment_type = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: string3Type, maxLength: 3, nullable: false),
                    payment_date = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    method = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    counterparty_reference = table.Column<string>(type: string200Type, maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_payments", x => x.id);
                    table.UniqueConstraint("AK_finance_payments_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_payments_amount_positive", "amount > 0");
                    table.CheckConstraint("CK_finance_payments_payment_type", "payment_type IN ('incoming', 'outgoing')");
                    table.ForeignKey(
                        name: "FK_finance_payments_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: companyPrincipalColumn,
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_cash_ledger_links_payments_payment_id",
                table: "payment_cash_ledger_links",
                column: "payment_id",
                principalTable: "finance_payments",
                principalColumn: "id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.CreateIndex(
                name: "IX_finance_payments_company_id",
                table: "finance_payments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_payments_status",
                table: "finance_payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_finance_payments_payment_date",
                table: "finance_payments",
                column: "payment_date");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_cash_ledger_links_payments_payment_id",
                table: "payment_cash_ledger_links");

            migrationBuilder.DropTable(
                name: "finance_payments");
        }
    }
}
