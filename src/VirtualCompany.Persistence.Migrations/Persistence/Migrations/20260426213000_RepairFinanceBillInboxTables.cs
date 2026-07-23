using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Migration("20260426213000_RepairFinanceBillInboxTables")]
    [DbContext(typeof(VirtualCompanyDbContext))]
    public partial class RepairFinanceBillInboxTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                return;
            }

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[bill_duplicate_checks]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [bill_duplicate_checks] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [supplier_name] nvarchar(200) NULL,
                        [supplier_org_number] nvarchar(64) NULL,
                        [invoice_number] nvarchar(64) NULL,
                        [total_amount] decimal(18,2) NULL,
                        [currency] nvarchar(3) NULL,
                        [is_duplicate] bit NOT NULL,
                        [result_status] nvarchar(32) NOT NULL CONSTRAINT [DF_bill_duplicate_checks_result_status] DEFAULT N'not_duplicate',
                        [matched_bill_ids_json] nvarchar(max) NOT NULL,
                        [criteria_summary] nvarchar(1000) NOT NULL,
                        [source_email_id] nvarchar(512) NULL,
                        [source_attachment_id] nvarchar(512) NULL,
                        [checked_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_bill_duplicate_checks] PRIMARY KEY ([id]),
                        CONSTRAINT [CK_bill_duplicate_checks_result_status] CHECK ([result_status] IN ('pending', 'not_duplicate', 'duplicate', 'inconclusive')),
                        CONSTRAINT [FK_bill_duplicate_checks_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                    );
                END

                IF COL_LENGTH(N'bill_duplicate_checks', N'result_status') IS NULL
                    ALTER TABLE [bill_duplicate_checks] ADD [result_status] nvarchar(32) NOT NULL CONSTRAINT [DF_bill_duplicate_checks_result_status] DEFAULT N'not_duplicate';

                IF OBJECT_ID(N'[CK_bill_duplicate_checks_result_status]', N'C') IS NULL
                    ALTER TABLE [bill_duplicate_checks] ADD CONSTRAINT [CK_bill_duplicate_checks_result_status] CHECK ([result_status] IN ('pending', 'not_duplicate', 'duplicate', 'inconclusive'));
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[normalized_bill_extractions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [normalized_bill_extractions] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [supplier_name] nvarchar(200) NULL,
                        [supplier_org_number] nvarchar(64) NULL,
                        [invoice_number] nvarchar(64) NULL,
                        [invoice_date] datetime2 NULL,
                        [due_date] datetime2 NULL,
                        [currency] nvarchar(3) NULL,
                        [total_amount] decimal(18,2) NULL,
                        [vat_amount] decimal(18,2) NULL,
                        [payment_reference] nvarchar(128) NULL,
                        [bankgiro] nvarchar(32) NULL,
                        [plusgiro] nvarchar(32) NULL,
                        [iban] nvarchar(34) NULL,
                        [bic] nvarchar(11) NULL,
                        [confidence] nvarchar(16) NOT NULL,
                        [source_email_id] nvarchar(512) NULL,
                        [source_attachment_id] nvarchar(512) NULL,
                        [evidence_json] nvarchar(max) NOT NULL,
                        [validation_status] nvarchar(32) NOT NULL,
                        [validation_findings_json] nvarchar(max) NOT NULL,
                        [duplicate_check_id] uniqueidentifier NOT NULL,
                        [requires_review] bit NOT NULL,
                        [is_eligible_for_approval_proposal] bit NOT NULL,
                        [validation_status_persisted_at] datetime2 NOT NULL,
                        [created_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_normalized_bill_extractions] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_normalized_bill_extractions_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_normalized_bill_extractions_confidence] CHECK ([confidence] IN ('high', 'medium', 'low')),
                        CONSTRAINT [CK_normalized_bill_extractions_validation_status] CHECK ([validation_status] IN ('pending', 'valid', 'flagged', 'rejected')),
                        CONSTRAINT [FK_normalized_bill_extractions_bill_duplicate_checks_duplicate_check_id] FOREIGN KEY ([duplicate_check_id]) REFERENCES [bill_duplicate_checks] ([id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_normalized_bill_extractions_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[detected_bills]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [detected_bills] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [supplier_name] nvarchar(200) NULL,
                        [supplier_org_number] nvarchar(64) NULL,
                        [invoice_number] nvarchar(64) NULL,
                        [invoice_date] datetime2 NULL,
                        [due_date] datetime2 NULL,
                        [currency] nvarchar(3) NULL,
                        [total_amount] decimal(18,2) NULL,
                        [vat_amount] decimal(18,2) NULL,
                        [payment_reference] nvarchar(128) NULL,
                        [bankgiro] nvarchar(32) NULL,
                        [plusgiro] nvarchar(32) NULL,
                        [iban] nvarchar(34) NULL,
                        [bic] nvarchar(11) NULL,
                        [confidence] decimal(5,4) NULL,
                        [confidence_level] nvarchar(16) NOT NULL,
                        [validation_status] nvarchar(32) NOT NULL,
                        [review_status] nvarchar(32) NOT NULL,
                        [requires_review] bit NOT NULL,
                        [is_eligible_for_approval_proposal] bit NOT NULL,
                        [validation_status_persisted] bit NOT NULL,
                        [validation_status_persisted_at] datetime2 NULL,
                        [validation_issues_json] nvarchar(max) NOT NULL,
                        [source_email_id] nvarchar(512) NULL,
                        [source_attachment_id] nvarchar(512) NULL,
                        [duplicate_check_id] uniqueidentifier NULL,
                        [created_at] datetime2 NOT NULL,
                        [updated_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_detected_bills] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_detected_bills_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_detected_bills_confidence] CHECK ([confidence] IS NULL OR ([confidence] >= 0 AND [confidence] <= 1)),
                        CONSTRAINT [CK_detected_bills_confidence_level] CHECK ([confidence_level] IN ('high', 'medium', 'low')),
                        CONSTRAINT [CK_detected_bills_review_status] CHECK ([review_status] IN ('not_required', 'required', 'completed')),
                        CONSTRAINT [CK_detected_bills_validation_status] CHECK ([validation_status] IN ('pending', 'valid', 'flagged', 'rejected')),
                        CONSTRAINT [FK_detected_bills_bill_duplicate_checks_duplicate_check_id] FOREIGN KEY ([duplicate_check_id]) REFERENCES [bill_duplicate_checks] ([id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_detected_bills_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[detected_bill_fields]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [detected_bill_fields] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [detected_bill_id] uniqueidentifier NOT NULL,
                        [field_name] nvarchar(64) NOT NULL,
                        [raw_value] nvarchar(2000) NULL,
                        [normalized_value] nvarchar(2000) NULL,
                        [source_document] nvarchar(512) NOT NULL,
                        [source_document_type] nvarchar(64) NULL,
                        [page_reference] nvarchar(128) NULL,
                        [section_reference] nvarchar(128) NULL,
                        [text_span] nvarchar(128) NULL,
                        [locator] nvarchar(512) NULL,
                        [extraction_method] nvarchar(64) NOT NULL,
                        [field_confidence] decimal(5,4) NULL,
                        [snippet] nvarchar(2000) NULL,
                        [created_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_detected_bill_fields] PRIMARY KEY ([id]),
                        CONSTRAINT [CK_detected_bill_fields_field_confidence] CHECK ([field_confidence] IS NULL OR ([field_confidence] >= 0 AND [field_confidence] <= 1)),
                        CONSTRAINT [FK_detected_bill_fields_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_detected_bill_fields_detected_bills_detected_bill_id] FOREIGN KEY ([detected_bill_id]) REFERENCES [detected_bills] ([id]) ON DELETE CASCADE
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[finance_bill_review_states]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [finance_bill_review_states] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [detected_bill_id] uniqueidentifier NOT NULL,
                        [status] nvarchar(64) NOT NULL,
                        [proposal_summary] nvarchar(4000) NOT NULL,
                        [created_at] datetime2 NOT NULL,
                        [updated_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_finance_bill_review_states] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_finance_bill_review_states_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_finance_bill_review_states_status] CHECK ([status] IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')),
                        CONSTRAINT [FK_finance_bill_review_states_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_finance_bill_review_states_detected_bills_detected_bill_id] FOREIGN KEY ([detected_bill_id]) REFERENCES [detected_bills] ([id]) ON DELETE CASCADE
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[finance_bill_review_actions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [finance_bill_review_actions] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [review_state_id] uniqueidentifier NOT NULL,
                        [detected_bill_id] uniqueidentifier NOT NULL,
                        [action] nvarchar(64) NOT NULL,
                        [actor_user_id] uniqueidentifier NULL,
                        [actor_display_name] nvarchar(200) NOT NULL,
                        [prior_status] nvarchar(64) NOT NULL,
                        [new_status] nvarchar(64) NOT NULL,
                        [rationale] nvarchar(1000) NOT NULL,
                        [occurred_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_finance_bill_review_actions] PRIMARY KEY ([id]),
                        CONSTRAINT [CK_finance_bill_review_actions_new_status] CHECK ([new_status] IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')),
                        CONSTRAINT [CK_finance_bill_review_actions_prior_status] CHECK ([prior_status] IN ('detected', 'extracted', 'needs_review', 'proposed_for_approval', 'approved', 'rejected', 'sent_to_payment_exported')),
                        CONSTRAINT [FK_finance_bill_review_actions_detected_bills_detected_bill_id] FOREIGN KEY ([detected_bill_id]) REFERENCES [detected_bills] ([id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_finance_bill_review_actions_finance_bill_review_states_review_state_id] FOREIGN KEY ([review_state_id]) REFERENCES [finance_bill_review_states] ([id]) ON DELETE CASCADE
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[bill_approval_proposals]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [bill_approval_proposals] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [detected_bill_id] uniqueidentifier NOT NULL,
                        [review_state_id] uniqueidentifier NOT NULL,
                        [summary] nvarchar(4000) NOT NULL,
                        [approved_by_user_id] uniqueidentifier NULL,
                        [approved_at] datetime2 NOT NULL,
                        [payment_execution_requested] bit NOT NULL CONSTRAINT [DF_bill_approval_proposals_payment_execution_requested] DEFAULT 0,
                        CONSTRAINT [PK_bill_approval_proposals] PRIMARY KEY ([id]),
                        CONSTRAINT [CK_bill_approval_proposals_no_payment_execution] CHECK ([payment_execution_requested] = 0),
                        CONSTRAINT [FK_bill_approval_proposals_detected_bills_detected_bill_id] FOREIGN KEY ([detected_bill_id]) REFERENCES [detected_bills] ([id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_bill_approval_proposals_finance_bill_review_states_review_state_id] FOREIGN KEY ([review_state_id]) REFERENCES [finance_bill_review_states] ([id]) ON DELETE CASCADE
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bill_duplicate_checks_company_id_checked_at' AND object_id = OBJECT_ID(N'[bill_duplicate_checks]'))
                    CREATE INDEX [IX_bill_duplicate_checks_company_id_checked_at] ON [bill_duplicate_checks] ([company_id], [checked_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bill_duplicate_checks_company_id_invoice_number_total_amount' AND object_id = OBJECT_ID(N'[bill_duplicate_checks]'))
                    CREATE INDEX [IX_bill_duplicate_checks_company_id_invoice_number_total_amount] ON [bill_duplicate_checks] ([company_id], [invoice_number], [total_amount]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bill_duplicate_checks_company_id_result_status_checked_at' AND object_id = OBJECT_ID(N'[bill_duplicate_checks]'))
                    CREATE INDEX [IX_bill_duplicate_checks_company_id_result_status_checked_at] ON [bill_duplicate_checks] ([company_id], [result_status], [checked_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bill_duplicate_checks_company_id_supplier_name_invoice_number_total_amount' AND object_id = OBJECT_ID(N'[bill_duplicate_checks]'))
                    CREATE INDEX [IX_bill_duplicate_checks_company_id_supplier_name_invoice_number_total_amount] ON [bill_duplicate_checks] ([company_id], [supplier_name], [invoice_number], [total_amount]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_normalized_bill_extractions_company_id_invoice_number_total_amount' AND object_id = OBJECT_ID(N'[normalized_bill_extractions]'))
                    CREATE INDEX [IX_normalized_bill_extractions_company_id_invoice_number_total_amount] ON [normalized_bill_extractions] ([company_id], [invoice_number], [total_amount]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_normalized_bill_extractions_company_id_requires_review_created_at' AND object_id = OBJECT_ID(N'[normalized_bill_extractions]'))
                    CREATE INDEX [IX_normalized_bill_extractions_company_id_requires_review_created_at] ON [normalized_bill_extractions] ([company_id], [requires_review], [created_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_normalized_bill_extractions_company_id_supplier_org_number_invoice_number_total_amount' AND object_id = OBJECT_ID(N'[normalized_bill_extractions]'))
                    CREATE INDEX [IX_normalized_bill_extractions_company_id_supplier_org_number_invoice_number_total_amount] ON [normalized_bill_extractions] ([company_id], [supplier_org_number], [invoice_number], [total_amount]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_normalized_bill_extractions_company_id_validation_status_created_at' AND object_id = OBJECT_ID(N'[normalized_bill_extractions]'))
                    CREATE INDEX [IX_normalized_bill_extractions_company_id_validation_status_created_at] ON [normalized_bill_extractions] ([company_id], [validation_status], [created_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_normalized_bill_extractions_duplicate_check_id' AND object_id = OBJECT_ID(N'[normalized_bill_extractions]'))
                    CREATE INDEX [IX_normalized_bill_extractions_duplicate_check_id] ON [normalized_bill_extractions] ([duplicate_check_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bills_company_id_confidence_level_requires_review_created_at' AND object_id = OBJECT_ID(N'[detected_bills]'))
                    CREATE INDEX [IX_detected_bills_company_id_confidence_level_requires_review_created_at] ON [detected_bills] ([company_id], [confidence_level], [requires_review], [created_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bills_company_id_source_attachment_id' AND object_id = OBJECT_ID(N'[detected_bills]'))
                    CREATE INDEX [IX_detected_bills_company_id_source_attachment_id] ON [detected_bills] ([company_id], [source_attachment_id]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bills_company_id_source_email_id' AND object_id = OBJECT_ID(N'[detected_bills]'))
                    CREATE INDEX [IX_detected_bills_company_id_source_email_id] ON [detected_bills] ([company_id], [source_email_id]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bills_company_id_supplier_name_invoice_number_total_amount' AND object_id = OBJECT_ID(N'[detected_bills]'))
                    CREATE INDEX [IX_detected_bills_company_id_supplier_name_invoice_number_total_amount] ON [detected_bills] ([company_id], [supplier_name], [invoice_number], [total_amount]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bills_company_id_supplier_org_number_invoice_number_total_amount' AND object_id = OBJECT_ID(N'[detected_bills]'))
                    CREATE INDEX [IX_detected_bills_company_id_supplier_org_number_invoice_number_total_amount] ON [detected_bills] ([company_id], [supplier_org_number], [invoice_number], [total_amount]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bills_company_id_validation_status_created_at' AND object_id = OBJECT_ID(N'[detected_bills]'))
                    CREATE INDEX [IX_detected_bills_company_id_validation_status_created_at] ON [detected_bills] ([company_id], [validation_status], [created_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bills_duplicate_check_id' AND object_id = OBJECT_ID(N'[detected_bills]'))
                    CREATE INDEX [IX_detected_bills_duplicate_check_id] ON [detected_bills] ([duplicate_check_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bill_fields_company_id_detected_bill_id_field_name' AND object_id = OBJECT_ID(N'[detected_bill_fields]'))
                    CREATE UNIQUE INDEX [IX_detected_bill_fields_company_id_detected_bill_id_field_name] ON [detected_bill_fields] ([company_id], [detected_bill_id], [field_name]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bill_fields_company_id_field_name' AND object_id = OBJECT_ID(N'[detected_bill_fields]'))
                    CREATE INDEX [IX_detected_bill_fields_company_id_field_name] ON [detected_bill_fields] ([company_id], [field_name]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_detected_bill_fields_detected_bill_id' AND object_id = OBJECT_ID(N'[detected_bill_fields]'))
                    CREATE INDEX [IX_detected_bill_fields_detected_bill_id] ON [detected_bill_fields] ([detected_bill_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_finance_bill_review_states_company_id_detected_bill_id' AND object_id = OBJECT_ID(N'[finance_bill_review_states]'))
                    CREATE UNIQUE INDEX [IX_finance_bill_review_states_company_id_detected_bill_id] ON [finance_bill_review_states] ([company_id], [detected_bill_id]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_finance_bill_review_states_company_id_status_updated_at' AND object_id = OBJECT_ID(N'[finance_bill_review_states]'))
                    CREATE INDEX [IX_finance_bill_review_states_company_id_status_updated_at] ON [finance_bill_review_states] ([company_id], [status], [updated_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_finance_bill_review_states_detected_bill_id' AND object_id = OBJECT_ID(N'[finance_bill_review_states]'))
                    CREATE INDEX [IX_finance_bill_review_states_detected_bill_id] ON [finance_bill_review_states] ([detected_bill_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_finance_bill_review_actions_company_id_detected_bill_id_occurred_at' AND object_id = OBJECT_ID(N'[finance_bill_review_actions]'))
                    CREATE INDEX [IX_finance_bill_review_actions_company_id_detected_bill_id_occurred_at] ON [finance_bill_review_actions] ([company_id], [detected_bill_id], [occurred_at]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_finance_bill_review_actions_detected_bill_id' AND object_id = OBJECT_ID(N'[finance_bill_review_actions]'))
                    CREATE INDEX [IX_finance_bill_review_actions_detected_bill_id] ON [finance_bill_review_actions] ([detected_bill_id]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_finance_bill_review_actions_review_state_id' AND object_id = OBJECT_ID(N'[finance_bill_review_actions]'))
                    CREATE INDEX [IX_finance_bill_review_actions_review_state_id] ON [finance_bill_review_actions] ([review_state_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bill_approval_proposals_company_id_detected_bill_id' AND object_id = OBJECT_ID(N'[bill_approval_proposals]'))
                    CREATE UNIQUE INDEX [IX_bill_approval_proposals_company_id_detected_bill_id] ON [bill_approval_proposals] ([company_id], [detected_bill_id]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bill_approval_proposals_detected_bill_id' AND object_id = OBJECT_ID(N'[bill_approval_proposals]'))
                    CREATE INDEX [IX_bill_approval_proposals_detected_bill_id] ON [bill_approval_proposals] ([detected_bill_id]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_bill_approval_proposals_review_state_id' AND object_id = OBJECT_ID(N'[bill_approval_proposals]'))
                    CREATE INDEX [IX_bill_approval_proposals_review_state_id] ON [bill_approval_proposals] ([review_state_id]);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
