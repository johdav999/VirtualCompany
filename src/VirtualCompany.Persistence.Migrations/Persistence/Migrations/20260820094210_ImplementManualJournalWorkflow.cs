using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260820094210_ImplementManualJournalWorkflow")]
public partial class ImplementManualJournalWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE [ledger_entry_evidence_links] (
    [id] uniqueidentifier NOT NULL,
    [company_id] uniqueidentifier NOT NULL,
    [ledger_entry_id] uniqueidentifier NOT NULL,
    [document_id] uniqueidentifier NOT NULL,
    [content_hash] nvarchar(64) NOT NULL,
    [title] nvarchar(200) NOT NULL,
    [created_utc] datetime2 NOT NULL,
    CONSTRAINT [PK_ledger_entry_evidence_links] PRIMARY KEY ([id]),
    CONSTRAINT [FK_ledger_entry_evidence_links_knowledge_documents_company_id_document_id] FOREIGN KEY ([company_id], [document_id]) REFERENCES [knowledge_documents] ([CompanyId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ledger_entry_evidence_links_ledger_entries_company_id_ledger_entry_id] FOREIGN KEY ([company_id], [ledger_entry_id]) REFERENCES [ledger_entries] ([company_id], [id]) ON DELETE NO ACTION
);

CREATE TABLE [manual_journal_drafts] (
    [id] uniqueidentifier NOT NULL,
    [company_id] uniqueidentifier NOT NULL,
    [fiscal_period_id] uniqueidentifier NOT NULL,
    [voucher_series_code] nvarchar(32) NOT NULL,
    [document_date] date NOT NULL,
    [posting_date] date NOT NULL,
    [explanation] nvarchar(1000) NOT NULL,
    [currency] nvarchar(3) NOT NULL,
    [status] nvarchar(32) NOT NULL,
    [version] bigint NOT NULL,
    [payload_hash] nvarchar(64) NOT NULL,
    [created_by_user_id] uniqueidentifier NOT NULL,
    [updated_by_user_id] uniqueidentifier NOT NULL,
    [approval_request_id] uniqueidentifier NULL,
    [ledger_entry_id] uniqueidentifier NULL,
    [original_ledger_entry_id] uniqueidentifier NULL,
    [correction_reason] nvarchar(1000) NULL,
    [created_utc] datetime2 NOT NULL,
    [updated_utc] datetime2 NOT NULL,
    [posted_utc] datetime2 NULL,
    [discarded_utc] datetime2 NULL,
    CONSTRAINT [PK_manual_journal_drafts] PRIMARY KEY ([id]),
    CONSTRAINT [AK_manual_journal_drafts_company_id_id] UNIQUE ([company_id], [id]),
    CONSTRAINT [FK_manual_journal_drafts_approval_requests_company_id_approval_request_id] FOREIGN KEY ([company_id], [approval_request_id]) REFERENCES [approval_requests] ([CompanyId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_manual_journal_drafts_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_manual_journal_drafts_finance_fiscal_periods_company_id_fiscal_period_id] FOREIGN KEY ([company_id], [fiscal_period_id]) REFERENCES [finance_fiscal_periods] ([company_id], [id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_manual_journal_drafts_ledger_entries_company_id_ledger_entry_id] FOREIGN KEY ([company_id], [ledger_entry_id]) REFERENCES [ledger_entries] ([company_id], [id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_manual_journal_drafts_ledger_entries_company_id_original_ledger_entry_id] FOREIGN KEY ([company_id], [original_ledger_entry_id]) REFERENCES [ledger_entries] ([company_id], [id]) ON DELETE NO ACTION
);

CREATE TABLE [manual_journal_draft_lines] (
    [id] uniqueidentifier NOT NULL,
    [company_id] uniqueidentifier NOT NULL,
    [draft_id] uniqueidentifier NOT NULL,
    [finance_account_id] uniqueidentifier NOT NULL,
    [line_number] int NOT NULL,
    [debit_amount] decimal(19,6) NOT NULL,
    [credit_amount] decimal(19,6) NOT NULL,
    [currency] nvarchar(3) NOT NULL,
    [description] nvarchar(500) NULL,
    [cost_center_id] uniqueidentifier NULL,
    [tax_facts_json] nvarchar(max) NULL,
    [dimension_facts_json] nvarchar(max) NULL,
    CONSTRAINT [PK_manual_journal_draft_lines] PRIMARY KEY ([id]),
    CONSTRAINT [FK_manual_journal_draft_lines_finance_accounts_company_id_finance_account_id] FOREIGN KEY ([company_id], [finance_account_id]) REFERENCES [finance_accounts] ([company_id], [id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_manual_journal_draft_lines_manual_journal_drafts_company_id_draft_id] FOREIGN KEY ([company_id], [draft_id]) REFERENCES [manual_journal_drafts] ([company_id], [id]) ON DELETE CASCADE
);

CREATE TABLE [manual_journal_evidence_links] (
    [id] uniqueidentifier NOT NULL,
    [company_id] uniqueidentifier NOT NULL,
    [draft_id] uniqueidentifier NOT NULL,
    [document_id] uniqueidentifier NOT NULL,
    [content_hash] nvarchar(64) NOT NULL,
    [title] nvarchar(200) NOT NULL,
    [created_utc] datetime2 NOT NULL,
    CONSTRAINT [PK_manual_journal_evidence_links] PRIMARY KEY ([id]),
    CONSTRAINT [FK_manual_journal_evidence_links_knowledge_documents_company_id_document_id] FOREIGN KEY ([company_id], [document_id]) REFERENCES [knowledge_documents] ([CompanyId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_manual_journal_evidence_links_manual_journal_drafts_company_id_draft_id] FOREIGN KEY ([company_id], [draft_id]) REFERENCES [manual_journal_drafts] ([company_id], [id]) ON DELETE CASCADE
);

CREATE TABLE [manual_journal_operations] (
    [id] uniqueidentifier NOT NULL,
    [company_id] uniqueidentifier NOT NULL,
    [draft_id] uniqueidentifier NOT NULL,
    [action] nvarchar(64) NOT NULL,
    [idempotency_key] nvarchar(200) NOT NULL,
    [payload_hash] nvarchar(64) NOT NULL,
    [result_version] bigint NOT NULL,
    [approval_request_id] uniqueidentifier NULL,
    [ledger_entry_id] uniqueidentifier NULL,
    [created_utc] datetime2 NOT NULL,
    CONSTRAINT [PK_manual_journal_operations] PRIMARY KEY ([id]),
    CONSTRAINT [FK_manual_journal_operations_manual_journal_drafts_company_id_draft_id] FOREIGN KEY ([company_id], [draft_id]) REFERENCES [manual_journal_drafts] ([company_id], [id]) ON DELETE CASCADE
);

CREATE INDEX [IX_ledger_entry_evidence_links_company_id_document_id] ON [ledger_entry_evidence_links] ([company_id], [document_id]);
CREATE UNIQUE INDEX [IX_ledger_entry_evidence_links_company_id_ledger_entry_id_document_id] ON [ledger_entry_evidence_links] ([company_id], [ledger_entry_id], [document_id]);
CREATE UNIQUE INDEX [IX_manual_journal_draft_lines_company_id_draft_id_line_number] ON [manual_journal_draft_lines] ([company_id], [draft_id], [line_number]);
CREATE INDEX [IX_manual_journal_draft_lines_company_id_finance_account_id] ON [manual_journal_draft_lines] ([company_id], [finance_account_id]);
CREATE UNIQUE INDEX [IX_manual_journal_drafts_company_id_approval_request_id] ON [manual_journal_drafts] ([company_id], [approval_request_id]) WHERE approval_request_id IS NOT NULL;
CREATE INDEX [IX_manual_journal_drafts_company_id_fiscal_period_id] ON [manual_journal_drafts] ([company_id], [fiscal_period_id]);
CREATE UNIQUE INDEX [IX_manual_journal_drafts_company_id_ledger_entry_id] ON [manual_journal_drafts] ([company_id], [ledger_entry_id]) WHERE ledger_entry_id IS NOT NULL;
CREATE INDEX [IX_manual_journal_drafts_company_id_original_ledger_entry_id] ON [manual_journal_drafts] ([company_id], [original_ledger_entry_id]);
CREATE INDEX [IX_manual_journal_drafts_company_id_status_updated_utc] ON [manual_journal_drafts] ([company_id], [status], [updated_utc]);
CREATE INDEX [IX_manual_journal_evidence_links_company_id_document_id] ON [manual_journal_evidence_links] ([company_id], [document_id]);
CREATE UNIQUE INDEX [IX_manual_journal_evidence_links_company_id_draft_id_document_id] ON [manual_journal_evidence_links] ([company_id], [draft_id], [document_id]);
CREATE INDEX [IX_manual_journal_operations_company_id_draft_id_action_result_version] ON [manual_journal_operations] ([company_id], [draft_id], [action], [result_version]);
CREATE UNIQUE INDEX [IX_manual_journal_operations_company_id_idempotency_key] ON [manual_journal_operations] ([company_id], [idempotency_key]);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ledger_entry_evidence_links");
        migrationBuilder.DropTable(name: "manual_journal_draft_lines");
        migrationBuilder.DropTable(name: "manual_journal_evidence_links");
        migrationBuilder.DropTable(name: "manual_journal_operations");
        migrationBuilder.DropTable(name: "manual_journal_drafts");
    }
}
