using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedReportDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "report_definition_hash",
                table: "financial_report_suite_snapshots",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "report_definition_version_id",
                table: "financial_report_suite_snapshots",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "report_definition_version_number",
                table: "financial_report_suite_snapshots",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "report_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    report_kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_template_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definitions", x => x.id);
                    table.UniqueConstraint("AK_report_definitions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_report_definitions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    report_kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    definition_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    revision = table.Column<int>(type: "int", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    submitted_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    activated_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    retired_utc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_versions", x => x.id);
                    table.UniqueConstraint("AK_report_definition_versions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_report_definition_versions_report_definitions_company_id_definition_id",
                        columns: x => new { x.company_id, x.definition_id },
                        principalTable: "report_definitions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    submitted_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    decision_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_approvals", x => x.id);
                    table.ForeignKey(
                        name: "FK_report_definition_approvals_report_definition_versions_company_id_version_id",
                        columns: x => new { x.company_id, x.version_id },
                        principalTable: "report_definition_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_command_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_report_definition_command_receipts_report_definition_versions_company_id_version_id",
                        columns: x => new { x.company_id, x.version_id },
                        principalTable: "report_definition_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_comparisons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    period_count = table.Column<int>(type: "int", nullable: false),
                    show_variance = table.Column<bool>(type: "bit", nullable: false),
                    show_variance_percent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_comparisons", x => x.id);
                    table.ForeignKey(
                        name: "FK_report_definition_comparisons_report_definition_versions_company_id_version_id",
                        columns: x => new { x.company_id, x.version_id },
                        principalTable: "report_definition_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_sections", x => x.id);
                    table.UniqueConstraint("AK_report_definition_sections_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_report_definition_sections_report_definition_versions_company_id_version_id",
                        columns: x => new { x.company_id, x.version_id },
                        principalTable: "report_definition_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_validation_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_valid = table.Column<bool>(type: "bit", nullable: false),
                    definition_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    validated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    validated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_validation_results", x => x.id);
                    table.UniqueConstraint("AK_report_definition_validation_results_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_report_definition_validation_results_report_definition_versions_company_id_version_id",
                        columns: x => new { x.company_id, x.version_id },
                        principalTable: "report_definition_versions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    section_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    line_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false),
                    formula = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    sign_rule = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    scale = table.Column<int>(type: "int", nullable: false),
                    decimals = table.Column<int>(type: "int", nullable: false),
                    suppress_zero = table.Column<bool>(type: "bit", nullable: false),
                    currency_mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    dimension_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_lines", x => x.id);
                    table.UniqueConstraint("AK_report_definition_lines_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_report_definition_lines_report_definition_sections_company_id_section_id",
                        columns: x => new { x.company_id, x.section_id },
                        principalTable: "report_definition_sections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_report_definition_lines_report_definition_versions_company_id_version_id",
                        columns: x => new { x.company_id, x.version_id },
                        principalTable: "report_definition_versions",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "report_definition_validation_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    validation_result_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_validation_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_report_definition_validation_issues_report_definition_validation_results_company_id_validation_result_id",
                        columns: x => new { x.company_id, x.validation_result_id },
                        principalTable: "report_definition_validation_results",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_definition_account_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_account_groups", x => x.id);
                    table.UniqueConstraint("AK_report_definition_account_groups_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_report_definition_account_groups_report_definition_lines_company_id_line_id",
                        columns: x => new { x.company_id, x.line_id },
                        principalTable: "report_definition_lines",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_report_definition_account_groups_report_definition_versions_company_id_version_id",
                        columns: x => new { x.company_id, x.version_id },
                        principalTable: "report_definition_versions",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "report_definition_account_group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definition_account_group_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_report_definition_account_group_members_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_report_definition_account_group_members_report_definition_account_groups_company_id_group_id",
                        columns: x => new { x.company_id, x.group_id },
                        principalTable: "report_definition_account_groups",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_report_suite_snapshots_company_id_report_definition_version_id",
                table: "financial_report_suite_snapshots",
                columns: new[] { "company_id", "report_definition_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_account_group_members_company_id_finance_account_id",
                table: "report_definition_account_group_members",
                columns: new[] { "company_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_account_group_members_company_id_group_id_finance_account_id",
                table: "report_definition_account_group_members",
                columns: new[] { "company_id", "group_id", "finance_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_account_groups_company_id_line_id",
                table: "report_definition_account_groups",
                columns: new[] { "company_id", "line_id" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_account_groups_company_id_version_id_code",
                table: "report_definition_account_groups",
                columns: new[] { "company_id", "version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_approvals_company_id_version_id_submitted_utc",
                table: "report_definition_approvals",
                columns: new[] { "company_id", "version_id", "submitted_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_command_receipts_company_id_idempotency_key",
                table: "report_definition_command_receipts",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_command_receipts_company_id_version_id",
                table: "report_definition_command_receipts",
                columns: new[] { "company_id", "version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_comparisons_company_id_version_id",
                table: "report_definition_comparisons",
                columns: new[] { "company_id", "version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_lines_company_id_section_id",
                table: "report_definition_lines",
                columns: new[] { "company_id", "section_id" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_lines_company_id_version_id_code",
                table: "report_definition_lines",
                columns: new[] { "company_id", "version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_sections_company_id_version_id_code",
                table: "report_definition_sections",
                columns: new[] { "company_id", "version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_validation_issues_company_id_validation_result_id_code",
                table: "report_definition_validation_issues",
                columns: new[] { "company_id", "validation_result_id", "code" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_validation_results_company_id_version_id_validated_utc",
                table: "report_definition_validation_results",
                columns: new[] { "company_id", "version_id", "validated_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_versions_company_id_definition_id_version_number",
                table: "report_definition_versions",
                columns: new[] { "company_id", "definition_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definition_versions_company_id_report_kind_status_effective_from_effective_to",
                table: "report_definition_versions",
                columns: new[] { "company_id", "report_kind", "status", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definitions_company_id_code",
                table: "report_definitions",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_definitions_company_id_report_kind",
                table: "report_definitions",
                columns: new[] { "company_id", "report_kind" });

            migrationBuilder.AddForeignKey(
                name: "FK_financial_report_suite_snapshots_report_definition_versions_company_id_report_definition_version_id",
                table: "financial_report_suite_snapshots",
                columns: new[] { "company_id", "report_definition_version_id" },
                principalTable: "report_definition_versions",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_report_suite_snapshots_report_definition_versions_company_id_report_definition_version_id",
                table: "financial_report_suite_snapshots");

            migrationBuilder.DropTable(
                name: "report_definition_account_group_members");

            migrationBuilder.DropTable(
                name: "report_definition_approvals");

            migrationBuilder.DropTable(
                name: "report_definition_command_receipts");

            migrationBuilder.DropTable(
                name: "report_definition_comparisons");

            migrationBuilder.DropTable(
                name: "report_definition_validation_issues");

            migrationBuilder.DropTable(
                name: "report_definition_account_groups");

            migrationBuilder.DropTable(
                name: "report_definition_validation_results");

            migrationBuilder.DropTable(
                name: "report_definition_lines");

            migrationBuilder.DropTable(
                name: "report_definition_sections");

            migrationBuilder.DropTable(
                name: "report_definition_versions");

            migrationBuilder.DropTable(
                name: "report_definitions");

            migrationBuilder.DropIndex(
                name: "IX_financial_report_suite_snapshots_company_id_report_definition_version_id",
                table: "financial_report_suite_snapshots");

            migrationBuilder.DropColumn(
                name: "report_definition_hash",
                table: "financial_report_suite_snapshots");

            migrationBuilder.DropColumn(
                name: "report_definition_version_id",
                table: "financial_report_suite_snapshots");

            migrationBuilder.DropColumn(
                name: "report_definition_version_number",
                table: "financial_report_suite_snapshots");
        }
    }
}
