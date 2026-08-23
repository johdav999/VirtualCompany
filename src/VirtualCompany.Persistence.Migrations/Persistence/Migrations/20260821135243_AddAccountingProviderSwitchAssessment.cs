using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingProviderSwitchAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    work_index = table.Column<int>(type: "int", nullable: false),
                    total_work_items = table.Column<int>(type: "int", nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_assessments", x => x.id);
                    table.UniqueConstraint("AK_accounting_provider_switch_assessments_company_id_id", x => new { x.company_id, x.id });
                    table.UniqueConstraint("AK_accounting_provider_switch_assessments_company_id_switch_id_id", x => new { x.company_id, x.switch_id, x.id });
                    table.CheckConstraint("CK_accounting_provider_switch_assessments_progress", "[work_index] >= 0 AND [work_index] <= [total_work_items] AND [total_work_items] > 0");
                    table.CheckConstraint("CK_accounting_provider_switch_assessments_status", "[status] IN ('queued', 'running', 'completed', 'failed')");
                    table.CheckConstraint("CK_accounting_provider_switch_assessments_version", "[version] > 0");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_assessments_accounting_provider_switches_company_id_switch_id",
                        columns: x => new { x.company_id, x.switch_id },
                        principalTable: "accounting_provider_switches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_assessments_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_capabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    endpoint_role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    capability_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    required_scope = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_capabilities", x => x.id);
                    table.CheckConstraint("CK_accounting_provider_switch_capabilities_level", "[level] IN ('supported', 'partial', 'unsupported', 'unknown')");
                    table.CheckConstraint("CK_accounting_provider_switch_capabilities_role", "[endpoint_role] IN ('source', 'target')");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_capabilities_accounting_provider_switch_assessments_company_id_switch_id_assessment_id",
                        columns: x => new { x.company_id, x.switch_id, x.assessment_id },
                        principalTable: "accounting_provider_switch_assessments",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_capabilities_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_datasets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    endpoint_role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    dataset_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    availability = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    capability_level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    record_count = table.Column<long>(type: "bigint", nullable: false),
                    financial_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    source_cursor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    integrity_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    extracted_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_datasets", x => x.id);
                    table.CheckConstraint("CK_accounting_provider_switch_datasets_availability", "[availability] IN ('available', 'confirmed_absent', 'not_returned', 'not_authorized', 'unsupported', 'unknown')");
                    table.CheckConstraint("CK_accounting_provider_switch_datasets_capability", "[capability_level] IN ('supported', 'partial', 'unsupported', 'unknown')");
                    table.CheckConstraint("CK_accounting_provider_switch_datasets_count", "[record_count] >= 0");
                    table.CheckConstraint("CK_accounting_provider_switch_datasets_role", "[endpoint_role] IN ('source', 'target')");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_datasets_accounting_provider_switch_assessments_company_id_switch_id_assessment_id",
                        columns: x => new { x.company_id, x.switch_id, x.assessment_id },
                        principalTable: "accounting_provider_switch_assessments",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_datasets_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "accounting_provider_switch_gaps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    switch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    dataset_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    is_blocking = table.Column<bool>(type: "bit", nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    operator_action = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_provider_switch_gaps", x => x.id);
                    table.CheckConstraint("CK_accounting_provider_switch_gaps_severity", "[severity] IN ('information', 'warning', 'blocking')");
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_gaps_accounting_provider_switch_assessments_company_id_switch_id_assessment_id",
                        columns: x => new { x.company_id, x.switch_id, x.assessment_id },
                        principalTable: "accounting_provider_switch_assessments",
                        principalColumns: new[] { "company_id", "switch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_provider_switch_gaps_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_assessments_company_id_switch_id_idempotency_key",
                table: "accounting_provider_switch_assessments",
                columns: new[] { "company_id", "switch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_assessments_company_id_switch_id_requested_at",
                table: "accounting_provider_switch_assessments",
                columns: new[] { "company_id", "switch_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_assessments_status_next_attempt_at_lease_expires_at",
                table: "accounting_provider_switch_assessments",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_capabilities_company_id_assessment_id_endpoint_role_capability_key",
                table: "accounting_provider_switch_capabilities",
                columns: new[] { "company_id", "assessment_id", "endpoint_role", "capability_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_capabilities_company_id_switch_id_assessment_id",
                table: "accounting_provider_switch_capabilities",
                columns: new[] { "company_id", "switch_id", "assessment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_datasets_company_id_assessment_id_endpoint_role_dataset_key",
                table: "accounting_provider_switch_datasets",
                columns: new[] { "company_id", "assessment_id", "endpoint_role", "dataset_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_datasets_company_id_switch_id_assessment_id",
                table: "accounting_provider_switch_datasets",
                columns: new[] { "company_id", "switch_id", "assessment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_gaps_company_id_assessment_id_reason_code_dataset_key",
                table: "accounting_provider_switch_gaps",
                columns: new[] { "company_id", "assessment_id", "reason_code", "dataset_key" },
                unique: true,
                filter: "[dataset_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_gaps_company_id_switch_id_assessment_id",
                table: "accounting_provider_switch_gaps",
                columns: new[] { "company_id", "switch_id", "assessment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_provider_switch_gaps_company_id_switch_id_is_blocking",
                table: "accounting_provider_switch_gaps",
                columns: new[] { "company_id", "switch_id", "is_blocking" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_provider_switch_capabilities");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_datasets");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_gaps");

            migrationBuilder.DropTable(
                name: "accounting_provider_switch_assessments");
        }
    }
}
