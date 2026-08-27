using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSwedishVatReturnWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vat_filing_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vat_filing_periods", x => x.id);
                    table.UniqueConstraint("AK_vat_filing_periods_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey("FK_vat_filing_periods_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_vat_filing_periods_finance_fiscal_periods_company_id_fiscal_period_id", x => new { x.company_id, x.fiscal_period_id }, "finance_fiscal_periods", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vat_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    filing_period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), version = table.Column<int>(type: "int", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false), status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    correction_of_vat_return_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), correction_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    correction_evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true), cutoff_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    input_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true), calculation_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    included_source_count = table.Column<int>(type: "int", nullable: false), excluded_source_count = table.Column<int>(type: "int", nullable: false),
                    output_vat_exact = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false), input_vat_exact = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    settlement_exact = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false), settlement_filing_amount = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), finalized_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), finalized_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    package_storage_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true), package_checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    package_file_name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true), package_media_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    package_content_length = table.Column<long>(type: "bigint", nullable: true), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vat_returns", x => x.id);
                    table.UniqueConstraint("AK_vat_returns_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey("FK_vat_returns_vat_filing_periods_company_id_filing_period_id", x => new { x.company_id, x.filing_period_id }, "vat_filing_periods", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_vat_returns_vat_returns_company_id_correction_of_vat_return_id", x => new { x.company_id, x.correction_of_vat_return_id }, "vat_returns", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                });

            CreateVatChildTables(migrationBuilder);
            migrationBuilder.CreateIndex("IX_vat_filing_periods_company_id_fiscal_period_id", "vat_filing_periods", new[] { "company_id", "fiscal_period_id" });
            migrationBuilder.CreateIndex("IX_vat_filing_periods_company_id_period_code", "vat_filing_periods", new[] { "company_id", "period_code" }, unique: true);
            migrationBuilder.CreateIndex("IX_vat_filing_periods_company_id_start_date_end_date", "vat_filing_periods", new[] { "company_id", "start_date", "end_date" }, unique: true);
            migrationBuilder.CreateIndex("IX_vat_return_box_results_company_id_vat_return_id_box_code", "vat_return_box_results", new[] { "company_id", "vat_return_id", "box_code" }, unique: true);
            migrationBuilder.CreateIndex("IX_vat_return_reviews_company_id_vat_return_id_occurred_at", "vat_return_reviews", new[] { "company_id", "vat_return_id", "occurred_at" });
            migrationBuilder.CreateIndex("IX_vat_return_source_contributions_company_id_ledger_entry_id", "vat_return_source_contributions", new[] { "company_id", "ledger_entry_id" });
            migrationBuilder.CreateIndex("IX_vat_return_source_contributions_company_id_vat_return_id_ledger_entry_id_source_checksum_box_code", "vat_return_source_contributions", new[] { "company_id", "vat_return_id", "ledger_entry_id", "source_checksum", "box_code" }, unique: true);
            migrationBuilder.CreateIndex("IX_vat_return_validation_issues_company_id_vat_return_id_code", "vat_return_validation_issues", new[] { "company_id", "vat_return_id", "code" });
            migrationBuilder.CreateIndex("IX_vat_returns_company_id_correction_of_vat_return_id", "vat_returns", new[] { "company_id", "correction_of_vat_return_id" });
            migrationBuilder.CreateIndex("IX_vat_returns_company_id_filing_period_id_status", "vat_returns", new[] { "company_id", "filing_period_id", "status" });
            migrationBuilder.CreateIndex("IX_vat_returns_company_id_filing_period_id_version", "vat_returns", new[] { "company_id", "filing_period_id", "version" }, unique: true);
            migrationBuilder.CreateIndex("IX_vat_returns_company_id_idempotency_key", "vat_returns", new[] { "company_id", "idempotency_key" }, unique: true);
        }

        private static void CreateVatChildTables(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable("vat_return_box_results", table => new { id = table.Column<Guid>("uniqueidentifier", nullable: false), company_id = table.Column<Guid>("uniqueidentifier", nullable: false), vat_return_id = table.Column<Guid>("uniqueidentifier", nullable: false), box_code = table.Column<string>("nvarchar(8)", maxLength: 8, nullable: false), fact_type = table.Column<string>("nvarchar(40)", maxLength: 40, nullable: false), exact_amount = table.Column<decimal>("decimal(19,6)", precision: 19, scale: 6, nullable: false), filing_amount = table.Column<long>("bigint", nullable: false), currency = table.Column<string>("nvarchar(3)", maxLength: 3, nullable: false), source_count = table.Column<int>("int", nullable: false) }, constraints: table => { table.PrimaryKey("PK_vat_return_box_results", x => x.id); table.ForeignKey("FK_vat_return_box_results_vat_returns_company_id_vat_return_id", x => new { x.company_id, x.vat_return_id }, "vat_returns", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade); });
            migrationBuilder.CreateTable("vat_return_reviews", table => new { id = table.Column<Guid>("uniqueidentifier", nullable: false), company_id = table.Column<Guid>("uniqueidentifier", nullable: false), vat_return_id = table.Column<Guid>("uniqueidentifier", nullable: false), action = table.Column<string>("nvarchar(40)", maxLength: 40, nullable: false), actor_user_id = table.Column<Guid>("uniqueidentifier", nullable: false), approval_request_id = table.Column<Guid>("uniqueidentifier", nullable: true), evidence_hash = table.Column<string>("nvarchar(64)", maxLength: 64, nullable: false), occurred_at = table.Column<DateTime>("datetime2", nullable: false) }, constraints: table => { table.PrimaryKey("PK_vat_return_reviews", x => x.id); table.ForeignKey("FK_vat_return_reviews_vat_returns_company_id_vat_return_id", x => new { x.company_id, x.vat_return_id }, "vat_returns", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade); });
            migrationBuilder.CreateTable("vat_return_source_contributions", table => new { id = table.Column<Guid>("uniqueidentifier", nullable: false), company_id = table.Column<Guid>("uniqueidentifier", nullable: false), vat_return_id = table.Column<Guid>("uniqueidentifier", nullable: false), ledger_entry_id = table.Column<Guid>("uniqueidentifier", nullable: false), voucher_number = table.Column<string>("nvarchar(64)", maxLength: 64, nullable: false), posting_date = table.Column<DateOnly>("date", nullable: false), source_type = table.Column<string>("nvarchar(64)", maxLength: 64, nullable: false), source_id = table.Column<string>("nvarchar(128)", maxLength: 128, nullable: false), source_version = table.Column<string>("nvarchar(64)", maxLength: 64, nullable: false), policy_pack_key = table.Column<string>("nvarchar(96)", maxLength: 96, nullable: false), policy_pack_version = table.Column<string>("nvarchar(32)", maxLength: 32, nullable: false), tax_rule_key = table.Column<string>("nvarchar(96)", maxLength: 96, nullable: false), tax_rule_version = table.Column<string>("nvarchar(32)", maxLength: 32, nullable: false), box_code = table.Column<string>("nvarchar(8)", maxLength: 8, nullable: false), fact_type = table.Column<string>("nvarchar(40)", maxLength: 40, nullable: false), exact_amount = table.Column<decimal>("decimal(19,6)", precision: 19, scale: 6, nullable: false), currency = table.Column<string>("nvarchar(3)", maxLength: 3, nullable: false), source_checksum = table.Column<string>("nvarchar(64)", maxLength: 64, nullable: false) }, constraints: table => { table.PrimaryKey("PK_vat_return_source_contributions", x => x.id); table.ForeignKey("FK_vat_return_source_contributions_vat_returns_company_id_vat_return_id", x => new { x.company_id, x.vat_return_id }, "vat_returns", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade); });
            migrationBuilder.CreateTable("vat_return_validation_issues", table => new { id = table.Column<Guid>("uniqueidentifier", nullable: false), company_id = table.Column<Guid>("uniqueidentifier", nullable: false), vat_return_id = table.Column<Guid>("uniqueidentifier", nullable: false), code = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false), explanation = table.Column<string>("nvarchar(1000)", maxLength: 1000, nullable: false), is_blocking = table.Column<bool>("bit", nullable: false), ledger_entry_id = table.Column<Guid>("uniqueidentifier", nullable: true), source_reference = table.Column<string>("nvarchar(500)", maxLength: 500, nullable: true), difference = table.Column<decimal>("decimal(19,6)", precision: 19, scale: 6, nullable: true) }, constraints: table => { table.PrimaryKey("PK_vat_return_validation_issues", x => x.id); table.ForeignKey("FK_vat_return_validation_issues_vat_returns_company_id_vat_return_id", x => new { x.company_id, x.vat_return_id }, "vat_returns", new[] { "company_id", "id" }, onDelete: ReferentialAction.Cascade); });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("vat_return_box_results");
            migrationBuilder.DropTable("vat_return_reviews");
            migrationBuilder.DropTable("vat_return_source_contributions");
            migrationBuilder.DropTable("vat_return_validation_issues");
            migrationBuilder.DropTable("vat_returns");
            migrationBuilder.DropTable("vat_filing_periods");
        }
    }
}
