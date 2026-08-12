using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingMeasurementLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketing_attribution_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    result_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    touch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    weight = table.Column<decimal>(type: "decimal(8,7)", precision: 8, scale: 7, nullable: false),
                    attributed_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    evidence_version = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_attribution_allocations", x => x.id);
                    table.UniqueConstraint("AK_marketing_attribution_allocations_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_attribution_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    model_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    rules_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    limitations = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    lookback_days = table.Column<int>(type: "int", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_attribution_models", x => x.id);
                    table.UniqueConstraint("AK_marketing_attribution_models_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_attribution_touches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    touch_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    source_version = table.Column<int>(type: "int", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_attribution_touches", x => x.id);
                    table.UniqueConstraint("AK_marketing_attribution_touches_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_experiment_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    experiment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decision = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    sample_size = table.Column<int>(type: "int", nullable: false),
                    contamination_rate = table.Column<decimal>(type: "decimal(6,5)", precision: 6, scale: 5, nullable: false),
                    guardrail_breached = table.Column<bool>(type: "bit", nullable: false),
                    causal_eligible = table.Column<bool>(type: "bit", nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    limitations = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_experiment_decisions", x => x.id);
                    table.UniqueConstraint("AK_marketing_experiment_decisions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_experiment_exposures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    experiment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    variant = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    assignment_key = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    exposed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_experiment_exposures", x => x.id);
                    table.UniqueConstraint("AK_marketing_experiment_exposures_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "marketing_segment_learning_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    metrics_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    proposed_changes_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_segment_learning_proposals", x => x.id);
                    table.UniqueConstraint("AK_marketing_segment_learning_proposals_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_allocations_company_id",
                table: "marketing_attribution_allocations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_allocations_company_id_result_id_touch_id",
                table: "marketing_attribution_allocations",
                columns: new[] { "company_id", "result_id", "touch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_models_company_id",
                table: "marketing_attribution_models",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_models_company_id_idempotency_key",
                table: "marketing_attribution_models",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_models_company_id_name_version",
                table: "marketing_attribution_models",
                columns: new[] { "company_id", "name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_touches_company_id",
                table: "marketing_attribution_touches",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_touches_company_id_idempotency_key",
                table: "marketing_attribution_touches",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_attribution_touches_company_id_subject_type_subject_id_occurred_at",
                table: "marketing_attribution_touches",
                columns: new[] { "company_id", "subject_type", "subject_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_experiment_decisions_company_id",
                table: "marketing_experiment_decisions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_experiment_decisions_company_id_experiment_id",
                table: "marketing_experiment_decisions",
                columns: new[] { "company_id", "experiment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_experiment_exposures_company_id",
                table: "marketing_experiment_exposures",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_experiment_exposures_company_id_experiment_id_assignment_key",
                table: "marketing_experiment_exposures",
                columns: new[] { "company_id", "experiment_id", "assignment_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_learning_proposals_company_id",
                table: "marketing_segment_learning_proposals",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_learning_proposals_company_id_idempotency_key",
                table: "marketing_segment_learning_proposals",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_segment_learning_proposals_company_id_segment_version_id_created_at",
                table: "marketing_segment_learning_proposals",
                columns: new[] { "company_id", "segment_version_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_attribution_allocations");

            migrationBuilder.DropTable(
                name: "marketing_attribution_models");

            migrationBuilder.DropTable(
                name: "marketing_attribution_touches");

            migrationBuilder.DropTable(
                name: "marketing_experiment_decisions");

            migrationBuilder.DropTable(
                name: "marketing_experiment_exposures");

            migrationBuilder.DropTable(
                name: "marketing_segment_learning_proposals");
        }
    }
}
