using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260504220000_AddSalesQualificationAutomationAndHandoffs")]
public partial class AddSalesQualificationAutomationAndHandoffs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("fit", "leads", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>("temperature", "leads", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("priority", "leads", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("suggested_next_action", "leads", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<DateTime>("qualified_at", "leads", nullable: true);
        migrationBuilder.AddColumn<Guid>("qualified_by_user_id", "leads", nullable: true);

        migrationBuilder.AddColumn<string>("category", "sales_agent_recommendations", maxLength: 64, nullable: false, defaultValue: "follow_up");
        migrationBuilder.AddColumn<string>("trigger_condition", "sales_agent_recommendations", maxLength: 80, nullable: false, defaultValue: "manual_review");
        migrationBuilder.AddColumn<string>("action_type", "sales_agent_recommendations", maxLength: 80, nullable: false, defaultValue: "create_draft_reply");
        migrationBuilder.AddColumn<string>("risk_level", "sales_agent_recommendations", maxLength: 32, nullable: false, defaultValue: "medium");
        migrationBuilder.AddColumn<bool>("requires_approval", "sales_agent_recommendations", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("approval_status", "sales_agent_recommendations", maxLength: 32, nullable: false, defaultValue: "waiting_for_approval");
        migrationBuilder.AddColumn<string>("execution_status", "sales_agent_recommendations", maxLength: 32, nullable: false, defaultValue: "pending");
        migrationBuilder.AddColumn<string>("failure_summary", "sales_agent_recommendations", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>("dedupe_key", "sales_agent_recommendations", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<decimal>("confidence", "sales_agent_recommendations", type: "decimal(5,4)", nullable: true);

        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[sales_automation_policies]', N'U') IS NULL
BEGIN
    CREATE TABLE [sales_automation_policies] (
        [id] uniqueidentifier NOT NULL,
        [company_id] uniqueidentifier NOT NULL,
        [mode] nvarchar(80) NOT NULL,
        [finance_documents_always_require_approval] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_finance_documents_always_require_approval] DEFAULT CAST(1 AS bit),
        [created_at] datetime2 NOT NULL,
        [updated_at] datetime2 NOT NULL,
        CONSTRAINT [PK_sales_automation_policies] PRIMARY KEY ([id]),
        CONSTRAINT [AK_sales_automation_policies_company_id_id] UNIQUE ([company_id], [id]),
        CONSTRAINT [FK_sales_automation_policies_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
    );
END;

IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'mode') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [mode] nvarchar(80) NOT NULL CONSTRAINT [DF_sales_automation_policies_mode] DEFAULT N'assistive';
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'finance_documents_always_require_approval') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [finance_documents_always_require_approval] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_finance_documents_always_require_approval] DEFAULT CAST(1 AS bit);
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'created_at') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [created_at] datetime2 NOT NULL CONSTRAINT [DF_sales_automation_policies_created_at] DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'updated_at') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [updated_at] datetime2 NOT NULL CONSTRAINT [DF_sales_automation_policies_updated_at] DEFAULT SYSUTCDATETIME();
");

        migrationBuilder.CreateTable(
            name: "sales_finance_handoffs",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                company_id = table.Column<Guid>(nullable: false),
                deal_id = table.Column<Guid>(nullable: false),
                status = table.Column<string>(maxLength: 32, nullable: false),
                approval_status = table.Column<string>(maxLength: 32, nullable: false),
                execution_status = table.Column<string>(maxLength: 32, nullable: false),
                summary = table.Column<string>(maxLength: 1000, nullable: false),
                dedupe_key = table.Column<string>(maxLength: 256, nullable: false),
                external_document_id = table.Column<string>(maxLength: 256, nullable: true),
                failure_summary = table.Column<string>(maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_finance_handoffs", x => x.id);
                table.UniqueConstraint("AK_sales_finance_handoffs_company_id_id", x => new { x.company_id, x.id });
                table.ForeignKey("FK_sales_finance_handoffs_deals_company_id_deal_id", x => new { x.company_id, x.deal_id }, "deals", new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_sales_agent_recommendations_company_id_approval_status", "sales_agent_recommendations", new[] { "company_id", "approval_status" });
        migrationBuilder.CreateIndex("IX_sales_agent_recommendations_company_id_execution_status", "sales_agent_recommendations", new[] { "company_id", "execution_status" });
        migrationBuilder.CreateIndex("IX_sales_agent_recommendations_company_id_dedupe_key", "sales_agent_recommendations", new[] { "company_id", "dedupe_key" }, unique: true, filter: "[dedupe_key] IS NOT NULL");
        migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_sales_automation_policies_company_id'
      AND object_id = OBJECT_ID(N'[dbo].[sales_automation_policies]'))
BEGIN
    CREATE UNIQUE INDEX [IX_sales_automation_policies_company_id]
    ON [sales_automation_policies] ([company_id]);
END;
");
        migrationBuilder.CreateIndex("IX_sales_finance_handoffs_company_id_deal_id", "sales_finance_handoffs", new[] { "company_id", "deal_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_sales_finance_handoffs_company_id_dedupe_key", "sales_finance_handoffs", new[] { "company_id", "dedupe_key" }, unique: true);
        migrationBuilder.CreateIndex("IX_sales_finance_handoffs_company_id_approval_status", "sales_finance_handoffs", new[] { "company_id", "approval_status" });
        migrationBuilder.CreateIndex("IX_sales_finance_handoffs_company_id_execution_status", "sales_finance_handoffs", new[] { "company_id", "execution_status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("sales_finance_handoffs");
        migrationBuilder.DropTable("sales_automation_policies");
        migrationBuilder.DropIndex("IX_sales_agent_recommendations_company_id_approval_status", "sales_agent_recommendations");
        migrationBuilder.DropIndex("IX_sales_agent_recommendations_company_id_execution_status", "sales_agent_recommendations");
        migrationBuilder.DropIndex("IX_sales_agent_recommendations_company_id_dedupe_key", "sales_agent_recommendations");
        migrationBuilder.DropColumn("fit", "leads");
        migrationBuilder.DropColumn("temperature", "leads");
        migrationBuilder.DropColumn("priority", "leads");
        migrationBuilder.DropColumn("suggested_next_action", "leads");
        migrationBuilder.DropColumn("qualified_at", "leads");
        migrationBuilder.DropColumn("qualified_by_user_id", "leads");
        migrationBuilder.DropColumn("category", "sales_agent_recommendations");
        migrationBuilder.DropColumn("trigger_condition", "sales_agent_recommendations");
        migrationBuilder.DropColumn("action_type", "sales_agent_recommendations");
        migrationBuilder.DropColumn("risk_level", "sales_agent_recommendations");
        migrationBuilder.DropColumn("requires_approval", "sales_agent_recommendations");
        migrationBuilder.DropColumn("approval_status", "sales_agent_recommendations");
        migrationBuilder.DropColumn("execution_status", "sales_agent_recommendations");
        migrationBuilder.DropColumn("failure_summary", "sales_agent_recommendations");
        migrationBuilder.DropColumn("dedupe_key", "sales_agent_recommendations");
        migrationBuilder.DropColumn("confidence", "sales_agent_recommendations");
    }
}
