using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260825233000_AddCustomerInvoicePdfDeliveryR2")]
public partial class AddCustomerInvoicePdfDeliveryR2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_invoice_rendered_artifacts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), issued_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), snapshot_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), template_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false), media_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false), file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false), status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true), content_length = table.Column<long>(type: "bigint", nullable: true), object_key = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true), generation_attempts = table.Column<int>(type: "int", nullable: false), failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true), failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), updated_at = table.Column<DateTime>(type: "datetime2", nullable: false), rendered_at = table.Column<DateTime>(type: "datetime2", nullable: true)
            }, constraints: table => { table.PrimaryKey("PK_customer_invoice_rendered_artifacts", x => x.id); table.UniqueConstraint("AK_customer_invoice_rendered_artifacts_company_id_id", x => new { x.company_id, x.id }); table.ForeignKey("FK_customer_invoice_rendered_artifacts_finance_invoices_company_id_invoice_id", x => new { x.company_id, x.invoice_id }, "finance_invoices", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_customer_invoice_rendered_artifacts_issued_statutory_documents_company_id_issued_document_id", x => new { x.company_id, x.issued_document_id }, "issued_statutory_documents", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateTable(
            name: "customer_invoice_email_deliveries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), artifact_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), recipient_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false), recipient_snapshot_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false), subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false), reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false), idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false), status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), attempts = table.Column<int>(type: "int", nullable: false), provider_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true), failure_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true), failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true), requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), updated_at = table.Column<DateTime>(type: "datetime2", nullable: false), accepted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
            }, constraints: table => { table.PrimaryKey("PK_customer_invoice_email_deliveries", x => x.id); table.UniqueConstraint("AK_customer_invoice_email_deliveries_company_id_id", x => new { x.company_id, x.id }); table.ForeignKey("FK_customer_invoice_email_deliveries_customer_invoice_rendered_artifacts_company_id_artifact_id", x => new { x.company_id, x.artifact_id }, "customer_invoice_rendered_artifacts", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_customer_invoice_email_deliveries_finance_invoices_company_id_invoice_id", x => new { x.company_id, x.invoice_id }, "finance_invoices", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_rendered_artifacts_company_id_invoice_id_snapshot_hash_template_version_locale", table: "customer_invoice_rendered_artifacts", columns: new[] { "company_id", "invoice_id", "snapshot_hash", "template_version", "locale" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_rendered_artifacts_company_id_issued_document_id", table: "customer_invoice_rendered_artifacts", columns: new[] { "company_id", "issued_document_id" });
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_rendered_artifacts_company_id_status_updated_at", table: "customer_invoice_rendered_artifacts", columns: new[] { "company_id", "status", "updated_at" });
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_email_deliveries_company_id_idempotency_key", table: "customer_invoice_email_deliveries", columns: new[] { "company_id", "idempotency_key" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_email_deliveries_company_id_artifact_id", table: "customer_invoice_email_deliveries", columns: new[] { "company_id", "artifact_id" });
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_email_deliveries_company_id_invoice_id_created_at", table: "customer_invoice_email_deliveries", columns: new[] { "company_id", "invoice_id", "created_at" });
        migrationBuilder.CreateIndex(name: "IX_customer_invoice_email_deliveries_company_id_status_updated_at", table: "customer_invoice_email_deliveries", columns: new[] { "company_id", "status", "updated_at" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable(name: "customer_invoice_email_deliveries"); migrationBuilder.DropTable(name: "customer_invoice_rendered_artifacts"); }
}
