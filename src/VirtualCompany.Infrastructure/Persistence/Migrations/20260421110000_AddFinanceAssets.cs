using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceAssets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string32Type = isPostgres ? "character varying(32)" : "nvarchar(32)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string160Type = isPostgres ? "character varying(160)" : "nvarchar(160)";

            migrationBuilder.CreateTable(
                name: "finance_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    counterparty_id = table.Column<Guid>(type: guidType, nullable: false),
                    reference_number = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    name = table.Column<string>(type: string160Type, maxLength: 160, nullable: false),
                    category = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    purchased_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    amount = table.Column<decimal>(type: decimalType, nullable: false),
                    currency = table.Column<string>(type: isPostgres ? "character varying(3)" : "nvarchar(3)", maxLength: 3, nullable: false),
                    funding_behavior = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    funding_settlement_status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    status = table.Column<string>(type: string32Type, maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    source_simulation_event_record_id = table.Column<Guid>(type: guidType, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_assets", x => x.id);
                    table.UniqueConstraint("AK_finance_assets_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_assets_amount_positive", "amount > 0");
                    table.CheckConstraint("CK_finance_assets_funding_behavior", "funding_behavior IN ('cash', 'payable')");
                    table.CheckConstraint("CK_finance_assets_funding_settlement_status", "funding_settlement_status IN ('unpaid', 'partially_paid', 'paid')");
                    table.CheckConstraint("CK_finance_assets_status", "status IN ('active', 'disposed')");
                    table.ForeignKey(
                        name: "FK_finance_assets_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_finance_assets_finance_counterparties_company_id_counterparty_id",
                        columns: x => new { x.company_id, x.counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_assets_simulation_event_records_company_id_source_simulation_event_record_id",
                        columns: x => new { x.company_id, x.source_simulation_event_record_id },
                        principalTable: "simulation_event_records",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_assets_company_id_reference_number",
                table: "finance_assets",
                columns: new[] { "company_id", "reference_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_assets_company_id_purchased_at",
                table: "finance_assets",
                columns: new[] { "company_id", "purchased_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_assets_company_id_funding_behavior_funding_settlement_status",
                table: "finance_assets",
                columns: new[] { "company_id", "funding_behavior", "funding_settlement_status" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_assets_company_id_counterparty_id",
                table: "finance_assets",
                columns: new[] { "company_id", "counterparty_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_assets_company_id_source_simulation_event_record_id",
                table: "finance_assets",
                columns: new[] { "company_id", "source_simulation_event_record_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_assets");
        }
    }
}
