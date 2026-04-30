using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddNormalizedBillExtractions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var provider = migrationBuilder.ActiveProvider ?? string.Empty;
            var isPostgres = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";

            migrationBuilder.CreateTable(
                name: "normalized_bill_extractions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    supplier_name = table.Column<string>(maxLength: 200, nullable: true),
                    supplier_org_number = table.Column<string>(maxLength: 64, nullable: true),
                    invoice_number = table.Column<string>(maxLength: 64, nullable: true),
                    invoice_date = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    due_date = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    currency = table.Column<string>(maxLength: 3, nullable: true),
                    total_amount = table.Column<decimal>(type: decimalType, nullable: true),
                    vat_amount = table.Column<decimal>(type: decimalType, nullable: true),
                    payment_reference = table.Column<string>(maxLength: 128, nullable: true),
                    bankgiro = table.Column<string>(maxLength: 32, nullable: true),
                    plusgiro = table.Column<string>(maxLength: 32, nullable: true),
                    iban = table.Column<string>(maxLength: 34, nullable: true),
                    bic = table.Column<string>(maxLength: 11, nullable: true),
                    confidence = table.Column<string>(maxLength: 16, nullable: false),
                    source_email_id = table.Column<string>(maxLength: 512, nullable: true),
                    source_attachment_id = table.Column<string>(maxLength: 512, nullable: true),
                    evidence_json = table.Column<string>(type: jsonType, nullable: false),
                    validation_status = table.Column<string>(maxLength: 32, nullable: false),
                    validation_findings_json = table.Column<string>(type: jsonType, nullable: false),
                    duplicate_check_id = table.Column<Guid>(type: guidType, nullable: false),
                    requires_review = table.Column<bool>(nullable: false),
                    is_eligible_for_approval_proposal = table.Column<bool>(nullable: false),
                    validation_status_persisted_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_normalized_bill_extractions", x => x.id);
                    table.UniqueConstraint("AK_normalized_bill_extractions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_normalized_bill_extractions_bill_duplicate_checks_duplicate_check_id",
                        column: x => x.duplicate_check_id,
                        principalTable: "bill_duplicate_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_normalized_bill_extractions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.CheckConstraint("CK_normalized_bill_extractions_confidence", "confidence IN ('high', 'medium', 'low')");
                    table.CheckConstraint("CK_normalized_bill_extractions_validation_status", "validation_status IN ('pending', 'valid', 'flagged', 'rejected')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_normalized_bill_extractions_company_id_invoice_number_total_amount",
                table: "normalized_bill_extractions",
                columns: new[] { "company_id", "invoice_number", "total_amount" });

            migrationBuilder.CreateIndex(
                name: "IX_normalized_bill_extractions_company_id_requires_review_created_at",
                table: "normalized_bill_extractions",
                columns: new[] { "company_id", "requires_review", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_normalized_bill_extractions_company_id_supplier_org_number_invoice_number_total_amount",
                table: "normalized_bill_extractions",
                columns: new[] { "company_id", "supplier_org_number", "invoice_number", "total_amount" });

            migrationBuilder.CreateIndex(
                name: "IX_normalized_bill_extractions_company_id_validation_status_created_at",
                table: "normalized_bill_extractions",
                columns: new[] { "company_id", "validation_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_normalized_bill_extractions_duplicate_check_id",
                table: "normalized_bill_extractions",
                column: "duplicate_check_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "normalized_bill_extractions");
        }
    }
}
