using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdHocPresentationRunsAndLegacyCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                table: "sales_presentation_runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "context_contact_id",
                table: "sales_presentation_runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "context_customer_company_id",
                table: "sales_presentation_runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "context_deal_id",
                table: "sales_presentation_runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "context_lead_id",
                table: "sales_presentation_runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "runtime_strategy",
                table: "sales_presentation_runs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "meeting_session");

            migrationBuilder.CreateTable(
                name: "sales_presentation_legacy_compatibility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deck_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    presentation_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    disposition = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_presentation_legacy_compatibility", x => x.id);
                    table.UniqueConstraint("AK_sales_presentation_legacy_compatibility_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_presentation_legacy_compatibility_attempts", "attempt_count >= 0");
                    table.ForeignKey(
                        name: "FK_sales_presentation_legacy_compatibility_sales_presentation_decks_company_id_deck_id",
                        columns: x => new { x.company_id, x.deck_id },
                        principalTable: "sales_presentation_decks",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_presentation_legacy_compatibility_sales_presentation_runs_company_id_presentation_run_id",
                        columns: x => new { x.company_id, x.presentation_run_id },
                        principalTable: "sales_presentation_runs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_client_request_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "client_request_id" },
                unique: true,
                filter: "[client_request_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_context_contact_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_context_customer_company_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_customer_company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_context_deal_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_deal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_runs_company_id_context_lead_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_lead_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_legacy_compatibility_company_id_deck_id",
                table: "sales_presentation_legacy_compatibility",
                columns: new[] { "company_id", "deck_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_legacy_compatibility_company_id_presentation_run_id",
                table: "sales_presentation_legacy_compatibility",
                columns: new[] { "company_id", "presentation_run_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_presentation_legacy_compatibility_company_id_status_updated_at",
                table: "sales_presentation_legacy_compatibility",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_runs_contacts_company_id_context_contact_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_contact_id" },
                principalTable: "contacts",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_runs_customer_companies_company_id_context_customer_company_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_customer_company_id" },
                principalTable: "customer_companies",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_runs_deals_company_id_context_deal_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_deal_id" },
                principalTable: "deals",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_presentation_runs_leads_company_id_context_lead_id",
                table: "sales_presentation_runs",
                columns: new[] { "company_id", "context_lead_id" },
                principalTable: "leads",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_presentation_runs_contacts_company_id_context_contact_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_presentation_runs_customer_companies_company_id_context_customer_company_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_presentation_runs_deals_company_id_context_deal_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_presentation_runs_leads_company_id_context_lead_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropTable(
                name: "sales_presentation_legacy_compatibility");

            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_runs_company_id_client_request_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_runs_company_id_context_contact_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_runs_company_id_context_customer_company_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_runs_company_id_context_deal_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropIndex(
                name: "IX_sales_presentation_runs_company_id_context_lead_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropColumn(
                name: "context_contact_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropColumn(
                name: "context_customer_company_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropColumn(
                name: "context_deal_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropColumn(
                name: "context_lead_id",
                table: "sales_presentation_runs");

            migrationBuilder.DropColumn(
                name: "runtime_strategy",
                table: "sales_presentation_runs");
        }
    }
}
