using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddB2BRouterPeppolDeliveryR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_invoice_electronic_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    issued_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    snapshot_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    artifact_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    profile = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    profile_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    participant_scheme = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    participant_identifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    document_number = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    submission_attempts = table.Column<int>(type: "int", nullable: false),
                    reconciliation_attempts = table.Column<int>(type: "int", nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    provider_state = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    document_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    allow_email_fallback = table.Column<bool>(type: "bit", nullable: false),
                    fallback_recipient_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    fallback_email_delivery_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    request_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_reconcile_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_electronic_deliveries", x => x.id);
                    table.UniqueConstraint("AK_customer_invoice_electronic_deliveries_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_invoice_electronic_deliveries_customer_invoice_email_deliveries_company_id_fallback_email_delivery_id",
                        columns: x => new { x.company_id, x.fallback_email_delivery_id },
                        principalTable: "customer_invoice_email_deliveries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_electronic_deliveries_customer_invoice_rendered_artifacts_company_id_artifact_id",
                        columns: x => new { x.company_id, x.artifact_id },
                        principalTable: "customer_invoice_rendered_artifacts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_electronic_deliveries_finance_invoices_company_id_invoice_id",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "finance_invoices",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_electronic_deliveries_issued_statutory_documents_company_id_issued_document_id",
                        columns: x => new { x.company_id, x.issued_document_id },
                        principalTable: "issued_statutory_documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_electronic_delivery_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    event_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    provider_state = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    safe_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    evidence_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_electronic_delivery_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_electronic_delivery_events_customer_invoice_electronic_deliveries_company_id_delivery_id",
                        columns: x => new { x.company_id, x.delivery_id },
                        principalTable: "customer_invoice_electronic_deliveries",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_deliveries_company_id_artifact_id",
                table: "customer_invoice_electronic_deliveries",
                columns: new[] { "company_id", "artifact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_deliveries_company_id_fallback_email_delivery_id",
                table: "customer_invoice_electronic_deliveries",
                columns: new[] { "company_id", "fallback_email_delivery_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_deliveries_company_id_idempotency_key",
                table: "customer_invoice_electronic_deliveries",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_deliveries_company_id_invoice_id_created_at",
                table: "customer_invoice_electronic_deliveries",
                columns: new[] { "company_id", "invoice_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_deliveries_company_id_issued_document_id",
                table: "customer_invoice_electronic_deliveries",
                columns: new[] { "company_id", "issued_document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_deliveries_company_id_status_next_reconcile_at",
                table: "customer_invoice_electronic_deliveries",
                columns: new[] { "company_id", "status", "next_reconcile_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_deliveries_provider_key_provider_reference",
                table: "customer_invoice_electronic_deliveries",
                columns: new[] { "provider_key", "provider_reference" },
                unique: true,
                filter: "[provider_reference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_delivery_events_company_id_delivery_id_occurred_at",
                table: "customer_invoice_electronic_delivery_events",
                columns: new[] { "company_id", "delivery_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_electronic_delivery_events_company_id_provider_key_event_key",
                table: "customer_invoice_electronic_delivery_events",
                columns: new[] { "company_id", "provider_key", "event_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_invoice_electronic_delivery_events");

            migrationBuilder.DropTable(
                name: "customer_invoice_electronic_deliveries");
        }
    }
}
