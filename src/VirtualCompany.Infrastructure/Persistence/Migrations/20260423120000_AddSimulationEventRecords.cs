using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddSimulationEventRecords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";
            var string128Type = isPostgres ? "character varying(128)" : "nvarchar(128)";
            var string256Type = isPostgres ? "character varying(256)" : "nvarchar(256)";

            migrationBuilder.CreateTable(
                name: "simulation_event_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    simulation_session_id = table.Column<Guid>(type: guidType, nullable: true),
                    seed = table.Column<int>(type: "int", nullable: false),
                    start_simulated_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    simulation_date_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    event_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    source_entity_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    source_entity_id = table.Column<Guid>(type: guidType, nullable: true),
                    source_reference = table.Column<string>(type: string128Type, maxLength: 128, nullable: true),
                    parent_event_id = table.Column<Guid>(type: guidType, nullable: true),
                    sequence_number = table.Column<int>(type: "int", nullable: false),
                    deterministic_key = table.Column<string>(type: string256Type, maxLength: 256, nullable: false),
                    cash_before = table.Column<decimal>(type: decimalType, nullable: true),
                    cash_delta = table.Column<decimal>(type: decimalType, nullable: true),
                    cash_after = table.Column<decimal>(type: decimalType, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_event_records", x => x.id);
                    table.UniqueConstraint("AK_simulation_event_records_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint(
                        "CK_simulation_event_records_cash_snapshot",
                        "((cash_before IS NULL AND cash_delta IS NULL AND cash_after IS NULL) OR (cash_before IS NOT NULL AND cash_delta IS NOT NULL AND cash_after IS NOT NULL AND cash_after = cash_before + cash_delta))");
                    table.CheckConstraint("CK_simulation_event_records_sequence_number_positive", "sequence_number > 0");
                    table.ForeignKey(
                        name: "FK_simulation_event_records_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_simulation_event_records_simulation_event_records_company_id_parent_event_id",
                        columns: x => new { x.company_id, x.parent_event_id },
                        principalTable: "simulation_event_records",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_event_records_company_id_simulation_date_at",
                table: "simulation_event_records",
                columns: new[] { "company_id", "simulation_date_at" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_event_records_company_id_event_type_simulation_date_at",
                table: "simulation_event_records",
                columns: new[] { "company_id", "event_type", "simulation_date_at" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_event_records_company_id_source_entity_type_source_entity_id",
                table: "simulation_event_records",
                columns: new[] { "company_id", "source_entity_type", "source_entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_event_records_company_id_simulation_session_id",
                table: "simulation_event_records",
                columns: new[] { "company_id", "simulation_session_id" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_event_records_company_id_deterministic_key",
                table: "simulation_event_records",
                columns: new[] { "company_id", "deterministic_key" },
                unique: true);

            migrationBuilder.AddColumn<Guid>(name: "source_simulation_event_record_id", table: "finance_payments", type: guidType, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "source_simulation_event_record_id", table: "finance_invoices", type: guidType, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "source_simulation_event_record_id", table: "finance_bills", type: guidType, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "source_simulation_event_record_id", table: "finance_transactions", type: guidType, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "source_simulation_event_record_id", table: "finance_balances", type: guidType, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "source_simulation_event_record_id", table: "bank_transactions", type: guidType, nullable: true);

            migrationBuilder.CreateIndex(name: "IX_finance_payments_company_id_source_simulation_event_record_id", table: "finance_payments", columns: new[] { "company_id", "source_simulation_event_record_id" });
            migrationBuilder.CreateIndex(name: "IX_finance_invoices_company_id_source_simulation_event_record_id", table: "finance_invoices", columns: new[] { "company_id", "source_simulation_event_record_id" });
            migrationBuilder.CreateIndex(name: "IX_finance_bills_company_id_source_simulation_event_record_id", table: "finance_bills", columns: new[] { "company_id", "source_simulation_event_record_id" });
            migrationBuilder.CreateIndex(name: "IX_finance_transactions_company_id_source_simulation_event_record_id", table: "finance_transactions", columns: new[] { "company_id", "source_simulation_event_record_id" });
            migrationBuilder.CreateIndex(name: "IX_finance_balances_company_id_source_simulation_event_record_id", table: "finance_balances", columns: new[] { "company_id", "source_simulation_event_record_id" });
            migrationBuilder.CreateIndex(name: "IX_bank_transactions_company_id_source_simulation_event_record_id", table: "bank_transactions", columns: new[] { "company_id", "source_simulation_event_record_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_finance_payments_simulation_event_records_company_id_source_simulation_event_record_id",
                table: "finance_payments",
                columns: new[] { "company_id", "source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_invoices_simulation_event_records_company_id_source_simulation_event_record_id",
                table: "finance_invoices",
                columns: new[] { "company_id", "source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_bills_simulation_event_records_company_id_source_simulation_event_record_id",
                table: "finance_bills",
                columns: new[] { "company_id", "source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_transactions_simulation_event_records_company_id_source_simulation_event_record_id",
                table: "finance_transactions",
                columns: new[] { "company_id", "source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_balances_simulation_event_records_company_id_source_simulation_event_record_id",
                table: "finance_balances",
                columns: new[] { "company_id", "source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_bank_transactions_simulation_event_records_company_id_source_simulation_event_record_id",
                table: "bank_transactions",
                columns: new[] { "company_id", "source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_finance_payments_simulation_event_records_company_id_source_simulation_event_record_id", table: "finance_payments");
            migrationBuilder.DropForeignKey(name: "FK_finance_invoices_simulation_event_records_company_id_source_simulation_event_record_id", table: "finance_invoices");
            migrationBuilder.DropForeignKey(name: "FK_finance_bills_simulation_event_records_company_id_source_simulation_event_record_id", table: "finance_bills");
            migrationBuilder.DropForeignKey(name: "FK_finance_transactions_simulation_event_records_company_id_source_simulation_event_record_id", table: "finance_transactions");
            migrationBuilder.DropForeignKey(name: "FK_finance_balances_simulation_event_records_company_id_source_simulation_event_record_id", table: "finance_balances");
            migrationBuilder.DropForeignKey(name: "FK_bank_transactions_simulation_event_records_company_id_source_simulation_event_record_id", table: "bank_transactions");

            migrationBuilder.DropIndex(name: "IX_finance_payments_company_id_source_simulation_event_record_id", table: "finance_payments");
            migrationBuilder.DropIndex(name: "IX_finance_invoices_company_id_source_simulation_event_record_id", table: "finance_invoices");
            migrationBuilder.DropIndex(name: "IX_finance_bills_company_id_source_simulation_event_record_id", table: "finance_bills");
            migrationBuilder.DropIndex(name: "IX_finance_transactions_company_id_source_simulation_event_record_id", table: "finance_transactions");
            migrationBuilder.DropIndex(name: "IX_finance_balances_company_id_source_simulation_event_record_id", table: "finance_balances");
            migrationBuilder.DropIndex(name: "IX_bank_transactions_company_id_source_simulation_event_record_id", table: "bank_transactions");

            migrationBuilder.DropColumn(name: "source_simulation_event_record_id", table: "finance_payments");
            migrationBuilder.DropColumn(name: "source_simulation_event_record_id", table: "finance_invoices");
            migrationBuilder.DropColumn(name: "source_simulation_event_record_id", table: "finance_bills");
            migrationBuilder.DropColumn(name: "source_simulation_event_record_id", table: "finance_transactions");
            migrationBuilder.DropColumn(name: "source_simulation_event_record_id", table: "finance_balances");
            migrationBuilder.DropColumn(name: "source_simulation_event_record_id", table: "bank_transactions");

            migrationBuilder.DropTable(name: "simulation_event_records");
        }
    }
}