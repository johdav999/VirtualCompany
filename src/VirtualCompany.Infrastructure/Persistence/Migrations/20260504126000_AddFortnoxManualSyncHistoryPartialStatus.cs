using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260504126000_AddFortnoxManualSyncHistoryPartialStatus")]
public partial class AddFortnoxManualSyncHistoryPartialStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_sync_histories]', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'[dbo].[CK_fortnox_sync_histories_status]', N'C') IS NOT NULL
                    ALTER TABLE [fortnox_sync_histories] DROP CONSTRAINT [CK_fortnox_sync_histories_status];

                ALTER TABLE [fortnox_sync_histories]
                    ADD CONSTRAINT [CK_fortnox_sync_histories_status]
                    CHECK ([status] IN ('running', 'succeeded', 'failed', 'partial'));
            END
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[finance_integration_sync_states]', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'[dbo].[CK_finance_integration_sync_states_status]', N'C') IS NOT NULL
                    ALTER TABLE [finance_integration_sync_states] DROP CONSTRAINT [CK_finance_integration_sync_states_status];

                ALTER TABLE [finance_integration_sync_states]
                    ADD CONSTRAINT [CK_finance_integration_sync_states_status]
                    CHECK ([status] IN ('pending', 'running', 'succeeded', 'failed', 'partial'));
            END
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_external_references]', N'U') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_fortnox_external_references_company_id_external_entity_type_external_id'
                      AND object_id = OBJECT_ID(N'[dbo].[fortnox_external_references]'))
            BEGIN
                CREATE UNIQUE INDEX [IX_fortnox_external_references_company_id_external_entity_type_external_id]
                    ON [fortnox_external_references] ([company_id], [external_entity_type], [external_id]);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_sync_histories]', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'[dbo].[CK_fortnox_sync_histories_status]', N'C') IS NOT NULL
                    ALTER TABLE [fortnox_sync_histories] DROP CONSTRAINT [CK_fortnox_sync_histories_status];

                ALTER TABLE [fortnox_sync_histories]
                    ADD CONSTRAINT [CK_fortnox_sync_histories_status]
                    CHECK ([status] IN ('running', 'succeeded', 'failed'));
            END
            """);

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[finance_integration_sync_states]', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'[dbo].[CK_finance_integration_sync_states_status]', N'C') IS NOT NULL
                    ALTER TABLE [finance_integration_sync_states] DROP CONSTRAINT [CK_finance_integration_sync_states_status];

                ALTER TABLE [finance_integration_sync_states]
                    ADD CONSTRAINT [CK_finance_integration_sync_states_status]
                    CHECK ([status] IN ('pending', 'running', 'succeeded', 'failed'));
            END
            """);

        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_fortnox_external_references_company_id_external_entity_type_external_id' AND object_id = OBJECT_ID(N'[dbo].[fortnox_external_references]')) DROP INDEX [IX_fortnox_external_references_company_id_external_entity_type_external_id] ON [fortnox_external_references];");
    }
}
