using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260823190000_RoundSimulationCashSnapshotConstraint")]
public sealed class RoundSimulationCashSnapshotConstraint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_simulation_event_records_cash_snapshot",
            table: "simulation_event_records");

        migrationBuilder.AddCheckConstraint(
            name: "CK_simulation_event_records_cash_snapshot",
            table: "simulation_event_records",
            sql: "((cash_before IS NULL AND cash_delta IS NULL AND cash_after IS NULL) OR (cash_before IS NOT NULL AND cash_delta IS NOT NULL AND cash_after IS NOT NULL AND cash_after = ROUND(cash_before + cash_delta, 2)))");

        migrationBuilder.DropCheckConstraint(
            name: "CK_simulation_cash_delta_records_cash_snapshot",
            table: "simulation_cash_delta_records");

        migrationBuilder.AddCheckConstraint(
            name: "CK_simulation_cash_delta_records_cash_snapshot",
            table: "simulation_cash_delta_records",
            sql: "cash_after = ROUND(cash_before + cash_delta, 2)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_simulation_cash_delta_records_cash_snapshot",
            table: "simulation_cash_delta_records");

        migrationBuilder.AddCheckConstraint(
            name: "CK_simulation_cash_delta_records_cash_snapshot",
            table: "simulation_cash_delta_records",
            sql: "cash_after = cash_before + cash_delta");

        migrationBuilder.DropCheckConstraint(
            name: "CK_simulation_event_records_cash_snapshot",
            table: "simulation_event_records");

        migrationBuilder.AddCheckConstraint(
            name: "CK_simulation_event_records_cash_snapshot",
            table: "simulation_event_records",
            sql: "((cash_before IS NULL AND cash_delta IS NULL AND cash_after IS NULL) OR (cash_before IS NOT NULL AND cash_delta IS NOT NULL AND cash_after IS NOT NULL AND cash_after = cash_before + cash_delta))");
    }
}
