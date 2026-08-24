using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAllocationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "payment_allocations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_allocations_company_id_idempotency_key",
                table: "payment_allocations",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "[idempotency_key] IS NOT NULL");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_allocations_company_id_idempotency_key",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "payment_allocations");
        }
    }
}
