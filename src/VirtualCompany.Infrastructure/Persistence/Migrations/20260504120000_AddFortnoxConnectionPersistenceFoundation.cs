using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Migration("20260504120000_AddFortnoxConnectionPersistenceFoundation")]
[DbContext(typeof(VirtualCompanyDbContext))]
public partial class AddFortnoxConnectionPersistenceFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            return;
        }

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_connections]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'token_encryption_key_id') IS NULL
                    ALTER TABLE [fortnox_connections] ADD [token_encryption_key_id] nvarchar(128) NULL;
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'token_encryption_algorithm') IS NULL
                    ALTER TABLE [fortnox_connections] ADD [token_encryption_algorithm] nvarchar(64) NULL;
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'refresh_token_expires_at') IS NULL
                    ALTER TABLE [fortnox_connections] ADD [refresh_token_expires_at] datetime2 NULL;
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'fortnox_company_name') IS NULL
                    ALTER TABLE [fortnox_connections] ADD [fortnox_company_name] nvarchar(256) NULL;
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'last_validated_at') IS NULL
                    ALTER TABLE [fortnox_connections] ADD [last_validated_at] datetime2 NULL;
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'last_sync_at') IS NULL
                    ALTER TABLE [fortnox_connections] ADD [last_sync_at] datetime2 NULL;
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'disconnected_at') IS NULL
                    ALTER TABLE [fortnox_connections] ADD [disconnected_at] datetime2 NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fortnox_connections_status' AND object_id = OBJECT_ID(N'[dbo].[fortnox_connections]'))
                    CREATE INDEX [IX_fortnox_connections_status] ON [fortnox_connections] ([status]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fortnox_connections_access_token_expires_at' AND object_id = OBJECT_ID(N'[dbo].[fortnox_connections]'))
                    CREATE INDEX [IX_fortnox_connections_access_token_expires_at] ON [fortnox_connections] ([access_token_expires_at]);
            END
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_oauth_states]', N'U') IS NULL
            BEGIN
                CREATE TABLE [fortnox_oauth_states] (
                    [id] uniqueidentifier NOT NULL,
                    [company_id] uniqueidentifier NOT NULL,
                    [user_id] uniqueidentifier NOT NULL,
                    [connection_id] uniqueidentifier NULL,
                    [state_hash] nvarchar(128) NOT NULL,
                    [created_at] datetime2 NOT NULL,
                    [expires_at] datetime2 NOT NULL,
                    [consumed_at] datetime2 NULL,
                    [callback_received_at] datetime2 NULL,
                    [redirect_uri] nvarchar(2048) NULL,
                    [code_verifier_ciphertext] nvarchar(max) NULL,
                    [failure_reason] nvarchar(1000) NULL,
                    CONSTRAINT [PK_fortnox_oauth_states] PRIMARY KEY ([id]),
                    CONSTRAINT [FK_fortnox_oauth_states_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_fortnox_oauth_states_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([Id]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_fortnox_oauth_states_fortnox_connections_company_id_connection_id] FOREIGN KEY ([company_id], [connection_id]) REFERENCES [fortnox_connections] ([company_id], [id]) ON DELETE NO ACTION
                );

                CREATE UNIQUE INDEX [IX_fortnox_oauth_states_state_hash] ON [fortnox_oauth_states] ([state_hash]);
                CREATE INDEX [IX_fortnox_oauth_states_company_id_user_id] ON [fortnox_oauth_states] ([company_id], [user_id]);
                CREATE INDEX [IX_fortnox_oauth_states_expires_at] ON [fortnox_oauth_states] ([expires_at]);
                CREATE INDEX [IX_fortnox_oauth_states_consumed_at] ON [fortnox_oauth_states] ([consumed_at]);
                CREATE INDEX [IX_fortnox_oauth_states_user_id] ON [fortnox_oauth_states] ([user_id]);
                CREATE INDEX [IX_fortnox_oauth_states_company_id_connection_id] ON [fortnox_oauth_states] ([company_id], [connection_id]);
            END
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_sync_histories]', N'U') IS NULL
            BEGIN
                CREATE TABLE [fortnox_sync_histories] (
                    [id] uniqueidentifier NOT NULL,
                    [company_id] uniqueidentifier NOT NULL,
                    [fortnox_connection_id] uniqueidentifier NOT NULL,
                    [sync_type] nvarchar(64) NOT NULL,
                    [direction] nvarchar(32) NOT NULL,
                    [status] nvarchar(32) NOT NULL,
                    [started_at] datetime2 NOT NULL,
                    [completed_at] datetime2 NULL,
                    [triggered_by_user_id] uniqueidentifier NULL,
                    [records_processed] int NOT NULL CONSTRAINT [DF_fortnox_sync_histories_records_processed] DEFAULT 0,
                    [records_succeeded] int NOT NULL CONSTRAINT [DF_fortnox_sync_histories_records_succeeded] DEFAULT 0,
                    [records_failed] int NOT NULL CONSTRAINT [DF_fortnox_sync_histories_records_failed] DEFAULT 0,
                    [correlation_id] nvarchar(128) NULL,
                    [error_summary] nvarchar(1000) NULL,
                    [metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_fortnox_sync_histories_metadata_json] DEFAULT N'{}',
                    CONSTRAINT [PK_fortnox_sync_histories] PRIMARY KEY ([id]),
                    CONSTRAINT [CK_fortnox_sync_histories_direction] CHECK ([direction] IN ('import', 'export', 'bidirectional')),
                    CONSTRAINT [CK_fortnox_sync_histories_status] CHECK ([status] IN ('running', 'succeeded', 'failed')),
                    CONSTRAINT [CK_fortnox_sync_histories_records_processed_nonnegative] CHECK ([records_processed] >= 0),
                    CONSTRAINT [CK_fortnox_sync_histories_records_succeeded_nonnegative] CHECK ([records_succeeded] >= 0),
                    CONSTRAINT [CK_fortnox_sync_histories_records_failed_nonnegative] CHECK ([records_failed] >= 0),
                    CONSTRAINT [FK_fortnox_sync_histories_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_fortnox_sync_histories_fortnox_connections_company_id_fortnox_connection_id] FOREIGN KEY ([company_id], [fortnox_connection_id]) REFERENCES [fortnox_connections] ([company_id], [id]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_fortnox_sync_histories_users_triggered_by_user_id] FOREIGN KEY ([triggered_by_user_id]) REFERENCES [users] ([Id]) ON DELETE SET NULL
                );

                CREATE INDEX [IX_fortnox_sync_histories_company_id_fortnox_connection_id_started_at] ON [fortnox_sync_histories] ([company_id], [fortnox_connection_id], [started_at]);
                CREATE INDEX [IX_fortnox_sync_histories_status] ON [fortnox_sync_histories] ([status]);
                CREATE INDEX [IX_fortnox_sync_histories_company_id_correlation_id] ON [fortnox_sync_histories] ([company_id], [correlation_id]);
                CREATE INDEX [IX_fortnox_sync_histories_triggered_by_user_id] ON [fortnox_sync_histories] ([triggered_by_user_id]);
            END
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_external_references]', N'U') IS NULL
            BEGIN
                CREATE TABLE [fortnox_external_references] (
                    [id] uniqueidentifier NOT NULL,
                    [company_id] uniqueidentifier NOT NULL,
                    [fortnox_connection_id] uniqueidentifier NULL,
                    [entity_type] nvarchar(64) NOT NULL,
                    [internal_entity_id] uniqueidentifier NOT NULL,
                    [external_entity_type] nvarchar(64) NOT NULL,
                    [external_id] nvarchar(256) NOT NULL,
                    [external_display_reference] nvarchar(128) NULL,
                    [last_synced_at] datetime2 NULL,
                    [created_at] datetime2 NOT NULL,
                    [updated_at] datetime2 NOT NULL,
                    CONSTRAINT [PK_fortnox_external_references] PRIMARY KEY ([id]),
                    CONSTRAINT [FK_fortnox_external_references_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_fortnox_external_references_fortnox_connections_company_id_fortnox_connection_id] FOREIGN KEY ([company_id], [fortnox_connection_id]) REFERENCES [fortnox_connections] ([company_id], [id]) ON DELETE NO ACTION
                );

                CREATE UNIQUE INDEX [IX_fortnox_external_references_company_id_entity_type_internal_entity_id_external_entity_type] ON [fortnox_external_references] ([company_id], [entity_type], [internal_entity_id], [external_entity_type]);
                CREATE INDEX [IX_fortnox_external_references_company_id_external_entity_type_external_id] ON [fortnox_external_references] ([company_id], [external_entity_type], [external_id]);
                CREATE INDEX [IX_fortnox_external_references_company_id_fortnox_connection_id] ON [fortnox_external_references] ([company_id], [fortnox_connection_id]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            return;
        }

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_external_references]', N'U') IS NOT NULL
                DROP TABLE [fortnox_external_references];
            IF OBJECT_ID(N'[dbo].[fortnox_sync_histories]', N'U') IS NOT NULL
                DROP TABLE [fortnox_sync_histories];
            IF OBJECT_ID(N'[dbo].[fortnox_oauth_states]', N'U') IS NOT NULL
                DROP TABLE [fortnox_oauth_states];

            IF OBJECT_ID(N'[dbo].[fortnox_connections]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fortnox_connections_access_token_expires_at' AND object_id = OBJECT_ID(N'[dbo].[fortnox_connections]'))
                    DROP INDEX [IX_fortnox_connections_access_token_expires_at] ON [fortnox_connections];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fortnox_connections_status' AND object_id = OBJECT_ID(N'[dbo].[fortnox_connections]'))
                    DROP INDEX [IX_fortnox_connections_status] ON [fortnox_connections];
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'disconnected_at') IS NOT NULL
                    ALTER TABLE [fortnox_connections] DROP COLUMN [disconnected_at];
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'last_sync_at') IS NOT NULL
                    ALTER TABLE [fortnox_connections] DROP COLUMN [last_sync_at];
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'last_validated_at') IS NOT NULL
                    ALTER TABLE [fortnox_connections] DROP COLUMN [last_validated_at];
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'fortnox_company_name') IS NOT NULL
                    ALTER TABLE [fortnox_connections] DROP COLUMN [fortnox_company_name];
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'refresh_token_expires_at') IS NOT NULL
                    ALTER TABLE [fortnox_connections] DROP COLUMN [refresh_token_expires_at];
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'token_encryption_algorithm') IS NOT NULL
                    ALTER TABLE [fortnox_connections] DROP COLUMN [token_encryption_algorithm];
                IF COL_LENGTH(N'[dbo].[fortnox_connections]', N'token_encryption_key_id') IS NOT NULL
                    ALTER TABLE [fortnox_connections] DROP COLUMN [token_encryption_key_id];
            END
            """);
    }
}
