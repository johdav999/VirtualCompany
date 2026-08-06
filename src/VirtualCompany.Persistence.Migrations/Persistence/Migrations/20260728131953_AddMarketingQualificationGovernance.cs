using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingQualificationGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "marketing_plans",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "marketing_qualification_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    audience_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    required_channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    threshold = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    freshness_days = table.Column<int>(type: "int", nullable: false),
                    requires_customer_company = table.Column<bool>(type: "bit", nullable: false),
                    effective_from_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    effective_to_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    rules_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    exclusions_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_qualification_definitions", x => x.id);
                    table.UniqueConstraint("AK_marketing_qualification_definitions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_qualification_evaluations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_qualification_definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_version = table.Column<int>(type: "int", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_codes_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_references_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    evaluated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_qualification_evaluations", x => x.id);
                    table.UniqueConstraint("AK_marketing_qualification_evaluations_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_qualification_evaluations_marketing_qualification_definitions_company_id_marketing_qualification_definition_id",
                        columns: x => new { x.company_id, x.marketing_qualification_definition_id },
                        principalTable: "marketing_qualification_definitions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "marketing_qualification_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    marketing_qualification_evaluation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    linked_lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    linked_deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_qualification_feedback", x => x.id);
                    table.UniqueConstraint("AK_marketing_qualification_feedback_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_marketing_qualification_feedback_marketing_qualification_evaluations_company_id_marketing_qualification_evaluation_id",
                        columns: x => new { x.company_id, x.marketing_qualification_evaluation_id },
                        principalTable: "marketing_qualification_evaluations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_plans_company_id_idempotency_key",
                table: "marketing_plans",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "[idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_definitions_company_id",
                table: "marketing_qualification_definitions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_definitions_company_id_audience_type_status_effective_from_at",
                table: "marketing_qualification_definitions",
                columns: new[] { "company_id", "audience_type", "status", "effective_from_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_evaluations_company_id",
                table: "marketing_qualification_evaluations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_evaluations_company_id_idempotency_key",
                table: "marketing_qualification_evaluations",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_evaluations_company_id_marketing_qualification_definition_id",
                table: "marketing_qualification_evaluations",
                columns: new[] { "company_id", "marketing_qualification_definition_id" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_evaluations_company_id_status_evaluated_at",
                table: "marketing_qualification_evaluations",
                columns: new[] { "company_id", "status", "evaluated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_feedback_company_id",
                table: "marketing_qualification_feedback",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_qualification_feedback_company_id_marketing_qualification_evaluation_id_created_at",
                table: "marketing_qualification_feedback",
                columns: new[] { "company_id", "marketing_qualification_evaluation_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_qualification_feedback");

            migrationBuilder.DropTable(
                name: "marketing_qualification_evaluations");

            migrationBuilder.DropTable(
                name: "marketing_qualification_definitions");

            migrationBuilder.DropIndex(
                name: "IX_marketing_plans_company_id_idempotency_key",
                table: "marketing_plans");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "marketing_plans");
        }
    }
}
