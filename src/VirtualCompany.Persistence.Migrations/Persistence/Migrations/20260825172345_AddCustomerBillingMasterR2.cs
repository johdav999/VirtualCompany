using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBillingMasterR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "merged_at",
                table: "finance_counterparties",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_counterparty_id",
                table: "finance_counterparties",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_billing_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    party_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tax_identifier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    normalized_tax_identifier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    vat_identifier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    normalized_vat_identifier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    identity_validation_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    billing_address_line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    billing_address_line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    billing_postal_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    billing_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    billing_region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    billing_country_code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    delivery_address_line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    delivery_address_line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    delivery_postal_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    delivery_city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    delivery_region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    delivery_country_code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    language_code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    payment_term_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    payment_term_days = table.Column<int>(type: "int", nullable: false),
                    payment_method = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    invoice_delivery_channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    invoice_delivery_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_invoice_delivery_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    buyer_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    e_invoice_identifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    normalized_e_invoice_identifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    e_invoice_identifier_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    credit_limit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    credit_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    default_account_mapping = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    default_dimension_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    user_attested_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    externally_verified_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    verification_source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    conflict_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    merged_into_counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_billing_profiles", x => x.id);
                    table.UniqueConstraint("AK_customer_billing_profiles_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_billing_profiles_finance_counterparties_company_id_counterparty_id",
                        columns: x => new { x.company_id, x.counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_duplicate_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    first_counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    second_counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    score = table.Column<int>(type: "int", nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    merge_source_counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    merge_target_counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decision_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    detected_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_duplicate_candidates", x => x.id);
                    table.UniqueConstraint("AK_customer_duplicate_candidates_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_duplicate_candidates_finance_counterparties_company_id_first_counterparty_id",
                        columns: x => new { x.company_id, x.first_counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_duplicate_candidates_finance_counterparties_company_id_second_counterparty_id",
                        columns: x => new { x.company_id, x.second_counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_invoice_customer_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    billing_profile_version = table.Column<long>(type: "bigint", nullable: true),
                    source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    snapshot_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_invoice_customer_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_invoice_customer_snapshots_finance_counterparties_company_id_counterparty_id",
                        columns: x => new { x.company_id, x.counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_invoice_customer_snapshots_finance_invoices_company_id_invoice_id",
                        columns: x => new { x.company_id, x.invoice_id },
                        principalTable: "finance_invoices",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_billing_profile_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    profile_version = table.Column<long>(type: "bigint", nullable: false),
                    source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    changed_fields = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    snapshot_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_billing_profile_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_billing_profile_versions_customer_billing_profiles_company_id_profile_id",
                        columns: x => new { x.company_id, x.profile_id },
                        principalTable: "customer_billing_profiles",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_billing_source_conflicts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    base_version = table.Column<long>(type: "bigint", nullable: false),
                    existing_source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    incoming_source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    incoming_source_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    changed_fields = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    incoming_snapshot_json = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    used_incoming_values = table.Column<bool>(type: "bit", nullable: true),
                    decision_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    detected_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    detected_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_billing_source_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_billing_source_conflicts_customer_billing_profiles_company_id_profile_id",
                        columns: x => new { x.company_id, x.profile_id },
                        principalTable: "customer_billing_profiles",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_counterparty_redirects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_counterparty_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    duplicate_candidate_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_counterparty_redirects", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_counterparty_redirects_customer_duplicate_candidates_company_id_duplicate_candidate_id",
                        columns: x => new { x.company_id, x.duplicate_candidate_id },
                        principalTable: "customer_duplicate_candidates",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_counterparty_redirects_finance_counterparties_company_id_source_counterparty_id",
                        columns: x => new { x.company_id, x.source_counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_counterparty_redirects_finance_counterparties_company_id_target_counterparty_id",
                        columns: x => new { x.company_id, x.target_counterparty_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_counterparties_company_id_merged_into_counterparty_id",
                table: "finance_counterparties",
                columns: new[] { "company_id", "merged_into_counterparty_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profile_versions_company_id_counterparty_id_created_at",
                table: "customer_billing_profile_versions",
                columns: new[] { "company_id", "counterparty_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profile_versions_company_id_profile_id_profile_version",
                table: "customer_billing_profile_versions",
                columns: new[] { "company_id", "profile_id", "profile_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profiles_company_id_conflict_state",
                table: "customer_billing_profiles",
                columns: new[] { "company_id", "conflict_state" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profiles_company_id_counterparty_id",
                table: "customer_billing_profiles",
                columns: new[] { "company_id", "counterparty_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profiles_company_id_normalized_e_invoice_identifier",
                table: "customer_billing_profiles",
                columns: new[] { "company_id", "normalized_e_invoice_identifier" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profiles_company_id_normalized_invoice_delivery_email",
                table: "customer_billing_profiles",
                columns: new[] { "company_id", "normalized_invoice_delivery_email" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profiles_company_id_normalized_tax_identifier",
                table: "customer_billing_profiles",
                columns: new[] { "company_id", "normalized_tax_identifier" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_profiles_company_id_normalized_vat_identifier",
                table: "customer_billing_profiles",
                columns: new[] { "company_id", "normalized_vat_identifier" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_source_conflicts_company_id_counterparty_id_detected_at",
                table: "customer_billing_source_conflicts",
                columns: new[] { "company_id", "counterparty_id", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_billing_source_conflicts_company_id_profile_id_status",
                table: "customer_billing_source_conflicts",
                columns: new[] { "company_id", "profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_counterparty_redirects_company_id_duplicate_candidate_id",
                table: "customer_counterparty_redirects",
                columns: new[] { "company_id", "duplicate_candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_counterparty_redirects_company_id_source_counterparty_id",
                table: "customer_counterparty_redirects",
                columns: new[] { "company_id", "source_counterparty_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_counterparty_redirects_company_id_target_counterparty_id",
                table: "customer_counterparty_redirects",
                columns: new[] { "company_id", "target_counterparty_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_duplicate_candidates_company_id_first_counterparty_id_second_counterparty_id",
                table: "customer_duplicate_candidates",
                columns: new[] { "company_id", "first_counterparty_id", "second_counterparty_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_duplicate_candidates_company_id_second_counterparty_id",
                table: "customer_duplicate_candidates",
                columns: new[] { "company_id", "second_counterparty_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_duplicate_candidates_company_id_status_updated_at",
                table: "customer_duplicate_candidates",
                columns: new[] { "company_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_customer_snapshots_company_id_counterparty_id_created_at",
                table: "customer_invoice_customer_snapshots",
                columns: new[] { "company_id", "counterparty_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_invoice_customer_snapshots_company_id_invoice_id",
                table: "customer_invoice_customer_snapshots",
                columns: new[] { "company_id", "invoice_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_billing_profile_versions");

            migrationBuilder.DropTable(
                name: "customer_billing_source_conflicts");

            migrationBuilder.DropTable(
                name: "customer_counterparty_redirects");

            migrationBuilder.DropTable(
                name: "customer_invoice_customer_snapshots");

            migrationBuilder.DropTable(
                name: "customer_billing_profiles");

            migrationBuilder.DropTable(
                name: "customer_duplicate_candidates");

            migrationBuilder.DropIndex(
                name: "IX_finance_counterparties_company_id_merged_into_counterparty_id",
                table: "finance_counterparties");

            migrationBuilder.DropColumn(
                name: "merged_at",
                table: "finance_counterparties");

            migrationBuilder.DropColumn(
                name: "merged_into_counterparty_id",
                table: "finance_counterparties");
        }
    }
}
