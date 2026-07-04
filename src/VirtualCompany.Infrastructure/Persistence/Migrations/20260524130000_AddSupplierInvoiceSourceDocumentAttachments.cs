using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524130000_AddSupplierInvoiceSourceDocumentAttachments")]
    public partial class AddSupplierInvoiceSourceDocumentAttachments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_invoice_source_document_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bill_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "not_attached"),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attached_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    response_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    provider_metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    audit_trail_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_invoice_source_document_attachments", x => x.id);
                    table.UniqueConstraint("AK_supplier_invoice_source_document_attachments_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint(
                        "CK_supplier_invoice_source_document_attachments_status",
                        "status IN ('not_attached', 'attachment_requested', 'attached', 'failed', 'not_available')");
                    table.ForeignKey(
                        name: "FK_supplier_invoice_source_document_attachments_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_source_document_attachments_finance_bills_company_id_bill_id",
                        columns: x => new { x.company_id, x.bill_id },
                        principalTable: "finance_bills",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_source_document_attachments_knowledge_documents_company_id_document_id",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_source_document_attachments_company_id_bill_id",
                table: "supplier_invoice_source_document_attachments",
                columns: new[] { "company_id", "bill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_source_document_attachments_company_id_status_updated_at",
                table: "supplier_invoice_source_document_attachments",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_source_document_attachments_company_id_document_id",
                table: "supplier_invoice_source_document_attachments",
                columns: new[] { "company_id", "document_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "supplier_invoice_source_document_attachments");
        }
    }
}
