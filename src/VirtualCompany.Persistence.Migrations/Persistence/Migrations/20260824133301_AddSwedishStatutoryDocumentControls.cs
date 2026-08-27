using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSwedishStatutoryDocumentControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issued_statutory_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    authority = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    document_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    fiscal_year_key = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    sequence_number = table.Column<long>(type: "bigint", nullable: true),
                    statutory_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    statutory_profile_version = table.Column<long>(type: "bigint", nullable: false),
                    policy_pack_key = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    policy_pack_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    policy_pack_definition_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 32768, nullable: false),
                    snapshot_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    tax_facts_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16384, nullable: false),
                    approval_ids_json = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    business_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    original_issued_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    issued_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    issued_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    rendered_evidence_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    delivery_evidence_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    evidence_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issued_statutory_documents", x => x.id);
                    table.UniqueConstraint("AK_issued_statutory_documents_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_issued_statutory_documents_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "statutory_document_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    fiscal_year_start = table.Column<DateOnly>(type: "date", nullable: false),
                    fiscal_year_end = table.Column<DateOnly>(type: "date", nullable: false),
                    prefix = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    number_width = table.Column<int>(type: "int", nullable: false),
                    next_number = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statutory_document_series", x => x.id);
                    table.UniqueConstraint("AK_statutory_document_series_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_statutory_document_series_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "statutory_document_number_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fiscal_year_key = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    formatted_number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    gap_reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    business_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    issued_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    allocated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statutory_document_number_allocations", x => x.id);
                    table.UniqueConstraint("AK_statutory_document_number_allocations_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_statutory_document_number_allocations_statutory_document_series_company_id_series_id",
                        columns: x => new { x.company_id, x.series_id },
                        principalTable: "statutory_document_series",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_issued_statutory_documents_company_id_business_key_source_version",
                table: "issued_statutory_documents",
                columns: new[] { "company_id", "business_key", "source_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issued_statutory_documents_company_id_document_number_authority",
                table: "issued_statutory_documents",
                columns: new[] { "company_id", "document_number", "authority" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_statutory_documents_company_id_series_id_fiscal_year_key_sequence_number",
                table: "issued_statutory_documents",
                columns: new[] { "company_id", "series_id", "fiscal_year_key", "sequence_number" },
                unique: true,
                filter: "[series_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_issued_statutory_documents_company_id_source_record_id_source_version",
                table: "issued_statutory_documents",
                columns: new[] { "company_id", "source_record_id", "source_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statutory_document_number_allocations_company_id_business_key_source_version",
                table: "statutory_document_number_allocations",
                columns: new[] { "company_id", "business_key", "source_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statutory_document_number_allocations_company_id_series_id_fiscal_year_key_number",
                table: "statutory_document_number_allocations",
                columns: new[] { "company_id", "series_id", "fiscal_year_key", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statutory_document_series_company_id_code_fiscal_year_start_fiscal_year_end",
                table: "statutory_document_series",
                columns: new[] { "company_id", "code", "fiscal_year_start", "fiscal_year_end" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statutory_document_series_company_id_document_type_fiscal_year_start_fiscal_year_end",
                table: "statutory_document_series",
                columns: new[] { "company_id", "document_type", "fiscal_year_start", "fiscal_year_end" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issued_statutory_documents");

            migrationBuilder.DropTable(
                name: "statutory_document_number_allocations");

            migrationBuilder.DropTable(
                name: "statutory_document_series");
        }
    }
}
