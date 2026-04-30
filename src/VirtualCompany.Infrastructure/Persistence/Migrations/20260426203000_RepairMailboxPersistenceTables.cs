using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Migration("20260426203000_RepairMailboxPersistenceTables")]
    [DbContext(typeof(VirtualCompanyDbContext))]
    public partial class RepairMailboxPersistenceTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                return;
            }

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[mailbox_connections]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [mailbox_connections] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [user_id] uniqueidentifier NOT NULL,
                        [provider] nvarchar(32) NOT NULL,
                        [status] nvarchar(32) NOT NULL,
                        [email_address] nvarchar(256) NOT NULL,
                        [display_name] nvarchar(200) NULL,
                        [mailbox_external_id] nvarchar(256) NULL,
                        [encrypted_access_token] nvarchar(max) NULL,
                        [encrypted_refresh_token] nvarchar(max) NULL,
                        [encrypted_credential_envelope] nvarchar(max) NULL,
                        [access_token_expires_at] datetime2 NULL,
                        [granted_scopes_json] nvarchar(max) NOT NULL CONSTRAINT [DF_mailbox_connections_granted_scopes_json] DEFAULT N'[]',
                        [configured_folders_json] nvarchar(max) NOT NULL CONSTRAINT [DF_mailbox_connections_configured_folders_json] DEFAULT N'[]',
                        [provider_metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_mailbox_connections_provider_metadata_json] DEFAULT N'{}',
                        [last_successful_scan_at] datetime2 NULL,
                        [last_error_summary] nvarchar(1000) NULL,
                        [created_at] datetime2 NOT NULL,
                        [updated_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_mailbox_connections] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_mailbox_connections_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_mailbox_connections_provider] CHECK ([provider] IN ('gmail', 'microsoft365')),
                        CONSTRAINT [CK_mailbox_connections_status] CHECK ([status] IN ('pending', 'active', 'token_expired', 'revoked', 'failed', 'disconnected')),
                        CONSTRAINT [FK_mailbox_connections_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_mailbox_connections_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([Id]) ON DELETE NO ACTION
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[email_ingestion_runs]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [email_ingestion_runs] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [mailbox_connection_id] uniqueidentifier NOT NULL,
                        [triggered_by_user_id] uniqueidentifier NOT NULL,
                        [provider] nvarchar(32) NOT NULL,
                        [started_at] datetime2 NOT NULL,
                        [completed_at] datetime2 NULL,
                        [scan_from_at] datetime2 NULL,
                        [scan_to_at] datetime2 NULL,
                        [scanned_message_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_scanned_message_count] DEFAULT 0,
                        [detected_candidate_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_detected_candidate_count] DEFAULT 0,
                        [non_candidate_message_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_non_candidate_message_count] DEFAULT 0,
                        [candidate_attachment_snapshot_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_candidate_attachment_snapshot_count] DEFAULT 0,
                        [deduplicated_attachment_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_deduplicated_attachment_count] DEFAULT 0,
                        [failure_details] nvarchar(4000) NULL,
                        [created_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_email_ingestion_runs] PRIMARY KEY ([id]),
                        CONSTRAINT [CK_email_ingestion_runs_provider] CHECK ([provider] IN ('gmail', 'microsoft365')),
                        CONSTRAINT [CK_email_ingestion_runs_scanned_message_count_nonnegative] CHECK ([scanned_message_count] >= 0),
                        CONSTRAINT [CK_email_ingestion_runs_detected_candidate_count_nonnegative] CHECK ([detected_candidate_count] >= 0),
                        CONSTRAINT [CK_email_ingestion_runs_non_candidate_message_count_nonnegative] CHECK ([non_candidate_message_count] >= 0),
                        CONSTRAINT [CK_email_ingestion_runs_candidate_attachment_snapshot_count_nonnegative] CHECK ([candidate_attachment_snapshot_count] >= 0),
                        CONSTRAINT [CK_email_ingestion_runs_deduplicated_attachment_count_nonnegative] CHECK ([deduplicated_attachment_count] >= 0),
                        CONSTRAINT [FK_email_ingestion_runs_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_email_ingestion_runs_mailbox_connections_mailbox_connection_id] FOREIGN KEY ([mailbox_connection_id]) REFERENCES [mailbox_connections] ([id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_email_ingestion_runs_users_triggered_by_user_id] FOREIGN KEY ([triggered_by_user_id]) REFERENCES [users] ([Id]) ON DELETE NO ACTION
                    );
                END

                IF COL_LENGTH(N'email_ingestion_runs', N'non_candidate_message_count') IS NULL
                    ALTER TABLE [email_ingestion_runs] ADD [non_candidate_message_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_non_candidate_message_count] DEFAULT 0;

                IF COL_LENGTH(N'email_ingestion_runs', N'candidate_attachment_snapshot_count') IS NULL
                    ALTER TABLE [email_ingestion_runs] ADD [candidate_attachment_snapshot_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_candidate_attachment_snapshot_count] DEFAULT 0;

                IF COL_LENGTH(N'email_ingestion_runs', N'deduplicated_attachment_count') IS NULL
                    ALTER TABLE [email_ingestion_runs] ADD [deduplicated_attachment_count] int NOT NULL CONSTRAINT [DF_email_ingestion_runs_deduplicated_attachment_count] DEFAULT 0;
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[email_message_snapshots]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [email_message_snapshots] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [mailbox_connection_id] uniqueidentifier NOT NULL,
                        [email_ingestion_run_id] uniqueidentifier NOT NULL,
                        [external_message_id] nvarchar(512) NOT NULL,
                        [from_address] nvarchar(320) NULL,
                        [from_display_name] nvarchar(256) NULL,
                        [subject] nvarchar(500) NULL,
                        [sender_domain] nvarchar(256) NULL,
                        [received_at] datetime2 NULL,
                        [folder_id] nvarchar(256) NULL,
                        [folder_display_name] nvarchar(256) NULL,
                        [body_reference] nvarchar(512) NULL,
                        [untrusted_body_text] nvarchar(max) NULL,
                        [source_type] nvarchar(32) NOT NULL,
                        [candidate_decision] nvarchar(32) NOT NULL CONSTRAINT [DF_email_message_snapshots_candidate_decision] DEFAULT N'candidate',
                        [matched_rules_json] nvarchar(max) NOT NULL CONSTRAINT [DF_email_message_snapshots_matched_rules_json] DEFAULT N'[]',
                        [reason_summary] nvarchar(1000) NOT NULL,
                        [created_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_email_message_snapshots] PRIMARY KEY ([id]),
                        CONSTRAINT [AK_email_message_snapshots_company_id_id] UNIQUE ([company_id], [id]),
                        CONSTRAINT [CK_email_message_snapshots_candidate_decision] CHECK ([candidate_decision] IN ('candidate', 'not_candidate')),
                        CONSTRAINT [CK_email_message_snapshots_source_type] CHECK ([source_type] IN ('pdf_attachment', 'docx_attachment', 'email_body_only')),
                        CONSTRAINT [FK_email_message_snapshots_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_email_message_snapshots_email_ingestion_runs_email_ingestion_run_id] FOREIGN KEY ([email_ingestion_run_id]) REFERENCES [email_ingestion_runs] ([id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_email_message_snapshots_mailbox_connections_mailbox_connection_id] FOREIGN KEY ([mailbox_connection_id]) REFERENCES [mailbox_connections] ([id]) ON DELETE NO ACTION
                    );
                END

                IF COL_LENGTH(N'email_message_snapshots', N'candidate_decision') IS NULL
                    ALTER TABLE [email_message_snapshots] ADD [candidate_decision] nvarchar(32) NOT NULL CONSTRAINT [DF_email_message_snapshots_candidate_decision] DEFAULT N'candidate';

                IF COL_LENGTH(N'email_message_snapshots', N'sender_domain') IS NULL
                    ALTER TABLE [email_message_snapshots] ADD [sender_domain] nvarchar(256) NULL;
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[email_attachment_snapshots]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [email_attachment_snapshots] (
                        [id] uniqueidentifier NOT NULL,
                        [company_id] uniqueidentifier NOT NULL,
                        [email_message_snapshot_id] uniqueidentifier NOT NULL,
                        [external_attachment_id] nvarchar(512) NOT NULL,
                        [file_name] nvarchar(512) NULL,
                        [mime_type] nvarchar(256) NULL,
                        [size_bytes] bigint NULL,
                        [content_hash] nvarchar(128) NOT NULL,
                        [storage_reference] nvarchar(512) NULL,
                        [source_type] nvarchar(32) NOT NULL,
                        [untrusted_extracted_text] nvarchar(max) NULL,
                        [is_duplicate_by_hash] bit NOT NULL CONSTRAINT [DF_email_attachment_snapshots_is_duplicate_by_hash] DEFAULT 0,
                        [canonical_attachment_snapshot_id] uniqueidentifier NULL,
                        [created_at] datetime2 NOT NULL,
                        CONSTRAINT [PK_email_attachment_snapshots] PRIMARY KEY ([id]),
                        CONSTRAINT [CK_email_attachment_snapshots_size_nonnegative] CHECK ([size_bytes] IS NULL OR [size_bytes] >= 0),
                        CONSTRAINT [CK_email_attachment_snapshots_source_type] CHECK ([source_type] IN ('pdf_attachment', 'docx_attachment', 'email_body_only')),
                        CONSTRAINT [FK_email_attachment_snapshots_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_email_attachment_snapshots_email_message_snapshots_email_message_snapshot_id] FOREIGN KEY ([email_message_snapshot_id]) REFERENCES [email_message_snapshots] ([id]) ON DELETE CASCADE
                    );
                END

                IF COL_LENGTH(N'email_attachment_snapshots', N'is_duplicate_by_hash') IS NULL
                    ALTER TABLE [email_attachment_snapshots] ADD [is_duplicate_by_hash] bit NOT NULL CONSTRAINT [DF_email_attachment_snapshots_is_duplicate_by_hash] DEFAULT 0;

                IF COL_LENGTH(N'email_attachment_snapshots', N'canonical_attachment_snapshot_id') IS NULL
                    ALTER TABLE [email_attachment_snapshots] ADD [canonical_attachment_snapshot_id] uniqueidentifier NULL;
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mailbox_connections_company_id' AND object_id = OBJECT_ID(N'[mailbox_connections]'))
                    CREATE INDEX [IX_mailbox_connections_company_id] ON [mailbox_connections] ([company_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mailbox_connections_company_id_user_id' AND object_id = OBJECT_ID(N'[mailbox_connections]'))
                    CREATE INDEX [IX_mailbox_connections_company_id_user_id] ON [mailbox_connections] ([company_id], [user_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mailbox_connections_company_id_provider_email_address' AND object_id = OBJECT_ID(N'[mailbox_connections]'))
                    CREATE INDEX [IX_mailbox_connections_company_id_provider_email_address] ON [mailbox_connections] ([company_id], [provider], [email_address]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mailbox_connections_company_id_status' AND object_id = OBJECT_ID(N'[mailbox_connections]'))
                    CREATE INDEX [IX_mailbox_connections_company_id_status] ON [mailbox_connections] ([company_id], [status]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_mailbox_connections_user_id' AND object_id = OBJECT_ID(N'[mailbox_connections]'))
                    CREATE INDEX [IX_mailbox_connections_user_id] ON [mailbox_connections] ([user_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_ingestion_runs_company_id' AND object_id = OBJECT_ID(N'[email_ingestion_runs]'))
                    CREATE INDEX [IX_email_ingestion_runs_company_id] ON [email_ingestion_runs] ([company_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_ingestion_runs_company_id_mailbox_connection_id_started_at' AND object_id = OBJECT_ID(N'[email_ingestion_runs]'))
                    CREATE INDEX [IX_email_ingestion_runs_company_id_mailbox_connection_id_started_at] ON [email_ingestion_runs] ([company_id], [mailbox_connection_id], [started_at]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_ingestion_runs_company_id_provider_started_at' AND object_id = OBJECT_ID(N'[email_ingestion_runs]'))
                    CREATE INDEX [IX_email_ingestion_runs_company_id_provider_started_at] ON [email_ingestion_runs] ([company_id], [provider], [started_at]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_ingestion_runs_company_id_triggered_by_user_id_started_at' AND object_id = OBJECT_ID(N'[email_ingestion_runs]'))
                    CREATE INDEX [IX_email_ingestion_runs_company_id_triggered_by_user_id_started_at] ON [email_ingestion_runs] ([company_id], [triggered_by_user_id], [started_at]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_ingestion_runs_mailbox_connection_id' AND object_id = OBJECT_ID(N'[email_ingestion_runs]'))
                    CREATE INDEX [IX_email_ingestion_runs_mailbox_connection_id] ON [email_ingestion_runs] ([mailbox_connection_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_ingestion_runs_triggered_by_user_id' AND object_id = OBJECT_ID(N'[email_ingestion_runs]'))
                    CREATE INDEX [IX_email_ingestion_runs_triggered_by_user_id] ON [email_ingestion_runs] ([triggered_by_user_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_message_snapshots_company_id' AND object_id = OBJECT_ID(N'[email_message_snapshots]'))
                    CREATE INDEX [IX_email_message_snapshots_company_id] ON [email_message_snapshots] ([company_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_message_snapshots_company_id_email_ingestion_run_id' AND object_id = OBJECT_ID(N'[email_message_snapshots]'))
                    CREATE INDEX [IX_email_message_snapshots_company_id_email_ingestion_run_id] ON [email_message_snapshots] ([company_id], [email_ingestion_run_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_message_snapshots_company_id_mailbox_connection_id_external_message_id' AND object_id = OBJECT_ID(N'[email_message_snapshots]'))
                    CREATE UNIQUE INDEX [IX_email_message_snapshots_company_id_mailbox_connection_id_external_message_id] ON [email_message_snapshots] ([company_id], [mailbox_connection_id], [external_message_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_message_snapshots_company_id_source_type_created_at' AND object_id = OBJECT_ID(N'[email_message_snapshots]'))
                    CREATE INDEX [IX_email_message_snapshots_company_id_source_type_created_at] ON [email_message_snapshots] ([company_id], [source_type], [created_at]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_message_snapshots_company_id_sender_domain' AND object_id = OBJECT_ID(N'[email_message_snapshots]'))
                    CREATE INDEX [IX_email_message_snapshots_company_id_sender_domain] ON [email_message_snapshots] ([company_id], [sender_domain]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_message_snapshots_email_ingestion_run_id' AND object_id = OBJECT_ID(N'[email_message_snapshots]'))
                    CREATE INDEX [IX_email_message_snapshots_email_ingestion_run_id] ON [email_message_snapshots] ([email_ingestion_run_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_message_snapshots_mailbox_connection_id' AND object_id = OBJECT_ID(N'[email_message_snapshots]'))
                    CREATE INDEX [IX_email_message_snapshots_mailbox_connection_id] ON [email_message_snapshots] ([mailbox_connection_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_attachment_snapshots_company_id' AND object_id = OBJECT_ID(N'[email_attachment_snapshots]'))
                    CREATE INDEX [IX_email_attachment_snapshots_company_id] ON [email_attachment_snapshots] ([company_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_attachment_snapshots_company_id_content_hash' AND object_id = OBJECT_ID(N'[email_attachment_snapshots]'))
                    CREATE INDEX [IX_email_attachment_snapshots_company_id_content_hash] ON [email_attachment_snapshots] ([company_id], [content_hash]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_attachment_snapshots_company_id_content_hash_is_duplicate_by_hash' AND object_id = OBJECT_ID(N'[email_attachment_snapshots]'))
                    CREATE INDEX [IX_email_attachment_snapshots_company_id_content_hash_is_duplicate_by_hash] ON [email_attachment_snapshots] ([company_id], [content_hash], [is_duplicate_by_hash]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_attachment_snapshots_company_id_email_message_snapshot_id' AND object_id = OBJECT_ID(N'[email_attachment_snapshots]'))
                    CREATE INDEX [IX_email_attachment_snapshots_company_id_email_message_snapshot_id] ON [email_attachment_snapshots] ([company_id], [email_message_snapshot_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_attachment_snapshots_company_id_email_message_snapshot_id_external_attachment_id' AND object_id = OBJECT_ID(N'[email_attachment_snapshots]'))
                    CREATE UNIQUE INDEX [IX_email_attachment_snapshots_company_id_email_message_snapshot_id_external_attachment_id] ON [email_attachment_snapshots] ([company_id], [email_message_snapshot_id], [external_attachment_id]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_email_attachment_snapshots_email_message_snapshot_id' AND object_id = OBJECT_ID(N'[email_attachment_snapshots]'))
                    CREATE INDEX [IX_email_attachment_snapshots_email_message_snapshot_id] ON [email_attachment_snapshots] ([email_message_snapshot_id]);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
