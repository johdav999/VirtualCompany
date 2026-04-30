using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddDetectedBillSchemas : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var provider = migrationBuilder.ActiveProvider ?? string.Empty;
            var isPostgres = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
            var guidType = isPostgres ? "uuid" : "uniqueidentifier";
            var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
            var decimalType = isPostgres ? "numeric(18,2)" : "decimal(18,2)";
            var scoreType = isPostgres ? "numeric(5,4)" : "decimal(5,4)";
            var jsonType = isPostgres ? "jsonb" : "nvarchar(max)";

            migrationBuilder.AddColumn<string>(
                name: "result_status",
                table: "bill_duplicate_checks",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "not_duplicate");

            migrationBuilder.CreateTable(
                name: "detected_bills",
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
                    confidence = table.Column<decimal>(type: scoreType, nullable: true),
                    confidence_level = table.Column<string>(maxLength: 16, nullable: false),
                    validation_status = table.Column<string>(maxLength: 32, nullable: false),
                    review_status = table.Column<string>(maxLength: 32, nullable: false),
                    requires_review = table.Column<bool>(nullable: false),
                    is_eligible_for_approval_proposal = table.Column<bool>(nullable: false),
                    validation_status_persisted = table.Column<bool>(nullable: false),
                    validation_status_persisted_at = table.Column<DateTime>(type: dateTimeType, nullable: true),
                    validation_issues_json = table.Column<string>(type: jsonType, nullable: false),
                    source_email_id = table.Column<string>(maxLength: 512, nullable: true),
                    source_attachment_id = table.Column<string>(maxLength: 512, nullable: true),
                    duplicate_check_id = table.Column<Guid>(type: guidType, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    updated_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detected_bills", x => x.id);
                    table.UniqueConstraint("AK_detected_bills_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_detected_bills_bill_duplicate_checks_duplicate_check_id",
                        column: x => x.duplicate_check_id,
                        principalTable: "bill_duplicate_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_detected_bills_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.CheckConstraint("CK_detected_bills_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.CheckConstraint("CK_detected_bills_confidence_level", "confidence_level IN ('high', 'medium', 'low')");
                    table.CheckConstraint("CK_detected_bills_review_status", "review_status IN ('not_required', 'required', 'completed')");
                    table.CheckConstraint("CK_detected_bills_validation_status", "validation_status IN ('pending', 'valid', 'flagged', 'rejected')");
                });

            migrationBuilder.CreateTable(
                name: "detected_bill_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: guidType, nullable: false),
                    company_id = table.Column<Guid>(type: guidType, nullable: false),
                    detected_bill_id = table.Column<Guid>(type: guidType, nullable: false),
                    field_name = table.Column<string>(maxLength: 64, nullable: false),
                    raw_value = table.Column<string>(maxLength: 2000, nullable: true),
                    normalized_value = table.Column<string>(maxLength: 2000, nullable: true),
                    source_document = table.Column<string>(maxLength: 512, nullable: false),
                    source_document_type = table.Column<string>(maxLength: 64, nullable: true),
                    page_reference = table.Column<string>(maxLength: 128, nullable: true),
                    section_reference = table.Column<string>(maxLength: 128, nullable: true),
                    text_span = table.Column<string>(maxLength: 128, nullable: true),
                    locator = table.Column<string>(maxLength: 512, nullable: true),
                    extraction_method = table.Column<string>(maxLength: 64, nullable: false),
                    field_confidence = table.Column<decimal>(type: scoreType, nullable: true),
                    snippet = table.Column<string>(maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: dateTimeType, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detected_bill_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_detected_bill_fields_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_detected_bill_fields_detected_bills_detected_bill_id",
                        column: x => x.detected_bill_id,
                        principalTable: "detected_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.CheckConstraint("CK_detected_bill_fields_field_confidence", "field_confidence IS NULL OR (field_confidence >= 0 AND field_confidence <= 1)");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_bill_duplicate_checks_result_status",
                table: "bill_duplicate_checks",
                sql: "result_status IN ('pending', 'not_duplicate', 'duplicate', 'inconclusive')");

            migrationBuilder.CreateIndex(
                name: "IX_bill_duplicate_checks_company_id_result_status_checked_at",
                table: "bill_duplicate_checks",
                columns: new[] { "company_id", "result_status", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bill_fields_company_id_detected_bill_id_field_name",
                table: "detected_bill_fields",
                columns: new[] { "company_id", "detected_bill_id", "field_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detected_bill_fields_company_id_field_name",
                table: "detected_bill_fields",
                columns: new[] { "company_id", "field_name" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bill_fields_detected_bill_id",
                table: "detected_bill_fields",
                column: "detected_bill_id");

            migrationBuilder.CreateIndex(
                name: "IX_detected_bills_company_id_confidence_level_requires_review_created_at",
                table: "detected_bills",
                columns: new[] { "company_id", "confidence_level", "requires_review", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bills_company_id_source_attachment_id",
                table: "detected_bills",
                columns: new[] { "company_id", "source_attachment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bills_company_id_source_email_id",
                table: "detected_bills",
                columns: new[] { "company_id", "source_email_id" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bills_company_id_supplier_name_invoice_number_total_amount",
                table: "detected_bills",
                columns: new[] { "company_id", "supplier_name", "invoice_number", "total_amount" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bills_company_id_supplier_org_number_invoice_number_total_amount",
                table: "detected_bills",
                columns: new[] { "company_id", "supplier_org_number", "invoice_number", "total_amount" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bills_company_id_validation_status_created_at",
                table: "detected_bills",
                columns: new[] { "company_id", "validation_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_detected_bills_duplicate_check_id",
                table: "detected_bills",
                column: "duplicate_check_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "detected_bill_fields");
            migrationBuilder.DropTable(name: "detected_bills");
            migrationBuilder.DropIndex(
                name: "IX_bill_duplicate_checks_company_id_result_status_checked_at",
                table: "bill_duplicate_checks");
            migrationBuilder.DropCheckConstraint(
                name: "CK_bill_duplicate_checks_result_status",
                table: "bill_duplicate_checks");
            migrationBuilder.DropColumn(
                name: "result_status",
                table: "bill_duplicate_checks");
        }
    }
}
