using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceMarketingJourneyRuntimeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "marketing_journey_enrollments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "concurrency_version",
                table: "marketing_journey_enrollments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "last_evaluation_json",
                table: "marketing_journey_enrollments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "lease_expires_at",
                table: "marketing_journey_enrollments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "marketing_journey_enrollments",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maximum_attempts",
                table: "marketing_journey_enrollments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_at",
                table: "marketing_journey_enrollments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "marketing_journey_inbound_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    journey_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    journey_version = table.Column<int>(type: "int", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    event_reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    occurrence_version = table.Column<int>(type: "int", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_journey_inbound_events", x => x.id);
                    table.UniqueConstraint("AK_marketing_journey_inbound_events_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_journey_step_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    journey_version = table.Column<int>(type: "int", nullable: false),
                    step_index = table.Column<int>(type: "int", nullable: false),
                    attempt = table.Column<int>(type: "int", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    policy_evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    channel_action_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_journey_step_attempts", x => x.id);
                    table.UniqueConstraint("AK_marketing_journey_step_attempts_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_inbound_events_company_id",
                table: "marketing_journey_inbound_events",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_inbound_events_company_id_idempotency_key",
                table: "marketing_journey_inbound_events",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_inbound_events_company_id_journey_id_journey_version_contact_id_event_type_event_reference_occurrence_vers~",
                table: "marketing_journey_inbound_events",
                columns: new[] { "company_id", "journey_id", "journey_version", "contact_id", "event_type", "event_reference", "occurrence_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_step_attempts_company_id",
                table: "marketing_journey_step_attempts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_journey_step_attempts_company_id_enrollment_id_journey_version_step_index_attempt",
                table: "marketing_journey_step_attempts",
                columns: new[] { "company_id", "enrollment_id", "journey_version", "step_index", "attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_journey_inbound_events");

            migrationBuilder.DropTable(
                name: "marketing_journey_step_attempts");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "marketing_journey_enrollments");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "marketing_journey_enrollments");

            migrationBuilder.DropColumn(
                name: "last_evaluation_json",
                table: "marketing_journey_enrollments");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "marketing_journey_enrollments");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "marketing_journey_enrollments");

            migrationBuilder.DropColumn(
                name: "maximum_attempts",
                table: "marketing_journey_enrollments");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "marketing_journey_enrollments");
        }
    }
}
