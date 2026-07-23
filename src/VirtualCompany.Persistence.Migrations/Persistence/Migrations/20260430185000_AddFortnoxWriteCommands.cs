using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260430185000_AddFortnoxWriteCommands")]
public partial class AddFortnoxWriteCommands : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[fortnox_write_commands]', N'U') IS NULL
            BEGIN
                CREATE TABLE [fortnox_write_commands] (
                    [id] uniqueidentifier NOT NULL,
                    [company_id] uniqueidentifier NOT NULL,
                    [connection_id] uniqueidentifier NULL,
                    [actor_user_id] uniqueidentifier NULL,
                    [approval_id] uniqueidentifier NULL,
                    [approved_by_user_id] uniqueidentifier NULL,
                    [http_method] nvarchar(16) NOT NULL,
                    [path] nvarchar(512) NOT NULL,
                    [target_company] nvarchar(160) NOT NULL,
                    [entity_type] nvarchar(64) NOT NULL,
                    [payload_summary] nvarchar(1000) NOT NULL,
                    [payload_hash] nvarchar(128) NOT NULL,
                    [sanitized_payload_json] nvarchar(max) NOT NULL,
                    [status] nvarchar(32) NOT NULL,
                    [failure_category] nvarchar(64) NULL,
                    [safe_failure_summary] nvarchar(1000) NULL,
                    [external_id] nvarchar(256) NULL,
                    [correlation_id] nvarchar(128) NULL,
                    [created_at] datetime2 NOT NULL,
                    [updated_at] datetime2 NOT NULL,
                    [approved_at] datetime2 NULL,
                    [execution_started_at] datetime2 NULL,
                    [executed_at] datetime2 NULL,
                    [failed_at] datetime2 NULL,
                    CONSTRAINT [PK_fortnox_write_commands] PRIMARY KEY ([id]),
                    CONSTRAINT [FK_fortnox_write_commands_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                );
            END;

            IF OBJECT_ID(N'[dbo].[finance_integration_connections]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[dbo].[FK_fortnox_write_commands_finance_integration_connections_company_id_connection_id]', N'F') IS NULL
            BEGIN
                ALTER TABLE [fortnox_write_commands]
                ADD CONSTRAINT [FK_fortnox_write_commands_finance_integration_connections_company_id_connection_id]
                FOREIGN KEY ([company_id], [connection_id])
                REFERENCES [finance_integration_connections] ([company_id], [id])
                ON DELETE NO ACTION;
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_fortnox_write_commands_company_id_approval_id'
                  AND object_id = OBJECT_ID(N'[fortnox_write_commands]'))
            BEGIN
                CREATE UNIQUE INDEX [IX_fortnox_write_commands_company_id_approval_id]
                ON [fortnox_write_commands] ([company_id], [approval_id])
                WHERE [approval_id] IS NOT NULL;
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_fortnox_write_commands_company_id_connection_id_created_at'
                  AND object_id = OBJECT_ID(N'[fortnox_write_commands]'))
            BEGIN
                CREATE INDEX [IX_fortnox_write_commands_company_id_connection_id_created_at]
                ON [fortnox_write_commands] ([company_id], [connection_id], [created_at]);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'IX_fortnox_write_commands_company_id_payload_hash_http_method_path_status'
                  AND object_id = OBJECT_ID(N'[fortnox_write_commands]'))
            BEGIN
                CREATE INDEX [IX_fortnox_write_commands_company_id_payload_hash_http_method_path_status]
                ON [fortnox_write_commands] ([company_id], [payload_hash], [http_method], [path], [status]);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "fortnox_write_commands");
    }
}
