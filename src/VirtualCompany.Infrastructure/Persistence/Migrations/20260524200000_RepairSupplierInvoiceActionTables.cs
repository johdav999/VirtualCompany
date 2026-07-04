using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260524200000_RepairSupplierInvoiceActionTables")]
    public partial class RepairSupplierInvoiceActionTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[supplier_invoice_source_document_attachments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[supplier_invoice_source_document_attachments] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [bill_id] uniqueidentifier NOT NULL,
                        [document_id] uniqueidentifier NULL,
                        [status] nvarchar(64) NOT NULL CONSTRAINT [DF_supplier_invoice_source_document_attachments_status] DEFAULT N'not_attached',
                        [provider_key] nvarchar(64) NULL,
                        [connection_id] uniqueidentifier NULL,
                        [requested_by_user_id] uniqueidentifier NULL,
                        [requested_at] datetime2 NULL,
                        [attached_at] datetime2 NULL,
                        [response_summary] nvarchar(1000) NULL,
                        [provider_metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_source_document_attachments_provider_metadata_json] DEFAULT N'{}',
                        [audit_trail_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_source_document_attachments_audit_trail_json] DEFAULT N'{}',
                        [created_at] datetime2 NOT NULL,
                        [updated_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_supplier_invoice_source_document_attachments] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_supplier_invoice_source_document_attachments_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_supplier_invoice_source_document_attachments_status] CHECK ([status] IN ('not_attached', 'attachment_requested', 'attached', 'failed', 'not_available')),
                        CONSTRAINT [FK_supplier_invoice_source_document_attachments_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [dbo].[companies] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_supplier_invoice_source_document_attachments_finance_bills_company_id_bill_id] FOREIGN KEY ([company_id], [bill_id]) REFERENCES [dbo].[finance_bills] ([company_id], [id]),
                        CONSTRAINT [FK_supplier_invoice_source_document_attachments_knowledge_documents_company_id_document_id] FOREIGN KEY ([company_id], [document_id]) REFERENCES [dbo].[knowledge_documents] ([CompanyId], [Id])
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_source_document_attachments_company_id_bill_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_source_document_attachments]'))
                    CREATE UNIQUE INDEX [IX_supplier_invoice_source_document_attachments_company_id_bill_id] ON [dbo].[supplier_invoice_source_document_attachments] ([company_id], [bill_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_source_document_attachments_company_id_status_updated_at' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_source_document_attachments]'))
                    CREATE INDEX [IX_supplier_invoice_source_document_attachments_company_id_status_updated_at] ON [dbo].[supplier_invoice_source_document_attachments] ([company_id], [status], [updated_at]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_source_document_attachments_company_id_document_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_source_document_attachments]'))
                    CREATE INDEX [IX_supplier_invoice_source_document_attachments_company_id_document_id] ON [dbo].[supplier_invoice_source_document_attachments] ([company_id], [document_id]);

                IF OBJECT_ID(N'[dbo].[supplier_invoice_draft_actions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[supplier_invoice_draft_actions] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [bill_id] uniqueidentifier NOT NULL,
                        [status] nvarchar(64) NOT NULL CONSTRAINT [DF_supplier_invoice_draft_actions_status] DEFAULT N'draft',
                        [provider_key] nvarchar(64) NULL,
                        [connection_id] uniqueidentifier NULL,
                        [requested_by_user_id] uniqueidentifier NULL,
                        [requested_at] datetime2 NULL,
                        [updated_in_provider_at] datetime2 NULL,
                        [booked_at] datetime2 NULL,
                        [response_summary] nvarchar(1000) NULL,
                        [provider_metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_draft_actions_provider_metadata_json] DEFAULT N'{}',
                        [audit_trail_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_draft_actions_audit_trail_json] DEFAULT N'{}',
                        [created_at] datetime2 NOT NULL,
                        [updated_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_supplier_invoice_draft_actions] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_supplier_invoice_draft_actions_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_supplier_invoice_draft_actions_status] CHECK ([status] IN ('draft', 'update_pending', 'updated', 'bookkeeping_requested', 'booked', 'failed')),
                        CONSTRAINT [FK_supplier_invoice_draft_actions_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [dbo].[companies] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_supplier_invoice_draft_actions_finance_bills_company_id_bill_id] FOREIGN KEY ([company_id], [bill_id]) REFERENCES [dbo].[finance_bills] ([company_id], [id])
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_draft_actions_company_id_bill_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_draft_actions]'))
                    CREATE UNIQUE INDEX [IX_supplier_invoice_draft_actions_company_id_bill_id] ON [dbo].[supplier_invoice_draft_actions] ([company_id], [bill_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_draft_actions_company_id_status_updated_at' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_draft_actions]'))
                    CREATE INDEX [IX_supplier_invoice_draft_actions_company_id_status_updated_at] ON [dbo].[supplier_invoice_draft_actions] ([company_id], [status], [updated_at]);

                IF OBJECT_ID(N'[dbo].[supplier_invoice_correction_actions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[supplier_invoice_correction_actions] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [bill_id] uniqueidentifier NOT NULL,
                        [action_type] nvarchar(64) NOT NULL,
                        [status] nvarchar(64) NOT NULL,
                        [provider_key] nvarchar(64) NULL,
                        [connection_id] uniqueidentifier NULL,
                        [requested_by_user_id] uniqueidentifier NULL,
                        [requested_at] datetime2 NULL,
                        [completed_at] datetime2 NULL,
                        [credit_note_bill_id] uniqueidentifier NULL,
                        [provider_credit_note_number] nvarchar(128) NULL,
                        [response_summary] nvarchar(1000) NULL,
                        [provider_metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_correction_actions_provider_metadata_json] DEFAULT N'{}',
                        [audit_trail_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_correction_actions_audit_trail_json] DEFAULT N'{}',
                        [approval_request_id] uniqueidentifier NULL,
                        [approved_by_user_id] uniqueidentifier NULL,
                        [approved_at] datetime2 NULL,
                        [task_id] uniqueidentifier NULL,
                        [created_at] datetime2 NOT NULL,
                        [updated_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_supplier_invoice_correction_actions] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_supplier_invoice_correction_actions_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_supplier_invoice_correction_actions_action_type] CHECK ([action_type] IN ('cancellation', 'credit_note')),
                        CONSTRAINT [CK_supplier_invoice_correction_actions_status] CHECK ([status] IN ('cancellation_requested', 'cancelled', 'cancellation_failed', 'credit_note_requested', 'credit_note_created', 'credit_note_failed')),
                        CONSTRAINT [FK_supplier_invoice_correction_actions_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [dbo].[companies] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_supplier_invoice_correction_actions_finance_bills_company_id_bill_id] FOREIGN KEY ([company_id], [bill_id]) REFERENCES [dbo].[finance_bills] ([company_id], [id]),
                        CONSTRAINT [FK_supplier_invoice_correction_actions_finance_bills_company_id_credit_note_bill_id] FOREIGN KEY ([company_id], [credit_note_bill_id]) REFERENCES [dbo].[finance_bills] ([company_id], [id]),
                        CONSTRAINT [FK_supplier_invoice_correction_actions_approval_requests_approval_request_id] FOREIGN KEY ([approval_request_id]) REFERENCES [dbo].[approval_requests] ([Id]),
                        CONSTRAINT [FK_supplier_invoice_correction_actions_work_tasks_task_id] FOREIGN KEY ([task_id]) REFERENCES [dbo].[tasks] ([Id])
                    );
                END
                ELSE
                BEGIN
                    IF COL_LENGTH(N'[dbo].[supplier_invoice_correction_actions]', N'approval_request_id') IS NULL
                        ALTER TABLE [dbo].[supplier_invoice_correction_actions] ADD [approval_request_id] uniqueidentifier NULL;
                    IF COL_LENGTH(N'[dbo].[supplier_invoice_correction_actions]', N'approved_by_user_id') IS NULL
                        ALTER TABLE [dbo].[supplier_invoice_correction_actions] ADD [approved_by_user_id] uniqueidentifier NULL;
                    IF COL_LENGTH(N'[dbo].[supplier_invoice_correction_actions]', N'approved_at') IS NULL
                        ALTER TABLE [dbo].[supplier_invoice_correction_actions] ADD [approved_at] datetime2 NULL;
                    IF COL_LENGTH(N'[dbo].[supplier_invoice_correction_actions]', N'task_id') IS NULL
                        ALTER TABLE [dbo].[supplier_invoice_correction_actions] ADD [task_id] uniqueidentifier NULL;
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_correction_actions_company_id_bill_id_action_type' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_correction_actions]'))
                    CREATE UNIQUE INDEX [IX_supplier_invoice_correction_actions_company_id_bill_id_action_type] ON [dbo].[supplier_invoice_correction_actions] ([company_id], [bill_id], [action_type]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_correction_actions_company_id_credit_note_bill_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_correction_actions]'))
                    CREATE INDEX [IX_supplier_invoice_correction_actions_company_id_credit_note_bill_id] ON [dbo].[supplier_invoice_correction_actions] ([company_id], [credit_note_bill_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_correction_actions_company_id_status_updated_at' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_correction_actions]'))
                    CREATE INDEX [IX_supplier_invoice_correction_actions_company_id_status_updated_at] ON [dbo].[supplier_invoice_correction_actions] ([company_id], [status], [updated_at]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_correction_actions_company_id_approval_request_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_correction_actions]'))
                    CREATE INDEX [IX_supplier_invoice_correction_actions_company_id_approval_request_id] ON [dbo].[supplier_invoice_correction_actions] ([company_id], [approval_request_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_correction_actions_approval_request_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_correction_actions]'))
                    CREATE INDEX [IX_supplier_invoice_correction_actions_approval_request_id] ON [dbo].[supplier_invoice_correction_actions] ([approval_request_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_correction_actions_task_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_correction_actions]'))
                    CREATE INDEX [IX_supplier_invoice_correction_actions_task_id] ON [dbo].[supplier_invoice_correction_actions] ([task_id]);

                IF OBJECT_ID(N'[dbo].[supplier_invoice_enrichment_actions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[supplier_invoice_enrichment_actions] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [bill_id] uniqueidentifier NOT NULL,
                        [status] nvarchar(64) NOT NULL CONSTRAINT [DF_supplier_invoice_enrichment_actions_status] DEFAULT N'not_suggested',
                        [provider_key] nvarchar(64) NULL,
                        [connection_id] uniqueidentifier NULL,
                        [requested_by_user_id] uniqueidentifier NULL,
                        [approved_by_user_id] uniqueidentifier NULL,
                        [task_id] uniqueidentifier NULL,
                        [approval_request_id] uniqueidentifier NULL,
                        [requested_at] datetime2 NULL,
                        [approved_at] datetime2 NULL,
                        [synced_at] datetime2 NULL,
                        [response_summary] nvarchar(1000) NULL,
                        [suggestion_payload_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_enrichment_actions_suggestion_payload_json] DEFAULT N'{}',
                        [reconciliation_warnings_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_enrichment_actions_reconciliation_warnings_json] DEFAULT N'[]',
                        [provider_metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_enrichment_actions_provider_metadata_json] DEFAULT N'{}',
                        [audit_trail_json] nvarchar(max) NOT NULL CONSTRAINT [DF_supplier_invoice_enrichment_actions_audit_trail_json] DEFAULT N'{}',
                        [created_at] datetime2 NOT NULL,
                        [updated_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_supplier_invoice_enrichment_actions] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_supplier_invoice_enrichment_actions_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_supplier_invoice_enrichment_actions_status] CHECK ([status] IN ('not_suggested', 'awaiting_approval', 'approved', 'sync_requested', 'synced', 'failed', 'reconciliation_warning')),
                        CONSTRAINT [FK_supplier_invoice_enrichment_actions_approval_requests_approval_request_id] FOREIGN KEY ([approval_request_id]) REFERENCES [dbo].[approval_requests] ([Id]),
                        CONSTRAINT [FK_supplier_invoice_enrichment_actions_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [dbo].[companies] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_supplier_invoice_enrichment_actions_finance_bills_company_id_bill_id] FOREIGN KEY ([company_id], [bill_id]) REFERENCES [dbo].[finance_bills] ([company_id], [id]),
                        CONSTRAINT [FK_supplier_invoice_enrichment_actions_work_tasks_task_id] FOREIGN KEY ([task_id]) REFERENCES [dbo].[tasks] ([Id])
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_enrichment_actions_approval_request_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_enrichment_actions]'))
                    CREATE INDEX [IX_supplier_invoice_enrichment_actions_approval_request_id] ON [dbo].[supplier_invoice_enrichment_actions] ([approval_request_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_enrichment_actions_company_id_approval_request_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_enrichment_actions]'))
                    CREATE INDEX [IX_supplier_invoice_enrichment_actions_company_id_approval_request_id] ON [dbo].[supplier_invoice_enrichment_actions] ([company_id], [approval_request_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_enrichment_actions_company_id_bill_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_enrichment_actions]'))
                    CREATE UNIQUE INDEX [IX_supplier_invoice_enrichment_actions_company_id_bill_id] ON [dbo].[supplier_invoice_enrichment_actions] ([company_id], [bill_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_enrichment_actions_company_id_status_updated_at' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_enrichment_actions]'))
                    CREATE INDEX [IX_supplier_invoice_enrichment_actions_company_id_status_updated_at] ON [dbo].[supplier_invoice_enrichment_actions] ([company_id], [status], [updated_at]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_supplier_invoice_enrichment_actions_task_id' AND [object_id] = OBJECT_ID(N'[dbo].[supplier_invoice_enrichment_actions]'))
                    CREATE INDEX [IX_supplier_invoice_enrichment_actions_task_id] ON [dbo].[supplier_invoice_enrichment_actions] ([task_id]);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
