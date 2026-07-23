using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddLeadGenerationWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sales_icp_profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                previous_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                version = table.Column<int>(type: "int", nullable: false),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                countries = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                industries = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                employee_min = table.Column<int>(type: "int", nullable: true),
                employee_max = table.Column<int>(type: "int", nullable: true),
                revenue_min = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                revenue_max = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                buyer_roles = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                technologies = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                pain_hypotheses = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                positive_criteria = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                disqualifiers = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                activated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table => { table.PrimaryKey("PK_sales_icp_profiles", x => x.id); table.UniqueConstraint("AK_sales_icp_profiles_company_id_id", x => new { x.company_id, x.id }); });

        migrationBuilder.CreateTable(
            name: "sales_prospect_source_policies",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), version = table.Column<int>(type: "int", nullable: false),
                enabled_sources = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false), allowed_countries = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false), allowed_fields = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                per_run_budget = table.Column<decimal>(type: "decimal(18,2)", nullable: false), monthly_budget = table.Column<decimal>(type: "decimal(18,2)", nullable: false), approval_threshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false), retention_days = table.Column<int>(type: "int", nullable: false), refresh_days = table.Column<int>(type: "int", nullable: false), reserved_this_month = table.Column<decimal>(type: "decimal(18,2)", nullable: false), actual_this_month = table.Column<decimal>(type: "decimal(18,2)", nullable: false), is_active = table.Column<bool>(type: "bit", nullable: false), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), updated_at = table.Column<DateTime>(type: "datetime2", nullable: false), row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_sales_prospect_source_policies", x => x.id); table.UniqueConstraint("AK_sales_prospect_source_policies_company_id_id", x => new { x.company_id, x.id }); });

        migrationBuilder.CreateTable(
            name: "sales_prospecting_runs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), icp_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), approval_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false), account_limit = table.Column<int>(type: "int", nullable: false), contact_limit = table.Column<int>(type: "int", nullable: false), sources = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false), geography = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false), freshness_days = table.Column<int>(type: "int", nullable: false), estimated_cost = table.Column<decimal>(type: "decimal(18,2)", nullable: false), actual_cost = table.Column<decimal>(type: "decimal(18,2)", nullable: false), schedule = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true), status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), current_step = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false), accounts_found = table.Column<int>(type: "int", nullable: false), contacts_found = table.Column<int>(type: "int", nullable: false), cursor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true), failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), updated_at = table.Column<DateTime>(type: "datetime2", nullable: false), started_at = table.Column<DateTime>(type: "datetime2", nullable: true), completed_at = table.Column<DateTime>(type: "datetime2", nullable: true), row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_sales_prospecting_runs", x => x.id); table.UniqueConstraint("AK_sales_prospecting_runs_company_id_id", x => new { x.company_id, x.id }); });

        migrationBuilder.CreateTable(
            name: "sales_prospect_accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), icp_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), merged_into_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false), legal_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true), domain = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true), country = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true), industry = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true), employees = table.Column<int>(type: "int", nullable: true), revenue = table.Column<decimal>(type: "decimal(18,2)", nullable: true), technologies = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false), source_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false), source_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false), last_observed_at = table.Column<DateTime>(type: "datetime2", nullable: false), status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), fit_outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), fit_score = table.Column<decimal>(type: "decimal(5,2)", nullable: false), timing_score = table.Column<decimal>(type: "decimal(5,2)", nullable: false), role_score = table.Column<decimal>(type: "decimal(5,2)", nullable: false), data_confidence_score = table.Column<decimal>(type: "decimal(5,2)", nullable: false), overall_score = table.Column<decimal>(type: "decimal(5,2)", nullable: false), score_band = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), evaluation_json = table.Column<string>(type: "nvarchar(max)", nullable: false), research_brief_json = table.Column<string>(type: "nvarchar(max)", nullable: false), rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), updated_at = table.Column<DateTime>(type: "datetime2", nullable: false), row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_sales_prospect_accounts", x => x.id); table.UniqueConstraint("AK_sales_prospect_accounts_company_id_id", x => new { x.company_id, x.id }); });

        migrationBuilder.CreateTable(
            name: "sales_prospect_contacts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), prospect_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), merged_into_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true), full_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false), title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true), department = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true), seniority = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true), buying_roles = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false), email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true), email_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), phone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true), profile_url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true), employment_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false), confidence = table.Column<decimal>(type: "decimal(5,4)", nullable: false), source_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false), source_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false), verified_at = table.Column<DateTime>(type: "datetime2", nullable: true), status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), updated_at = table.Column<DateTime>(type: "datetime2", nullable: false), row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_sales_prospect_contacts", x => x.id); table.UniqueConstraint("AK_sales_prospect_contacts_company_id_id", x => new { x.company_id, x.id }); });

        migrationBuilder.CreateTable(
            name: "sales_prospect_signals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), prospect_account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), signal_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false), source_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false), source_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false), summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false), event_at = table.Column<DateTime>(type: "datetime2", nullable: false), fresh_until = table.Column<DateTime>(type: "datetime2", nullable: false), confidence = table.Column<decimal>(type: "decimal(5,4)", nullable: false), relevance = table.Column<decimal>(type: "decimal(5,2)", nullable: false), status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), dedupe_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false), created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            }, constraints: table => { table.PrimaryKey("PK_sales_prospect_signals", x => x.id); table.UniqueConstraint("AK_sales_prospect_signals_company_id_id", x => new { x.company_id, x.id }); });

        migrationBuilder.CreateTable(
            name: "sales_suppressions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), scope_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false), scope_value = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false), reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false), source = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false), created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false), is_active = table.Column<bool>(type: "bit", nullable: false), created_at = table.Column<DateTime>(type: "datetime2", nullable: false), expires_at = table.Column<DateTime>(type: "datetime2", nullable: true)
            }, constraints: table => { table.PrimaryKey("PK_sales_suppressions", x => x.id); table.UniqueConstraint("AK_sales_suppressions_company_id_id", x => new { x.company_id, x.id }); });

        migrationBuilder.CreateIndex("IX_sales_icp_profiles_company_id_name_version", "sales_icp_profiles", new[] { "company_id", "name", "version" }, unique: true);
        migrationBuilder.CreateIndex("IX_sales_icp_profiles_company_id_status", "sales_icp_profiles", new[] { "company_id", "status" });
        migrationBuilder.CreateIndex("IX_sales_prospect_source_policies_company_id", "sales_prospect_source_policies", "company_id", unique: true);
        migrationBuilder.CreateIndex("IX_sales_prospecting_runs_company_id_status_created_at", "sales_prospecting_runs", new[] { "company_id", "status", "created_at" });
        migrationBuilder.CreateIndex("IX_sales_prospect_accounts_company_id_domain", "sales_prospect_accounts", new[] { "company_id", "domain" });
        migrationBuilder.CreateIndex("IX_sales_prospect_accounts_company_id_source_key_source_reference", "sales_prospect_accounts", new[] { "company_id", "source_key", "source_reference" }, unique: true);
        migrationBuilder.CreateIndex("IX_sales_prospect_accounts_company_id_status_overall_score", "sales_prospect_accounts", new[] { "company_id", "status", "overall_score" });
        migrationBuilder.CreateIndex("IX_sales_prospect_contacts_company_id_email", "sales_prospect_contacts", new[] { "company_id", "email" });
        migrationBuilder.CreateIndex("IX_sales_prospect_contacts_company_id_source_key_source_reference", "sales_prospect_contacts", new[] { "company_id", "source_key", "source_reference" }, unique: true);
        migrationBuilder.CreateIndex("IX_sales_prospect_signals_company_id_dedupe_key", "sales_prospect_signals", new[] { "company_id", "dedupe_key" }, unique: true);
        migrationBuilder.CreateIndex("IX_sales_prospect_signals_company_id_prospect_account_id_fresh_until", "sales_prospect_signals", new[] { "company_id", "prospect_account_id", "fresh_until" });
        migrationBuilder.CreateIndex("IX_sales_suppressions_company_id_scope_type_scope_value_is_active", "sales_suppressions", new[] { "company_id", "scope_type", "scope_value", "is_active" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("sales_prospect_contacts");
        migrationBuilder.DropTable("sales_prospect_signals");
        migrationBuilder.DropTable("sales_suppressions");
        migrationBuilder.DropTable("sales_prospect_accounts");
        migrationBuilder.DropTable("sales_prospecting_runs");
        migrationBuilder.DropTable("sales_prospect_source_policies");
        migrationBuilder.DropTable("sales_icp_profiles");
    }
}
