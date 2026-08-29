using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernedStatementImportCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_statement_csv_mapping_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    current_version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_statement_csv_mapping_profiles", x => x.id);
                    table.UniqueConstraint("AK_bank_statement_csv_mapping_profiles_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_statement_csv_mapping_profiles_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_csv_mapping_profile_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    delimiter = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    culture_name = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    date_format = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    has_header = table.Column<bool>(type: "bit", nullable: false),
                    booking_date_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    value_date_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    amount_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    debit_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    credit_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    currency_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    reference_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    counterparty_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    external_reference_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    account_identifier_column = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    default_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_statement_csv_mapping_profile_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_statement_csv_mapping_profile_versions_bank_statement_csv_mapping_profiles_company_id_profile_id",
                        columns: x => new { x.company_id, x.profile_id },
                        principalTable: "bank_statement_csv_mapping_profiles",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_statement_csv_mapping_profile_versions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_import_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    csv_mapping_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    csv_mapping_profile_version = table.Column<int>(type: "int", nullable: true),
                    original_file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    content_length = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    format = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    message_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    parser_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    statement_identity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    source_account_identifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    opening_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    closing_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    debit_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    calculated_closing_balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    total_row_count = table.Column<int>(type: "int", nullable: false),
                    accepted_row_count = table.Column<int>(type: "int", nullable: false),
                    duplicate_row_count = table.Column<int>(type: "int", nullable: false),
                    error_row_count = table.Column<int>(type: "int", nullable: false),
                    imported_row_count = table.Column<int>(type: "int", nullable: false),
                    last_committed_row_number = table.Column<int>(type: "int", nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    completed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_statement_import_jobs", x => x.id);
                    table.UniqueConstraint("AK_bank_statement_import_jobs_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_bank_statement_import_jobs_bank_statement_csv_mapping_profiles_company_id_csv_mapping_profile_id",
                        columns: x => new { x.company_id, x.csv_mapping_profile_id },
                        principalTable: "bank_statement_csv_mapping_profiles",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_jobs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_jobs_company_bank_accounts_company_id_bank_account_id",
                        columns: x => new { x.company_id, x.bank_account_id },
                        principalTable: "company_bank_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_import_job_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    row_number = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_statement_import_job_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_job_issues_bank_statement_import_jobs_company_id_job_id",
                        columns: x => new { x.company_id, x.job_id },
                        principalTable: "bank_statement_import_jobs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_job_issues_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_import_job_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_number = table.Column<int>(type: "int", nullable: false),
                    row_identity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    row_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    booking_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    value_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    reference_text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    counterparty = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    external_reference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    issue_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    issue_severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    issue_message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    payment_status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    conflict_decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    conflict_decision_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    imported_bank_transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_statement_import_job_rows", x => x.id);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_job_rows_bank_statement_import_jobs_company_id_job_id",
                        columns: x => new { x.company_id, x.job_id },
                        principalTable: "bank_statement_import_jobs",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_job_rows_bank_transactions_company_id_imported_bank_transaction_id",
                        columns: x => new { x.company_id, x.imported_bank_transaction_id },
                        principalTable: "bank_transactions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bank_statement_import_job_rows_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_csv_mapping_profile_versions_company_id_profile_id_version",
                table: "bank_statement_csv_mapping_profile_versions",
                columns: new[] { "company_id", "profile_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_csv_mapping_profiles_company_id_name",
                table: "bank_statement_csv_mapping_profiles",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_job_issues_company_id_job_id_severity",
                table: "bank_statement_import_job_issues",
                columns: new[] { "company_id", "job_id", "severity" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_job_rows_company_id_imported_bank_transaction_id",
                table: "bank_statement_import_job_rows",
                columns: new[] { "company_id", "imported_bank_transaction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_job_rows_company_id_job_id_outcome_processed_at",
                table: "bank_statement_import_job_rows",
                columns: new[] { "company_id", "job_id", "outcome", "processed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_job_rows_company_id_job_id_row_number",
                table: "bank_statement_import_job_rows",
                columns: new[] { "company_id", "job_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_jobs_company_id_bank_account_id",
                table: "bank_statement_import_jobs",
                columns: new[] { "company_id", "bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_jobs_company_id_checksum",
                table: "bank_statement_import_jobs",
                columns: new[] { "company_id", "checksum" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_jobs_company_id_csv_mapping_profile_id",
                table: "bank_statement_import_jobs",
                columns: new[] { "company_id", "csv_mapping_profile_id" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_import_jobs_company_id_status_updated_at",
                table: "bank_statement_import_jobs",
                columns: new[] { "company_id", "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_statement_csv_mapping_profile_versions");

            migrationBuilder.DropTable(
                name: "bank_statement_import_job_issues");

            migrationBuilder.DropTable(
                name: "bank_statement_import_job_rows");

            migrationBuilder.DropTable(
                name: "bank_statement_import_jobs");

            migrationBuilder.DropTable(
                name: "bank_statement_csv_mapping_profiles");
        }
    }
}
