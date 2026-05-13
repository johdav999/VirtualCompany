using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260504193000_AddOutboundAutomationPolicyAndWebsiteLeadCapture")]
    public partial class AddOutboundAutomationPolicyAndWebsiteLeadCapture : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[sales_automation_policies]', N'U') IS NULL
BEGIN
    CREATE TABLE [sales_automation_policies] (
        [id] uniqueidentifier NOT NULL,
        [company_id] uniqueidentifier NOT NULL,
        [mode] nvarchar(80) NOT NULL,
        [finance_documents_always_require_approval] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_finance_documents_always_require_approval] DEFAULT CAST(1 AS bit),
        [outbound_enabled] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_outbound_enabled] DEFAULT CAST(0 AS bit),
        [max_emails_per_day] int NOT NULL CONSTRAINT [DF_sales_automation_policies_max_emails_per_day] DEFAULT 25,
        [require_approval_first_contact] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_first_contact] DEFAULT CAST(1 AS bit),
        [require_approval_pricing_discussion] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_pricing_discussion] DEFAULT CAST(1 AS bit),
        [require_approval_follow_ups] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_follow_ups] DEFAULT CAST(1 AS bit),
        [require_approval_re_engagement] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_re_engagement] DEFAULT CAST(1 AS bit),
        [website_lead_deduplication_window_minutes] int NOT NULL CONSTRAINT [DF_sales_automation_policies_website_lead_deduplication_window_minutes] DEFAULT 10080,
        [website_lead_follow_up_sequence_id] uniqueidentifier NULL,
        [created_at] datetime2 NOT NULL,
        [updated_at] datetime2 NOT NULL,
        CONSTRAINT [PK_sales_automation_policies] PRIMARY KEY ([id]),
        CONSTRAINT [AK_sales_automation_policies_company_id_id] UNIQUE ([company_id], [id]),
        CONSTRAINT [FK_sales_automation_policies_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
    );
END;

IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'outbound_enabled') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [outbound_enabled] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_outbound_enabled] DEFAULT CAST(0 AS bit);
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'max_emails_per_day') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [max_emails_per_day] int NOT NULL CONSTRAINT [DF_sales_automation_policies_max_emails_per_day] DEFAULT 25;
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'require_approval_first_contact') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [require_approval_first_contact] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_first_contact] DEFAULT CAST(1 AS bit);
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'require_approval_pricing_discussion') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [require_approval_pricing_discussion] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_pricing_discussion] DEFAULT CAST(1 AS bit);
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'require_approval_follow_ups') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [require_approval_follow_ups] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_follow_ups] DEFAULT CAST(1 AS bit);
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'require_approval_re_engagement') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [require_approval_re_engagement] bit NOT NULL CONSTRAINT [DF_sales_automation_policies_require_approval_re_engagement] DEFAULT CAST(1 AS bit);
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'website_lead_deduplication_window_minutes') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [website_lead_deduplication_window_minutes] int NOT NULL CONSTRAINT [DF_sales_automation_policies_website_lead_deduplication_window_minutes] DEFAULT 10080;
IF COL_LENGTH(N'[dbo].[sales_automation_policies]', N'website_lead_follow_up_sequence_id') IS NULL
    ALTER TABLE [sales_automation_policies] ADD [website_lead_follow_up_sequence_id] uniqueidentifier NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_sales_automation_policies_company_id'
      AND object_id = OBJECT_ID(N'[dbo].[sales_automation_policies]'))
BEGIN
    CREATE UNIQUE INDEX [IX_sales_automation_policies_company_id]
    ON [sales_automation_policies] ([company_id]);
END;

IF OBJECT_ID(N'[dbo].[leads]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[leads]', N'website_submission_email') IS NULL
        ALTER TABLE [leads] ADD [website_submission_email] nvarchar(256) NULL;
    IF COL_LENGTH(N'[dbo].[leads]', N'website_lead_submission_id') IS NULL
        ALTER TABLE [leads] ADD [website_lead_submission_id] uniqueidentifier NULL;
END;

IF OBJECT_ID(N'[dbo].[website_lead_submissions]', N'U') IS NULL
   AND OBJECT_ID(N'[dbo].[leads]', N'U') IS NOT NULL
