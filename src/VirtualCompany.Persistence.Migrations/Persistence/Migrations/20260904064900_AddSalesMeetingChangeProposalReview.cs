using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingChangeProposalReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "next_step",
                table: "deals",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "probability",
                table: "deals",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_meeting_change_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    evidence_artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    field = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    value_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    proposed_value_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    before_value_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    target_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    source_ids_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    evidence_version_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    risk_class = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    policy_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approval_binding_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    execution_attempt_count = table.Column<int>(type: "int", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    executed_before_value_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    executed_after_value_json = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    provider_reference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    last_error_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    executed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_change_proposals", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_change_proposals_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_change_proposals_attempts", "execution_attempt_count >= 0");
                    table.CheckConstraint("CK_sales_meeting_change_proposals_confidence", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "FK_sales_meeting_change_proposals_sales_meeting_artifacts_company_id_evidence_artifact_id",
                        columns: x => new { x.company_id, x.evidence_artifact_id },
                        principalTable: "sales_meeting_artifacts",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_sales_meeting_change_proposals_sales_meeting_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "sales_meeting_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_proposals_company_id_evidence_artifact_id",
                table: "sales_meeting_change_proposals",
                columns: new[] { "company_id", "evidence_artifact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_proposals_company_id_idempotency_key",
                table: "sales_meeting_change_proposals",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_proposals_company_id_session_id_status",
                table: "sales_meeting_change_proposals",
                columns: new[] { "company_id", "session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_proposals_company_id_target_type_target_id",
                table: "sales_meeting_change_proposals",
                columns: new[] { "company_id", "target_type", "target_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_change_proposals");

            migrationBuilder.DropColumn(
                name: "next_step",
                table: "deals");

            migrationBuilder.DropColumn(
                name: "probability",
                table: "deals");
        }
    }
}
