using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddSimulationCashDeltaRecords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var string64Type = isPostgres ? "character varying(64)" : "nvarchar(64)";

            migrationBuilder.CreateTable(
                name: "simulation_cash_delta_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    simulation_event_record_id = table.Column<Guid>(type: guidType, nullable: false),
                    simulation_date_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    source_entity_type = table.Column<string>(type: string64Type, maxLength: 64, nullable: false),
                    source_entity_id = table.Column<Guid>(type: guidType, nullable: true),
                    cash_before = table.Column<decimal>(type: decimalType, nullable: false),
                    cash_delta = table.Column<decimal>(type: decimalType, nullable: false),
                    cash_after = table.Column<decimal>(type: decimalType, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_cash_delta_records", x => x.id);
                    table.UniqueConstraint("AK_simulation_cash_delta_records_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint(
                        "CK_simulation_cash_delta_records_cash_snapshot",
                        "cash_after = cash_before + cash_delta");
                    table.ForeignKey(
                        name: "FK_simulation_cash_delta_records_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_simulation_cash_delta_records_simulation_event_records_company_id_simulation_event_record_id",
                        columns: x => new { x.company_id, x.simulation_event_record_id },
                        principalTable: "simulation_event_records",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_cash_delta_records_company_id_simulation_date_at",
                table: "simulation_cash_delta_records",
                columns: new[] { "company_id", "simulation_date_at" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_cash_delta_records_company_id_simulation_event_record_id",
                table: "simulation_cash_delta_records",
                columns: new[] { "company_id", "simulation_event_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_cash_delta_records_company_id_source_entity_type_source_entity_id",
                table: "simulation_cash_delta_records",
                columns: new[] { "company_id", "source_entity_type", "source_entity_id" });

            migrationBuilder.Sql(
                """
                INSERT INTO simulation_cash_delta_records (
                    id,
                    company_id,
                    simulation_event_record_id,
                    simulation_date_at,
                    source_entity_type,
                    source_entity_id,
                    cash_before,
                    cash_delta,
                    cash_after,
                    created_at
                )
                SELECT
                    s.id,
                    s.company_id,
                    s.id,
                    s.simulation_date_at,
                    s.source_entity_type,
                    s.source_entity_id,
                    s.cash_before,
                    s.cash_delta,
                    s.cash_after,
                    s.created_at
                FROM simulation_event_records s
                WHERE s.cash_before IS NOT NULL
                  AND s.cash_delta IS NOT NULL
                  AND s.cash_after IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM simulation_cash_delta_records d
                      WHERE d.company_id = s.company_id
                        AND d.simulation_event_record_id = s.id
                  );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "simulation_cash_delta_records");
        }
    }
}
