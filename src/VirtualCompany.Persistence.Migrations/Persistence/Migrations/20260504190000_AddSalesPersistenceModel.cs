using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260504190000_AddSalesPersistenceModel")]
    public partial class AddSalesPersistenceModel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_pipeline_stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_pipeline_stages", x => x.id);
                    table.UniqueConstraint("AK_sales_pipeline_stages_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "customer_companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    website = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    industry = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_companies", x => x.id);
                    table.UniqueConstraint("AK_customer_companies_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_companies_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    full_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.id);
                    table.UniqueConstraint("AK_contacts_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_contacts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contacts_customer_companies_company_id_customer_company_id",
                        columns: x => new { x.company_id, x.customer_company_id },
                        principalTable: "customer_companies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    primary_contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    pipeline_stage_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    converted_deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    estimated_value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    source = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_leads", x => x.id);
                    table.UniqueConstraint("AK_leads_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_leads_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_leads_contacts_company_id_primary_contact_id",
                        columns: x => new { x.company_id, x.primary_contact_id },
                        principalTable: "contacts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leads_customer_companies_company_id_customer_company_id",
                        columns: x => new { x.company_id, x.customer_company_id },
                        principalTable: "customer_companies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_leads_sales_pipeline_stages_pipeline_stage_id",
                        column: x => x.pipeline_stage_id,
                        principalTable: "sales_pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    primary_contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    pipeline_stage_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    expected_close_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deals", x => x.id);
                    table.UniqueConstraint("AK_deals_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_deals_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_deals_contacts_company_id_primary_contact_id",
                        columns: x => new { x.company_id, x.primary_contact_id },
                        principalTable: "contacts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deals_customer_companies_company_id_customer_company_id",
                        columns: x => new { x.company_id, x.customer_company_id },
                        principalTable: "customer_companies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deals_leads_company_id_source_lead_id",
                        columns: x => new { x.company_id, x.source_lead_id },
                        principalTable: "leads",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deals_sales_pipeline_stages_pipeline_stage_id",
                        column: x => x.pipeline_stage_id,
                        principalTable: "sales_pipeline_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_leads_deals_converted_deal_id",
                table: "leads",
                columns: new[] { "company_id", "converted_deal_id" },
                principalTable: "deals",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "sales_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    activity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_activities", x => x.id);
                    table.UniqueConstraint("AK_sales_activities_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(name: "FK_sales_activities_companies_company_id", column: x => x.company_id, principalTable: "companies", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_sales_activities_leads_company_id_lead_id", columns: x => new { x.company_id, x.lead_id }, principalTable: "leads", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_activities_deals_company_id_deal_id", columns: x => new { x.company_id, x.deal_id }, principalTable: "deals", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_activities_contacts_company_id_contact_id", columns: x => new { x.company_id, x.contact_id }, principalTable: "contacts", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_activities_customer_companies_company_id_customer_company_id", columns: x => new { x.company_id, x.customer_company_id }, principalTable: "customer_companies", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_agent_recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recommendation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_agent_recommendations", x => x.id);
                    table.UniqueConstraint("AK_sales_agent_recommendations_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(name: "FK_sales_agent_recommendations_companies_company_id", column: x => x.company_id, principalTable: "companies", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_sales_agent_recommendations_leads_company_id_lead_id", columns: x => new { x.company_id, x.lead_id }, principalTable: "leads", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_agent_recommendations_deals_company_id_deal_id", columns: x => new { x.company_id, x.deal_id }, principalTable: "deals", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_action_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action_summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_action_approvals", x => x.id);
                    table.UniqueConstraint("AK_sales_action_approvals_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(name: "FK_sales_action_approvals_companies_company_id", column: x => x.company_id, principalTable: "companies", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_sales_action_approvals_sales_agent_recommendations_company_id_recommendation_id", columns: x => new { x.company_id, x.recommendation_id }, principalTable: "sales_agent_recommendations", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_action_approvals_leads_company_id_lead_id", columns: x => new { x.company_id, x.lead_id }, principalTable: "leads", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_action_approvals_deals_company_id_deal_id", columns: x => new { x.company_id, x.deal_id }, principalTable: "deals", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_email_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_message_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_email_links", x => x.id);
                    table.UniqueConstraint("AK_sales_email_links_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(name: "FK_sales_email_links_companies_company_id", column: x => x.company_id, principalTable: "companies", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_sales_email_links_leads_company_id_lead_id", columns: x => new { x.company_id, x.lead_id }, principalTable: "leads", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_email_links_deals_company_id_deal_id", columns: x => new { x.company_id, x.deal_id }, principalTable: "deals", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_email_links_contacts_company_id_contact_id", columns: x => new { x.company_id, x.contact_id }, principalTable: "contacts", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_sales_email_links_customer_companies_company_id_customer_company_id", columns: x => new { x.company_id, x.customer_company_id }, principalTable: "customer_companies", principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO [sales_pipeline_stages] (
                    [id],
                    [company_id],
                    [name],
                    [display_order],
                    [is_system],
                    [is_active],
                    [created_at],
                    [updated_at],
                    [is_deleted],
                    [deleted_at])
                SELECT [seed].[id],
                       [seed].[company_id],
                       [seed].[name],
                       [seed].[display_order],
                       [seed].[is_system],
                       [seed].[is_active],
                       [seed].[created_at],
                       [seed].[updated_at],
                       [seed].[is_deleted],
                       [seed].[deleted_at]
                FROM (VALUES
                    (CAST(N'6d305bcb-3d87-40b0-a89d-bbe48b3f1891' AS uniqueidentifier), CAST(N'00000000-0000-0000-0000-000000000000' AS uniqueidentifier), N'New', 10, CAST(1 AS bit), CAST(1 AS bit), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(0 AS bit), CAST(NULL AS datetime2)),
                    (CAST(N'a7c6f0bf-2136-46a5-a82b-73506f91b79a' AS uniqueidentifier), CAST(N'00000000-0000-0000-0000-000000000000' AS uniqueidentifier), N'Qualified', 20, CAST(1 AS bit), CAST(1 AS bit), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(0 AS bit), CAST(NULL AS datetime2)),
                    (CAST(N'62e3f3e1-bfc3-4cf7-a24a-92d216d8d859' AS uniqueidentifier), CAST(N'00000000-0000-0000-0000-000000000000' AS uniqueidentifier), N'Proposal', 30, CAST(1 AS bit), CAST(1 AS bit), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(0 AS bit), CAST(NULL AS datetime2)),
                    (CAST(N'cbad0a5d-d5da-4c8e-a414-6fa5ce7d6f43' AS uniqueidentifier), CAST(N'00000000-0000-0000-0000-000000000000' AS uniqueidentifier), N'Won', 40, CAST(1 AS bit), CAST(1 AS bit), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(0 AS bit), CAST(NULL AS datetime2)),
                    (CAST(N'5c449a94-81b8-4edc-a0d6-8b42dd8ce47a' AS uniqueidentifier), CAST(N'00000000-0000-0000-0000-000000000000' AS uniqueidentifier), N'Lost', 50, CAST(1 AS bit), CAST(1 AS bit), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(N'2026-05-04T00:00:00.0000000' AS datetime2), CAST(0 AS bit), CAST(NULL AS datetime2))
                ) AS [seed] ([id], [company_id], [name], [display_order], [is_system], [is_active], [created_at], [updated_at], [is_deleted], [deleted_at])
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [sales_pipeline_stages] AS [existing]
                    WHERE [existing].[id] = [seed].[id]);
                """);

            CreateIndexes(migrationBuilder);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "sales_action_approvals");
            migrationBuilder.DropTable(name: "sales_activities");
            migrationBuilder.DropTable(name: "sales_email_links");
            migrationBuilder.DropTable(name: "sales_agent_recommendations");
            migrationBuilder.DropForeignKey(name: "FK_leads_deals_converted_deal_id", table: "leads");
            migrationBuilder.DropTable(name: "deals");
            migrationBuilder.DropTable(name: "leads");
            migrationBuilder.DropTable(name: "contacts");
            migrationBuilder.DropTable(name: "sales_pipeline_stages");
            migrationBuilder.DropTable(name: "customer_companies");
        }

        private static void CreateIndexes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(name: "IX_sales_pipeline_stages_company_id", table: "sales_pipeline_stages", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_sales_pipeline_stages_created_at", table: "sales_pipeline_stages", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_sales_pipeline_stages_company_id_name", table: "sales_pipeline_stages", columns: new[] { "company_id", "name" }, unique: true);

            migrationBuilder.CreateIndex(name: "IX_customer_companies_company_id", table: "customer_companies", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_customer_companies_status", table: "customer_companies", column: "status");
            migrationBuilder.CreateIndex(name: "IX_customer_companies_created_at", table: "customer_companies", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_customer_companies_company_id_name", table: "customer_companies", columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(name: "IX_contacts_company_id", table: "contacts", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_contacts_status", table: "contacts", column: "status");
            migrationBuilder.CreateIndex(name: "IX_contacts_created_at", table: "contacts", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_contacts_company_id_email", table: "contacts", columns: new[] { "company_id", "email" });
            migrationBuilder.CreateIndex(name: "IX_contacts_company_id_customer_company_id", table: "contacts", columns: new[] { "company_id", "customer_company_id" });

            migrationBuilder.CreateIndex(name: "IX_leads_company_id", table: "leads", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_leads_status", table: "leads", column: "status");
            migrationBuilder.CreateIndex(name: "IX_leads_created_at", table: "leads", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_leads_pipeline_stage_id", table: "leads", column: "pipeline_stage_id");
            migrationBuilder.CreateIndex(name: "IX_leads_company_id_converted_deal_id", table: "leads", columns: new[] { "company_id", "converted_deal_id" });
            migrationBuilder.CreateIndex(name: "IX_leads_company_id_primary_contact_id", table: "leads", columns: new[] { "company_id", "primary_contact_id" });
            migrationBuilder.CreateIndex(name: "IX_leads_company_id_customer_company_id", table: "leads", columns: new[] { "company_id", "customer_company_id" });

            migrationBuilder.CreateIndex(name: "IX_deals_company_id", table: "deals", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_deals_status", table: "deals", column: "status");
            migrationBuilder.CreateIndex(name: "IX_deals_created_at", table: "deals", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_deals_pipeline_stage_id", table: "deals", column: "pipeline_stage_id");
            migrationBuilder.CreateIndex(name: "IX_deals_company_id_source_lead_id", table: "deals", columns: new[] { "company_id", "source_lead_id" });
            migrationBuilder.CreateIndex(name: "IX_deals_company_id_customer_company_id", table: "deals", columns: new[] { "company_id", "customer_company_id" });
            migrationBuilder.CreateIndex(name: "IX_deals_company_id_primary_contact_id", table: "deals", columns: new[] { "company_id", "primary_contact_id" });

            migrationBuilder.CreateIndex(name: "IX_sales_activities_company_id", table: "sales_activities", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_sales_activities_status", table: "sales_activities", column: "status");
            migrationBuilder.CreateIndex(name: "IX_sales_activities_created_at", table: "sales_activities", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_sales_activities_company_id_lead_id", table: "sales_activities", columns: new[] { "company_id", "lead_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_activities_company_id_deal_id", table: "sales_activities", columns: new[] { "company_id", "deal_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_activities_company_id_contact_id", table: "sales_activities", columns: new[] { "company_id", "contact_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_activities_company_id_customer_company_id", table: "sales_activities", columns: new[] { "company_id", "customer_company_id" });

            migrationBuilder.CreateIndex(name: "IX_sales_agent_recommendations_company_id", table: "sales_agent_recommendations", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_sales_agent_recommendations_status", table: "sales_agent_recommendations", column: "status");
            migrationBuilder.CreateIndex(name: "IX_sales_agent_recommendations_created_at", table: "sales_agent_recommendations", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_sales_agent_recommendations_company_id_lead_id", table: "sales_agent_recommendations", columns: new[] { "company_id", "lead_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_agent_recommendations_company_id_deal_id", table: "sales_agent_recommendations", columns: new[] { "company_id", "deal_id" });

            migrationBuilder.CreateIndex(name: "IX_sales_action_approvals_company_id", table: "sales_action_approvals", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_sales_action_approvals_status", table: "sales_action_approvals", column: "status");
            migrationBuilder.CreateIndex(name: "IX_sales_action_approvals_created_at", table: "sales_action_approvals", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_sales_action_approvals_company_id_recommendation_id", table: "sales_action_approvals", columns: new[] { "company_id", "recommendation_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_action_approvals_company_id_lead_id", table: "sales_action_approvals", columns: new[] { "company_id", "lead_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_action_approvals_company_id_deal_id", table: "sales_action_approvals", columns: new[] { "company_id", "deal_id" });

            migrationBuilder.CreateIndex(name: "IX_sales_email_links_company_id", table: "sales_email_links", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_sales_email_links_status", table: "sales_email_links", column: "status");
            migrationBuilder.CreateIndex(name: "IX_sales_email_links_created_at", table: "sales_email_links", column: "created_at");
            migrationBuilder.CreateIndex(name: "IX_sales_email_links_company_id_external_message_id", table: "sales_email_links", columns: new[] { "company_id", "external_message_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_email_links_company_id_lead_id", table: "sales_email_links", columns: new[] { "company_id", "lead_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_email_links_company_id_deal_id", table: "sales_email_links", columns: new[] { "company_id", "deal_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_email_links_company_id_contact_id", table: "sales_email_links", columns: new[] { "company_id", "contact_id" });
            migrationBuilder.CreateIndex(name: "IX_sales_email_links_company_id_customer_company_id", table: "sales_email_links", columns: new[] { "company_id", "customer_company_id" });
        }
    }
}
