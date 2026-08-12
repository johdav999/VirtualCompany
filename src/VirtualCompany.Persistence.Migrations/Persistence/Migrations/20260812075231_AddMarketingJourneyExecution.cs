using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingJourneyExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_journey_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_lifecycle_journey_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    journey_version = table.Column<int>(type: "int", nullable: false),
                    consent_evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    next_step_index = table.Column<int>(type: "int", nullable: false),
                    next_step_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    actions_in_window = table.Column<int>(type: "int", nullable: false),
                    window_started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_channel_action_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_journey_enrollments", x => x.id);
                    table.UniqueConstraint("AK_marketing_journey_enrollments_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_enrollments_company_id",
                table: "marketing_journey_enrollments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_enrollments_company_id_idempotency_key",
                table: "marketing_journey_enrollments",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_enrollments_company_id_marketing_lifecycle_journey_id_contact_id",
                table: "marketing_journey_enrollments",
                columns: new[] { "company_id", "marketing_lifecycle_journey_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_enrollments_company_id_status_next_step_at",
                table: "marketing_journey_enrollments",
                columns: new[] { "company_id", "status", "next_step_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_journey_enrollments");
        }
    }
}
