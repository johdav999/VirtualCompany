using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Migration("20260501103000_AddFortnoxConnections")]
[DbContext(typeof(VirtualCompanyDbContext))]
public partial class AddFortnoxConnections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            return;
        }

        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[fortnox_connections]', N'U') IS NULL
            BEGIN
                CREATE TABLE [fortnox_connections] (
                    [id] uniqueidentifier NOT NULL,
                    [company_id] uniqueidentifier NOT NULL,
                    [connected_by_user_id] uniqueidentifier NOT NULL,
                    [status] nvarchar(32) NOT NULL,
                    [encrypted_access_token] nvarchar(max) NULL,
                    [encrypted_refresh_token] nvarchar(max) NULL,
                    [access_token_expires_at] datetime2 NULL,
                    [granted_scopes_json] nvarchar(max) NOT NULL CONSTRAINT [DF_fortnox_connections_granted_scopes_json] DEFAULT N'[]',
                    [provider_tenant_id] nvarchar(256) NULL,
                    [provider_metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_fortnox_connections_provider_metadata_json] DEFAULT N'{}',
                    [connected_at] datetime2 NULL,
                    [last_refresh_attempt_at] datetime2 NULL,
                    [last_successful_refresh_at] datetime2 NULL,
                    [last_error_summary] nvarchar(1000) NULL,
                    [created_at] datetime2 NOT NULL,
                    [updated_at] datetime2 NOT NULL,
                    CONSTRAINT [PK_fortnox_connections] PRIMARY KEY ([id]),
                    CONSTRAINT [AK_fortnox_connections_company_id_id] UNIQUE ([company_id], [id]),
                    CONSTRAINT [CK_fortnox_connections_status] CHECK ([status] IN ('pending', 'connected', 'needs_reconnect', 'revoked', 'error', 'disconnected')),
                    CONSTRAINT [FK_fortnox_connections_companies_company_id] FOREIGN KEY ([company_id]) REFERENCES [companies] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_fortnox_connections_users_connected_by_user_id] FOREIGN KEY ([connected_by_user_id]) REFERENCES [users] ([Id]) ON DELETE NO ACTION
                );

                CREATE UNIQUE INDEX [IX_fortnox_connections_company_id] ON [fortnox_connections] ([company_id]);
                CREATE INDEX [IX_fortnox_connections_company_id_status] ON [fortnox_connections] ([company_id], [status]);
                CREATE INDEX [IX_fortnox_connections_connected_by_user_id] ON [fortnox_connections] ([connected_by_user_id]);
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
            IF OBJECT_ID(N'[dbo].[fortnox_connections]', N'U') IS NOT NULL
            BEGIN
                DROP TABLE [fortnox_connections];
            END
            """);
    }
}
