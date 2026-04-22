using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddPaymentAllocationSimulationCausality : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";

            migrationBuilder.AddColumn<Guid>(
                name: "source_simulation_event_record_id",
                table: "payment_allocations",
                type: guidType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "payment_source_simulation_event_record_id",
                table: "payment_allocations",
                type: guidType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_source_simulation_event_record_id",
                table: "payment_allocations",
                type: guidType,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_source_simulation_event_record_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "source_simulation_event_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_payment_source_simulation_event_record_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "payment_source_simulation_event_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_target_source_simulation_event_record_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "target_source_simulation_event_record_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_allocations_simulation_event_records_company_id_source_simulation_event_record_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_allocations_simulation_event_records_company_id_payment_source_simulation_event_record_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "payment_source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_allocations_simulation_event_records_company_id_target_source_simulation_event_record_id",
                table: "payment_allocations",
                columns: new[] { "company_id", "target_source_simulation_event_record_id" },
                principalTable: "simulation_event_records",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                UPDATE payment_allocations
                SET payment_source_simulation_event_record_id = (
                        SELECT p.source_simulation_event_record_id
                        FROM finance_payments p
                        WHERE p.company_id = payment_allocations.company_id
                          AND p.id = payment_allocations.payment_id
                    ),
                    target_source_simulation_event_record_id = COALESCE(
                        (
                            SELECT i.source_simulation_event_record_id
                            FROM finance_invoices i
                            WHERE i.company_id = payment_allocations.company_id
                              AND i.id = payment_allocations.invoice_id
                        ),
                        (
                            SELECT b.source_simulation_event_record_id
                            FROM finance_bills b
                            WHERE b.company_id = payment_allocations.company_id
                              AND b.id = payment_allocations.bill_id
                        )
                    ),
                    source_simulation_event_record_id = COALESCE(
                        (
                            SELECT p.source_simulation_event_record_id
                            FROM finance_payments p
                            WHERE p.company_id = payment_allocations.company_id
                              AND p.id = payment_allocations.payment_id
                        ),
                        (
                            SELECT i.source_simulation_event_record_id
                            FROM finance_invoices i
                            WHERE i.company_id = payment_allocations.company_id
                              AND i.id = payment_allocations.invoice_id
                        ),
                        (
                            SELECT b.source_simulation_event_record_id
                            FROM finance_bills b
                            WHERE b.company_id = payment_allocations.company_id
                              AND b.id = payment_allocations.bill_id
                        )
                    );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_payment_allocations_simulation_event_records_company_id_source_simulation_event_record_id", table: "payment_allocations");
            migrationBuilder.DropForeignKey(name: "FK_payment_allocations_simulation_event_records_company_id_payment_source_simulation_event_record_id", table: "payment_allocations");
            migrationBuilder.DropForeignKey(name: "FK_payment_allocations_simulation_event_records_company_id_target_source_simulation_event_record_id", table: "payment_allocations");

            migrationBuilder.DropIndex(name: "IX_payment_allocations_company_id_source_simulation_event_record_id", table: "payment_allocations");
            migrationBuilder.DropIndex(name: "IX_payment_allocations_company_id_payment_source_simulation_event_record_id", table: "payment_allocations");
            migrationBuilder.DropIndex(name: "IX_payment_allocations_company_id_target_source_simulation_event_record_id", table: "payment_allocations");

            migrationBuilder.DropColumn(name: "source_simulation_event_record_id", table: "payment_allocations");
            migrationBuilder.DropColumn(name: "payment_source_simulation_event_record_id", table: "payment_allocations");
            migrationBuilder.DropColumn(name: "target_source_simulation_event_record_id", table: "payment_allocations");
        }
    }
}