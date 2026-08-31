using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernedAccountingDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_allocation_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    approval_threshold = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_allocation_templates", x => x.id);
                    table.UniqueConstraint("AK_accounting_allocation_templates_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_templates_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_dimension_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    allows_hierarchy = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_dimension_types", x => x.id);
                    table.UniqueConstraint("AK_accounting_dimension_types_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_types_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_allocation_template_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    rounding_precision = table.Column<int>(type: "int", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_allocation_template_versions", x => x.id);
                    table.UniqueConstraint("AK_accounting_allocation_template_versions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_template_versions_accounting_allocation_templates_company_id_template_id",
                        columns: x => new { x.company_id, x.template_id },
                        principalTable: "accounting_allocation_templates",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_template_versions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_dimension_account_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requirement = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_dimension_account_policies", x => x.id);
                    table.UniqueConstraint("AK_accounting_dimension_account_policies_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_account_policies_accounting_dimension_types_company_id_dimension_type_id",
                        columns: x => new { x.company_id, x.dimension_type_id },
                        principalTable: "accounting_dimension_types",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_account_policies_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_dimension_account_policies_finance_accounts_company_id_finance_account_id",
                        columns: x => new { x.company_id, x.finance_account_id },
                        principalTable: "finance_accounts",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_dimension_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_dimension_members", x => x.id);
                    table.UniqueConstraint("AK_accounting_dimension_members_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_members_accounting_dimension_members_company_id_parent_member_id",
                        columns: x => new { x.company_id, x.parent_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_members_accounting_dimension_types_company_id_dimension_type_id",
                        columns: x => new { x.company_id, x.dimension_type_id },
                        principalTable: "accounting_dimension_types",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_members_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_allocation_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    allocated_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_allocation_applications", x => x.id);
                    table.UniqueConstraint("AK_accounting_allocation_applications_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_applications_accounting_allocation_template_versions_company_id_template_version_id",
                        columns: x => new { x.company_id, x.template_version_id },
                        principalTable: "accounting_allocation_template_versions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_applications_accounting_allocation_templates_company_id_template_id",
                        columns: x => new { x.company_id, x.template_id },
                        principalTable: "accounting_allocation_templates",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_applications_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalTable: "approval_requests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_accounting_allocation_applications_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_allocation_template_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    allocation_kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    value = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    basis = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_allocation_template_lines", x => x.id);
                    table.UniqueConstraint("AK_accounting_allocation_template_lines_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_template_lines_accounting_allocation_template_versions_company_id_template_version_id",
                        columns: x => new { x.company_id, x.template_version_id },
                        principalTable: "accounting_allocation_template_versions",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_template_lines_accounting_dimension_members_company_id_dimension_member_id",
                        columns: x => new { x.company_id, x.dimension_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_allocation_template_lines_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_dimension_combination_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    left_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    right_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_allowed = table.Column<bool>(type: "bit", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_dimension_combination_rules", x => x.id);
                    table.UniqueConstraint("AK_accounting_dimension_combination_rules_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_combination_rules_accounting_dimension_members_company_id_left_member_id",
                        columns: x => new { x.company_id, x.left_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_combination_rules_accounting_dimension_members_company_id_right_member_id",
                        columns: x => new { x.company_id, x.right_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_combination_rules_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_dimension_external_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    external_dimension_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    external_value = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    dimension_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_dimension_external_mappings", x => x.id);
                    table.UniqueConstraint("AK_accounting_dimension_external_mappings_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_external_mappings_accounting_dimension_members_company_id_dimension_member_id",
                        columns: x => new { x.company_id, x.dimension_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_external_mappings_accounting_dimension_types_company_id_dimension_type_id",
                        columns: x => new { x.company_id, x.dimension_type_id },
                        principalTable: "accounting_dimension_types",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_external_mappings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_dimension_mapping_conflicts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    external_dimension_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    external_value = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    resolved_dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_dimension_mapping_conflicts", x => x.id);
                    table.UniqueConstraint("AK_accounting_dimension_mapping_conflicts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_mapping_conflicts_accounting_dimension_members_company_id_resolved_dimension_member_id",
                        columns: x => new { x.company_id, x.resolved_dimension_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_dimension_mapping_conflicts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entry_line_dimensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ledger_entry_line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_type_code_snapshot = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    dimension_type_name_snapshot = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    member_code_snapshot = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    member_name_snapshot = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    hierarchy_path_snapshot = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entry_line_dimensions", x => x.id);
                    table.UniqueConstraint("AK_ledger_entry_line_dimensions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_ledger_entry_line_dimensions_accounting_dimension_members_company_id_dimension_member_id",
                        columns: x => new { x.company_id, x.dimension_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_ledger_entry_line_dimensions_accounting_dimension_types_company_id_dimension_type_id",
                        columns: x => new { x.company_id, x.dimension_type_id },
                        principalTable: "accounting_dimension_types",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_ledger_entry_line_dimensions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ledger_entry_line_dimensions_ledger_entry_lines_company_id_ledger_entry_line_id",
                        columns: x => new { x.company_id, x.ledger_entry_line_id },
                        principalTable: "ledger_entry_lines",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "manual_journal_draft_line_dimensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    manual_journal_draft_line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_journal_draft_line_dimensions", x => x.id);
                    table.UniqueConstraint("AK_manual_journal_draft_line_dimensions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_manual_journal_draft_line_dimensions_accounting_dimension_members_company_id_dimension_member_id",
                        columns: x => new { x.company_id, x.dimension_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_manual_journal_draft_line_dimensions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_manual_journal_draft_line_dimensions_manual_journal_draft_lines_company_id_manual_journal_draft_line_id",
                        columns: x => new { x.company_id, x.manual_journal_draft_line_id },
                        principalTable: "manual_journal_draft_lines",
                        principalColumns: new[] { "company_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "accounting_allocation_application_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    application_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    dimension_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    allocation_kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    driver_value = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    raw_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    rounded_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    rounding_residual = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_allocation_application_lines", x => x.id);
                    table.UniqueConstraint("AK_accounting_allocation_application_lines_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_application_lines_accounting_allocation_applications_company_id_application_id",
                        columns: x => new { x.company_id, x.application_id },
                        principalTable: "accounting_allocation_applications",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_application_lines_accounting_dimension_members_company_id_dimension_member_id",
                        columns: x => new { x.company_id, x.dimension_member_id },
                        principalTable: "accounting_dimension_members",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounting_allocation_application_lines_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_allocation_evidence_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    application_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_allocation_evidence_links", x => x.id);
                    table.UniqueConstraint("AK_accounting_allocation_evidence_links_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_evidence_links_accounting_allocation_applications_company_id_application_id",
                        columns: x => new { x.company_id, x.application_id },
                        principalTable: "accounting_allocation_applications",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_accounting_allocation_evidence_links_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accounting_allocation_evidence_links_knowledge_documents_company_id_document_id",
                        columns: x => new { x.company_id, x.document_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_application_lines_company_id_application_id_sequence",
                table: "accounting_allocation_application_lines",
                columns: new[] { "company_id", "application_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_application_lines_company_id_dimension_member_id",
                table: "accounting_allocation_application_lines",
                columns: new[] { "company_id", "dimension_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_applications_approval_request_id",
                table: "accounting_allocation_applications",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_applications_company_id_idempotency_key",
                table: "accounting_allocation_applications",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_applications_company_id_source_type_source_id_source_version",
                table: "accounting_allocation_applications",
                columns: new[] { "company_id", "source_type", "source_id", "source_version" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_applications_company_id_template_id",
                table: "accounting_allocation_applications",
                columns: new[] { "company_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_applications_company_id_template_version_id",
                table: "accounting_allocation_applications",
                columns: new[] { "company_id", "template_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_evidence_links_company_id_application_id_document_id",
                table: "accounting_allocation_evidence_links",
                columns: new[] { "company_id", "application_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_evidence_links_company_id_document_id",
                table: "accounting_allocation_evidence_links",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_template_lines_company_id_dimension_member_id",
                table: "accounting_allocation_template_lines",
                columns: new[] { "company_id", "dimension_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_template_lines_company_id_template_version_id_sequence",
                table: "accounting_allocation_template_lines",
                columns: new[] { "company_id", "template_version_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_template_versions_company_id_template_id_version_number",
                table: "accounting_allocation_template_versions",
                columns: new[] { "company_id", "template_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_allocation_templates_company_id_code",
                table: "accounting_allocation_templates",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_account_policies_company_id_dimension_type_id",
                table: "accounting_dimension_account_policies",
                columns: new[] { "company_id", "dimension_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_account_policies_company_id_finance_account_id_dimension_type_id_effective_from",
                table: "accounting_dimension_account_policies",
                columns: new[] { "company_id", "finance_account_id", "dimension_type_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_combination_rules_company_id_left_member_id_right_member_id_effective_from",
                table: "accounting_dimension_combination_rules",
                columns: new[] { "company_id", "left_member_id", "right_member_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_combination_rules_company_id_right_member_id",
                table: "accounting_dimension_combination_rules",
                columns: new[] { "company_id", "right_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_external_mappings_company_id_dimension_member_id",
                table: "accounting_dimension_external_mappings",
                columns: new[] { "company_id", "dimension_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_external_mappings_company_id_dimension_type_id",
                table: "accounting_dimension_external_mappings",
                columns: new[] { "company_id", "dimension_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_external_mappings_company_id_provider_key_external_dimension_type_external_value_effective_from",
                table: "accounting_dimension_external_mappings",
                columns: new[] { "company_id", "provider_key", "external_dimension_type", "external_value", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_mapping_conflicts_company_id_provider_key_external_dimension_type_external_value_status",
                table: "accounting_dimension_mapping_conflicts",
                columns: new[] { "company_id", "provider_key", "external_dimension_type", "external_value", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_mapping_conflicts_company_id_resolved_dimension_member_id",
                table: "accounting_dimension_mapping_conflicts",
                columns: new[] { "company_id", "resolved_dimension_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_mapping_conflicts_company_id_status_created_at",
                table: "accounting_dimension_mapping_conflicts",
                columns: new[] { "company_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_members_company_id_dimension_type_id_code",
                table: "accounting_dimension_members",
                columns: new[] { "company_id", "dimension_type_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_members_company_id_dimension_type_id_parent_member_id_status",
                table: "accounting_dimension_members",
                columns: new[] { "company_id", "dimension_type_id", "parent_member_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_members_company_id_parent_member_id",
                table: "accounting_dimension_members",
                columns: new[] { "company_id", "parent_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_types_company_id_code",
                table: "accounting_dimension_types",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_dimension_types_company_id_status_effective_from",
                table: "accounting_dimension_types",
                columns: new[] { "company_id", "status", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_line_dimensions_company_id_dimension_member_id_ledger_entry_line_id",
                table: "ledger_entry_line_dimensions",
                columns: new[] { "company_id", "dimension_member_id", "ledger_entry_line_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_line_dimensions_company_id_dimension_type_id",
                table: "ledger_entry_line_dimensions",
                columns: new[] { "company_id", "dimension_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entry_line_dimensions_company_id_ledger_entry_line_id_dimension_type_id",
                table: "ledger_entry_line_dimensions",
                columns: new[] { "company_id", "ledger_entry_line_id", "dimension_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manual_journal_draft_line_dimensions_company_id_dimension_member_id",
                table: "manual_journal_draft_line_dimensions",
                columns: new[] { "company_id", "dimension_member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_manual_journal_draft_line_dimensions_company_id_manual_journal_draft_line_id_dimension_member_id",
                table: "manual_journal_draft_line_dimensions",
                columns: new[] { "company_id", "manual_journal_draft_line_id", "dimension_member_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                DECLARE @dimension_seed_actor uniqueidentifier = '00000000-0000-0000-0000-000000000001';
                DECLARE @dimension_seed_at datetime2 = SYSUTCDATETIME();
                DECLARE @dimension_seed_date date = CAST(@dimension_seed_at AS date);

                INSERT INTO accounting_dimension_types
                    (id, company_id, code, name, description, allows_hierarchy, status, effective_from,
                     effective_to, created_by_user_id, created_at, updated_at, version)
                SELECT NEWID(), company.Id, seed.code, seed.name, seed.description, seed.allows_hierarchy,
                       N'active', @dimension_seed_date, NULL, @dimension_seed_actor,
                       @dimension_seed_at, @dimension_seed_at, 1
                FROM companies AS company
                CROSS JOIN (VALUES
                    (N'cost_center', N'Cost center', N'Governed organizational cost ownership.', CAST(1 AS bit)),
                    (N'project', N'Project', N'Governed project attribution.', CAST(1 AS bit))
                ) AS seed(code, name, description, allows_hierarchy)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM accounting_dimension_types AS existing
                    WHERE existing.company_id = company.Id AND existing.code = seed.code
                );

                WITH legacy_cost_centers AS
                (
                    SELECT company_id, cost_center_id FROM ledger_entry_lines WHERE cost_center_id IS NOT NULL
                    UNION
                    SELECT company_id, cost_center_id FROM manual_journal_draft_lines WHERE cost_center_id IS NOT NULL
                    UNION
                    SELECT company_id, cost_center_id FROM budgets WHERE cost_center_id IS NOT NULL
                    UNION
                    SELECT company_id, cost_center_id FROM forecasts WHERE cost_center_id IS NOT NULL
                )
                INSERT INTO accounting_dimension_mapping_conflicts
                    (id, company_id, provider_key, external_dimension_type, external_value, reason_code,
                     explanation, status, resolved_dimension_member_id, created_at, resolved_at)
                SELECT NEWID(), legacy.company_id, N'legacy', N'cost_center',
                       CONVERT(nvarchar(36), legacy.cost_center_id), N'legacy_cost_center_unmapped',
                       N'Existing cost-center usage was retained, but no governed catalogue member could be identified without guessing.',
                       N'open', NULL, @dimension_seed_at, NULL
                FROM legacy_cost_centers AS legacy
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM accounting_dimension_mapping_conflicts AS existing
                    WHERE existing.company_id = legacy.company_id
                      AND existing.provider_key = N'legacy'
                      AND existing.external_dimension_type = N'cost_center'
                      AND existing.external_value = CONVERT(nvarchar(36), legacy.cost_center_id)
                      AND existing.status = N'open'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_allocation_application_lines");

            migrationBuilder.DropTable(
                name: "accounting_allocation_evidence_links");

            migrationBuilder.DropTable(
                name: "accounting_allocation_template_lines");

            migrationBuilder.DropTable(
                name: "accounting_dimension_account_policies");

            migrationBuilder.DropTable(
                name: "accounting_dimension_combination_rules");

            migrationBuilder.DropTable(
                name: "accounting_dimension_external_mappings");

            migrationBuilder.DropTable(
                name: "accounting_dimension_mapping_conflicts");

            migrationBuilder.DropTable(
                name: "ledger_entry_line_dimensions");

            migrationBuilder.DropTable(
                name: "manual_journal_draft_line_dimensions");

            migrationBuilder.DropTable(
                name: "accounting_allocation_applications");

            migrationBuilder.DropTable(
                name: "accounting_dimension_members");

            migrationBuilder.DropTable(
                name: "accounting_allocation_template_versions");

            migrationBuilder.DropTable(
                name: "accounting_dimension_types");

            migrationBuilder.DropTable(
                name: "accounting_allocation_templates");
        }
    }
}