BEGIN
    CREATE TABLE [website_lead_submissions] (
        [id] uniqueidentifier NOT NULL,
        [company_id] uniqueidentifier NOT NULL,
        [lead_id] uniqueidentifier NULL,
        [contact_id] uniqueidentifier NULL,
        [merged_into_submission_id] uniqueidentifier NULL,
        [enrollment_outbox_message_id] uniqueidentifier NULL,
        [normalized_email] nvarchar(256) NOT NULL,
        [name] nvarchar(160) NULL,
        [company_name] nvarchar(200) NULL,
        [message] nvarchar(2000) NULL,
        [source_url] nvarchar(512) NULL,
        [form_id] nvarchar(120) NULL,
        [status] nvarchar(32) NOT NULL,
        [received_at] datetime2 NOT NULL,
        [created_at] datetime2 NOT NULL,
        [updated_at] datetime2 NOT NULL,
        CONSTRAINT [PK_website_lead_submissions] PRIMARY KEY ([id]),
        CONSTRAINT [AK_website_lead_submissions_company_id_id] UNIQUE ([company_id], [id]),
        CONSTRAINT [FK_website_lead_submissions_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_website_lead_submissions_leads_company_id_lead_id] FOREIGN KEY ([company_id], [lead_id]) REFERENCES [leads] ([company_id], [id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'[dbo].[website_lead_submissions]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_website_lead_submissions_company_id_normalized_email_received_at' AND object_id = OBJECT_ID(N'[dbo].[website_lead_submissions]'))
        CREATE INDEX [IX_website_lead_submissions_company_id_normalized_email_received_at] ON [website_lead_submissions] ([company_id], [normalized_email], [received_at]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_website_lead_submissions_company_id_status_received_at' AND object_id = OBJECT_ID(N'[dbo].[website_lead_submissions]'))
        CREATE INDEX [IX_website_lead_submissions_company_id_status_received_at] ON [website_lead_submissions] ([company_id], [status], [received_at]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_website_lead_submissions_company_id_lead_id' AND object_id = OBJECT_ID(N'[dbo].[website_lead_submissions]'))
        CREATE INDEX [IX_website_lead_submissions_company_id_lead_id] ON [website_lead_submissions] ([company_id], [lead_id]) WHERE [lead_id] IS NOT NULL;
END;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("outbound_message_reviews");
            migrationBuilder.DropTable("website_lead_submissions");
            migrationBuilder.DropIndex("IX_sales_sequence_execution_steps_company_id_policy_decision_outcome_policy_evaluated_at", "sales_sequence_execution_steps");
            migrationBuilder.DropIndex("IX_sales_sequence_execution_steps_company_id_outbound_message_review_id", "sales_sequence_execution_steps");
            migrationBuilder.DropColumn("outbound_enabled", "sales_automation_policies");
            migrationBuilder.DropColumn("max_emails_per_day", "sales_automation_policies");
            migrationBuilder.DropColumn("require_approval_first_contact", "sales_automation_policies");
            migrationBuilder.DropColumn("require_approval_pricing_discussion", "sales_automation_policies");
            migrationBuilder.DropColumn("require_approval_follow_ups", "sales_automation_policies");
            migrationBuilder.DropColumn("require_approval_re_engagement", "sales_automation_policies");
            migrationBuilder.DropColumn("website_lead_deduplication_window_minutes", "sales_automation_policies");
            migrationBuilder.DropColumn("website_lead_follow_up_sequence_id", "sales_automation_policies");
            migrationBuilder.DropColumn("website_submission_email", "leads");
            migrationBuilder.DropColumn("website_lead_submission_id", "leads");
            migrationBuilder.DropColumn("policy_decision_outcome", "sales_sequence_execution_steps");
            migrationBuilder.DropColumn("policy_decision_reason_code", "sales_sequence_execution_steps");
            migrationBuilder.DropColumn("policy_decision_reason", "sales_sequence_execution_steps");
            migrationBuilder.DropColumn("outbound_message_review_id", "sales_sequence_execution_steps");
            migrationBuilder.DropColumn("policy_evaluated_at", "sales_sequence_execution_steps");
        }
    }
}
