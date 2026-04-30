using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceBillInboxReview : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_bill_review_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    detected_bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    proposal_summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_bill_review_states", x => x.id);
                    table.UniqueConstraint("AK_finance_bill_review_states_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_bill_review_states_status", "status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')");
                    table.ForeignKey("FK_finance_bill_review_states_companies_company_id", x => x.company_id, "companies", "id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_finance_bill_review_states_detected_bills_detected_bill_id", x => x.detected_bill_id, "detected_bills", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_bill_review_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    review_state_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    detected_bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    prior_status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    new_status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    rationale = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_bill_review_actions", x => x.id);
                    table.CheckConstraint("CK_finance_bill_review_actions_new_status", "new_status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')");
                    table.CheckConstraint("CK_finance_bill_review_actions_prior_status", "prior_status IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')");
                    table.ForeignKey("FK_finance_bill_review_actions_detected_bills_detected_bill_id", x => x.detected_bill_id, "detected_bills", "id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_finance_bill_review_actions_finance_bill_review_states_review_state_id", x => x.review_state_id, "finance_bill_review_states", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bill_approval_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    detected_bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    review_state_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    payment_execution_requested = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_approval_proposals", x => x.id);
                    table.CheckConstraint("CK_bill_approval_proposals_no_payment_execution", "payment_execution_requested = 0");
                    table.ForeignKey("FK_bill_approval_proposals_detected_bills_detected_bill_id", x => x.detected_bill_id, "detected_bills", "id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_bill_approval_proposals_finance_bill_review_states_review_state_id", x => x.review_state_id, "finance_bill_review_states", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bill_review_states_company_id_detected_bill_id",
                table: "finance_bill_review_states",
                columns: new[] { "company_id", "detected_bill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_bill_review_states_company_id_status_updated_at",
                table: "finance_bill_review_states",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bill_review_states_detected_bill_id",
                table: "finance_bill_review_states",
                column: "detected_bill_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_bill_review_actions_company_id_detected_bill_id_occurred_at",
                table: "finance_bill_review_actions",
                columns: new[] { "company_id", "detected_bill_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_bill_review_actions_detected_bill_id",
                table: "finance_bill_review_actions",
                column: "detected_bill_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_bill_review_actions_review_state_id",
                table: "finance_bill_review_actions",
                column: "review_state_id");

            migrationBuilder.CreateIndex(
                name: "IX_bill_approval_proposals_company_id_detected_bill_id",
                table: "bill_approval_proposals",
                columns: new[] { "company_id", "detected_bill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bill_approval_proposals_detected_bill_id",
                table: "bill_approval_proposals",
                column: "detected_bill_id");

            migrationBuilder.CreateIndex(
                name: "IX_bill_approval_proposals_review_state_id",
                table: "bill_approval_proposals",
                column: "review_state_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "bill_approval_proposals");
            migrationBuilder.DropTable(name: "finance_bill_review_actions");
            migrationBuilder.DropTable(name: "finance_bill_review_states");
        }
    }
}