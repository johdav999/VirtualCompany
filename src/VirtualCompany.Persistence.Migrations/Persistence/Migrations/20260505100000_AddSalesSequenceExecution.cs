using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505100000_AddSalesSequenceExecution")]
public partial class AddSalesSequenceExecution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "approval_required",
            table: "sales_campaigns",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "approval_status",
            table: "sales_campaigns",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "approval_requested_at",
            table: "sales_campaigns",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "approved_at",
            table: "sales_campaigns",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "max_emails_per_day",
            table: "sales_campaigns",
            type: "int",
            nullable: false,
            defaultValue: 50);

        migrationBuilder.AddColumn<bool>(
            name: "outbound_enabled",
            table: "sales_campaigns",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateTable(
            name: "sales_sequence_executions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sales_campaign_contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                stop_reason = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                stopped_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_sequence_executions", x => x.id);
                table.UniqueConstraint("AK_sales_sequence_executions_company_id_id", x => new { x.company_id, x.id });
                table.ForeignKey(
                    name: "FK_sales_sequence_executions_contacts_company_id_contact_id",
                    columns: x => new { x.company_id, x.contact_id },
                    principalTable: "contacts",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_sequence_executions_sales_campaign_contacts_company_id_sales_campaign_contact_id",
                    columns: x => new { x.company_id, x.sales_campaign_contact_id },
                    principalTable: "sales_campaign_contacts",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_sequence_executions_sales_campaigns_company_id_sales_campaign_id",
                    columns: x => new { x.company_id, x.sales_campaign_id },
                    principalTable: "sales_campaigns",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_sequence_execution_steps",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sequence_execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sales_sequence_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                step_order = table.Column<int>(type: "int", nullable: false),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                scheduled_send_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                delivery_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                bounce_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                bounce_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                mailbox_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                provider_message_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                provider_thread_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                internet_message_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                idempotency_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_sequence_execution_steps", x => x.id);
                table.UniqueConstraint("AK_sales_sequence_execution_steps_company_id_id", x => new { x.company_id, x.id });
                table.ForeignKey(
                    name: "FK_sales_sequence_execution_steps_sales_sequence_executions_company_id_sequence_execution_id",
                    columns: x => new { x.company_id, x.sequence_execution_id },
                    principalTable: "sales_sequence_executions",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_sales_sequence_execution_steps_sales_sequence_steps_company_id_sales_sequence_step_id",
                    columns: x => new { x.company_id, x.sales_sequence_step_id },
                    principalTable: "sales_sequence_steps",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_executions_company_id_contact_id_status",
            table: "sales_sequence_executions",
            columns: new[] { "company_id", "contact_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_executions_company_id_sales_campaign_id_contact_id",
            table: "sales_sequence_executions",
            columns: new[] { "company_id", "sales_campaign_id", "contact_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_executions_company_id_status_updated_at",
            table: "sales_sequence_executions",
            columns: new[] { "company_id", "status", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_execution_steps_company_id_scheduled_send_at_status",
            table: "sales_sequence_execution_steps",
            columns: new[] { "company_id", "scheduled_send_at", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_execution_steps_company_id_provider_message_id",
            table: "sales_sequence_execution_steps",
            columns: new[] { "company_id", "provider_message_id" },
            filter: "[provider_message_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_execution_steps_company_id_provider_thread_id",
            table: "sales_sequence_execution_steps",
            columns: new[] { "company_id", "provider_thread_id" },
            filter: "[provider_thread_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_execution_steps_company_id_idempotency_key",
            table: "sales_sequence_execution_steps",
            columns: new[] { "company_id", "idempotency_key" },
            unique: true);

        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[sales_sequence_execution_steps]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[sales_sequence_execution_steps]', N'policy_decision_outcome') IS NULL
        ALTER TABLE [sales_sequence_execution_steps] ADD [policy_decision_outcome] nvarchar(32) NULL;
    IF COL_LENGTH(N'[dbo].[sales_sequence_execution_steps]', N'policy_decision_reason_code') IS NULL
        ALTER TABLE [sales_sequence_execution_steps] ADD [policy_decision_reason_code] nvarchar(120) NULL;
    IF COL_LENGTH(N'[dbo].[sales_sequence_execution_steps]', N'policy_decision_reason') IS NULL
        ALTER TABLE [sales_sequence_execution_steps] ADD [policy_decision_reason] nvarchar(1000) NULL;
    IF COL_LENGTH(N'[dbo].[sales_sequence_execution_steps]', N'outbound_message_review_id') IS NULL
        ALTER TABLE [sales_sequence_execution_steps] ADD [outbound_message_review_id] uniqueidentifier NULL;
    IF COL_LENGTH(N'[dbo].[sales_sequence_execution_steps]', N'policy_evaluated_at') IS NULL
        ALTER TABLE [sales_sequence_execution_steps] ADD [policy_evaluated_at] datetime2 NULL;
END;
");

        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[outbound_message_reviews]', N'U') IS NULL
   AND OBJECT_ID(N'[dbo].[sales_sequence_execution_steps]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[contacts]', N'U') IS NOT NULL
BEGIN
    CREATE TABLE [outbound_message_reviews] (
        [id] uniqueidentifier NOT NULL,
        [company_id] uniqueidentifier NOT NULL,
        [sequence_execution_step_id] uniqueidentifier NOT NULL,
        [sales_campaign_id] uniqueidentifier NOT NULL,
        [contact_id] uniqueidentifier NOT NULL,
        [category] nvarchar(64) NOT NULL,
        [reason_code] nvarchar(120) NOT NULL,
        [reason] nvarchar(1000) NOT NULL,
        [original_subject] nvarchar(300) NOT NULL,
        [original_body] nvarchar(max) NOT NULL,
        [edited_subject] nvarchar(300) NULL,
        [edited_body] nvarchar(max) NULL,
        [status] nvarchar(32) NOT NULL,
        [decided_by_user_id] uniqueidentifier NULL,
        [decided_at] datetime2 NULL,
        [decision_comment] nvarchar(1000) NULL,
        [requested_at] datetime2 NOT NULL,
        [created_at] datetime2 NOT NULL,
        [updated_at] datetime2 NOT NULL,
        CONSTRAINT [PK_outbound_message_reviews] PRIMARY KEY ([id]),
        CONSTRAINT [AK_outbound_message_reviews_company_id_id] UNIQUE ([company_id], [id]),
        CONSTRAINT [FK_outbound_message_reviews_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_outbound_message_reviews_contacts_company_id_contact_id] FOREIGN KEY ([company_id], [contact_id]) REFERENCES [contacts] ([company_id], [id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_outbound_message_reviews_sales_sequence_execution_steps_company_id_sequence_execution_step_id] FOREIGN KEY ([company_id], [sequence_execution_step_id]) REFERENCES [sales_sequence_execution_steps] ([company_id], [id]) ON DELETE NO ACTION
    );
END;
");

        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[sales_sequence_execution_steps]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sales_sequence_execution_steps_company_id_policy_decision_outcome_policy_evaluated_at' AND object_id = OBJECT_ID(N'[dbo].[sales_sequence_execution_steps]'))
        CREATE INDEX [IX_sales_sequence_execution_steps_company_id_policy_decision_outcome_policy_evaluated_at] ON [sales_sequence_execution_steps] ([company_id], [policy_decision_outcome], [policy_evaluated_at]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_sales_sequence_execution_steps_company_id_outbound_message_review_id' AND object_id = OBJECT_ID(N'[dbo].[sales_sequence_execution_steps]'))
        CREATE INDEX [IX_sales_sequence_execution_steps_company_id_outbound_message_review_id] ON [sales_sequence_execution_steps] ([company_id], [outbound_message_review_id]) WHERE [outbound_message_review_id] IS NOT NULL;
END;

IF OBJECT_ID(N'[dbo].[outbound_message_reviews]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_outbound_message_reviews_company_id_status_requested_at' AND object_id = OBJECT_ID(N'[dbo].[outbound_message_reviews]'))
        CREATE INDEX [IX_outbound_message_reviews_company_id_status_requested_at] ON [outbound_message_reviews] ([company_id], [status], [requested_at]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_outbound_message_reviews_company_id_sequence_execution_step_id' AND object_id = OBJECT_ID(N'[dbo].[outbound_message_reviews]'))
        CREATE UNIQUE INDEX [IX_outbound_message_reviews_company_id_sequence_execution_step_id] ON [outbound_message_reviews] ([company_id], [sequence_execution_step_id]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_outbound_message_reviews_company_id_contact_id' AND object_id = OBJECT_ID(N'[dbo].[outbound_message_reviews]'))
        CREATE INDEX [IX_outbound_message_reviews_company_id_contact_id] ON [outbound_message_reviews] ([company_id], [contact_id]);
END;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("sales_sequence_execution_steps");
        migrationBuilder.DropTable("sales_sequence_executions");
        migrationBuilder.DropColumn("approval_required", "sales_campaigns");
        migrationBuilder.DropColumn("approval_status", "sales_campaigns");
        migrationBuilder.DropColumn("approval_requested_at", "sales_campaigns");
        migrationBuilder.DropColumn("approved_at", "sales_campaigns");
        migrationBuilder.DropColumn("max_emails_per_day", "sales_campaigns");
        migrationBuilder.DropColumn("outbound_enabled", "sales_campaigns");
    }
}
