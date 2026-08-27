using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSwedishStatutoryProfileFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_statutory_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    swedish_organisation_number = table.Column<string>(type: "nchar(10)", fixedLength: true, maxLength: 10, nullable: true),
                    vat_registration_number = table.Column<string>(type: "nchar(14)", fixedLength: true, maxLength: 14, nullable: true),
                    vat_registration_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    registered_address_line_1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    registered_address_line_2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    registered_postal_code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    registered_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    registered_country_code = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    correspondence_address_line_1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    correspondence_address_line_2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    correspondence_postal_code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    correspondence_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    correspondence_country_code = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    country_code = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    accounting_currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fiscal_year_basis = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    bookkeeping_method = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    organisation_registration_effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    vat_registration_effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    vat_registration_effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_user_attested = table.Column<bool>(type: "bit", nullable: false),
                    attested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attested_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    verification_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    source_captured_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    external_verifier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    externally_verified_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_statutory_profiles", x => x.id);
                    table.UniqueConstraint("AK_company_statutory_profiles_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_company_statutory_profiles_bookkeeping_method", "[bookkeeping_method] IN ('not_specified', 'accrual', 'cash')");
                    table.CheckConstraint("CK_company_statutory_profiles_fiscal_basis", "[fiscal_year_basis] IN ('calendar_year', 'non_calendar_year')");
                    table.CheckConstraint("CK_company_statutory_profiles_source_kind", "[source_kind] IN ('user_entry', 'imported_document', 'external_registry')");
                    table.CheckConstraint("CK_company_statutory_profiles_vat_dates", "[vat_registration_effective_to] IS NULL OR [vat_registration_effective_from] IS NULL OR [vat_registration_effective_to] >= [vat_registration_effective_from]");
                    table.CheckConstraint("CK_company_statutory_profiles_vat_status", "[vat_registration_status] IN ('not_registered', 'pending', 'registered')");
                    table.CheckConstraint("CK_company_statutory_profiles_verification_status", "[verification_status] IN ('unverified', 'externally_verified', 'verification_failed')");
                    table.ForeignKey(
                        name: "FK_company_statutory_profiles_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_statutory_profiles_company_id",
                table: "company_statutory_profiles",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_statutory_profiles_company_id_swedish_organisation_number",
                table: "company_statutory_profiles",
                columns: new[] { "company_id", "swedish_organisation_number" });

            migrationBuilder.CreateIndex(
                name: "IX_company_statutory_profiles_company_id_vat_registration_number",
                table: "company_statutory_profiles",
                columns: new[] { "company_id", "vat_registration_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_statutory_profiles");
        }
    }
}
