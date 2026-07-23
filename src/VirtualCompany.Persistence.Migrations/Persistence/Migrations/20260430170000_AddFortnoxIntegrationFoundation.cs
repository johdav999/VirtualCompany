using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Migration("20260430170000_AddFortnoxIntegrationFoundation")]
    [DbContext(typeof(VirtualCompanyDbContext))]
    public partial class AddFortnoxIntegrationFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                return;
            }

            migrationBuilder.CreateTable(
                name: "finance_integration_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                    connected_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider_tenant_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    display_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    scopes_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    connected_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_sync_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    disabled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_integration_connections", x => x.id);
                    table.UniqueConstraint("AK_finance_integration_connections_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_finance_integration_connections_status", "status IN ('pending', 'connected', 'needs_reconnect', 'disabled', 'error')");
                    table.ForeignKey("FK_finance_integration_connections_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_finance_integration_connections_users_connected_by_user_id", x => x.connected_by_user_id, "users", "Id", onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "finance_integration_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    token_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    encrypted_token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_integration_tokens", x => x.id);
                    table.CheckConstraint("CK_finance_integration_tokens_token_type", "token_type IN ('access', 'refresh')");
                    table.ForeignKey("FK_finance_integration_tokens_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        "FK_finance_integration_tokens_finance_integration_connections_company_id_connection_id",
                        x => new { x.company_id, x.connection_id },
                        "finance_integration_connections",
                        new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_integration_sync_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    scope_key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, defaultValue: "default"),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                    cursor = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    last_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    consecutive_failure_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_integration_sync_states", x => x.id);
                    table.CheckConstraint("CK_finance_integration_sync_states_status", "status IN ('pending', 'running', 'succeeded', 'failed')");
                    table.ForeignKey("FK_finance_integration_sync_states_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        "FK_finance_integration_sync_states_finance_integration_connections_company_id_connection_id",
                        x => new { x.company_id, x.connection_id },
                        "finance_integration_connections",
                        new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "finance_external_references",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    internal_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    external_number = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    external_updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_external_references", x => x.id);
                    table.ForeignKey("FK_finance_external_references_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        "FK_finance_external_references_finance_integration_connections_company_id_connection_id",
                        x => new { x.company_id, x.connection_id },
                        "finance_integration_connections",
                        new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_integration_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provider_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    internal_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    updated_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    skipped_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    error_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_integration_audit_events", x => x.id);
                    table.CheckConstraint("CK_finance_integration_audit_events_outcome", "outcome IN ('succeeded', 'failed', 'skipped')");
                    table.ForeignKey("FK_finance_integration_audit_events_companies_company_id", x => x.company_id, "companies", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        "FK_finance_integration_audit_events_finance_integration_connections_company_id_connection_id",
                        x => new { x.company_id, x.connection_id },
                        "finance_integration_connections",
                        new[] { "company_id", "id" },
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex("IX_finance_integration_connections_company_id_provider_key", "finance_integration_connections", new[] { "company_id", "provider_key" }, unique: true);
            migrationBuilder.CreateIndex("IX_finance_integration_connections_company_id_status", "finance_integration_connections", new[] { "company_id", "status" });
            migrationBuilder.CreateIndex("IX_finance_integration_connections_connected_by_user_id", "finance_integration_connections", "connected_by_user_id");
            migrationBuilder.CreateIndex("IX_finance_integration_connections_provider_key_provider_tenant_id", "finance_integration_connections", new[] { "provider_key", "provider_tenant_id" });
            migrationBuilder.CreateIndex("IX_finance_integration_tokens_company_id_connection_id", "finance_integration_tokens", new[] { "company_id", "connection_id" });
            migrationBuilder.CreateIndex("IX_finance_integration_tokens_company_id_provider_key_token_type", "finance_integration_tokens", new[] { "company_id", "provider_key", "token_type" });
            migrationBuilder.CreateIndex("IX_finance_integration_tokens_connection_id_token_type", "finance_integration_tokens", new[] { "connection_id", "token_type" }, unique: true);
            migrationBuilder.CreateIndex("IX_finance_integration_sync_states_company_id_connection_id", "finance_integration_sync_states", new[] { "company_id", "connection_id" });
            migrationBuilder.CreateIndex("IX_finance_integration_sync_states_company_id_provider_key_entity_type_scope_key", "finance_integration_sync_states", new[] { "company_id", "provider_key", "entity_type", "scope_key" }, unique: true);
            migrationBuilder.CreateIndex("IX_finance_integration_sync_states_company_id_status_last_started_at", "finance_integration_sync_states", new[] { "company_id", "status", "last_started_at" });
            migrationBuilder.CreateIndex("IX_finance_external_references_company_id_connection_id", "finance_external_references", new[] { "company_id", "connection_id" });
            migrationBuilder.CreateIndex("IX_finance_external_references_company_id_entity_type_internal_record_id", "finance_external_references", new[] { "company_id", "entity_type", "internal_record_id" }, unique: true);
            migrationBuilder.CreateIndex("IX_finance_external_references_company_id_provider_key_entity_type_external_id", "finance_external_references", new[] { "company_id", "provider_key", "entity_type", "external_id" }, unique: true);
            migrationBuilder.CreateIndex("IX_finance_external_references_company_id_provider_key_entity_type_external_number", "finance_external_references", new[] { "company_id", "provider_key", "entity_type", "external_number" });
            migrationBuilder.CreateIndex("IX_finance_integration_audit_events_company_id_provider_key_created_at", "finance_integration_audit_events", new[] { "company_id", "provider_key", "created_at" });
            migrationBuilder.CreateIndex("IX_finance_integration_audit_events_company_id_connection_id_created_at", "finance_integration_audit_events", new[] { "company_id", "connection_id", "created_at" });
            migrationBuilder.CreateIndex("IX_finance_integration_audit_events_company_id_entity_type_internal_record_id", "finance_integration_audit_events", new[] { "company_id", "entity_type", "internal_record_id" });
            migrationBuilder.CreateIndex("IX_finance_integration_audit_events_company_id_correlation_id", "finance_integration_audit_events", new[] { "company_id", "correlation_id" });

            foreach (var table in SourceTrackedTables)
            {
                migrationBuilder.Sql($"""
                    IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                    BEGIN
                    ALTER TABLE [{table}] ADD [source_type] nvarchar(32) NOT NULL CONSTRAINT [DF_{table}_source_type] DEFAULT N'manual';
                    ALTER TABLE [{table}] ADD [provider_key] nvarchar(64) NULL;
                    ALTER TABLE [{table}] ADD [provider_external_id] nvarchar(256) NULL;
                    ALTER TABLE [{table}] ADD [finance_external_reference_id] uniqueidentifier NULL;
                    END
                    """);

                migrationBuilder.Sql($"""
                    IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                    BEGIN
                    ALTER TABLE [{table}] ADD CONSTRAINT [CK_{table}_source_type] CHECK ([source_type] IN ('manual', 'simulation', 'fortnox'));
                    END
                    """);

                migrationBuilder.Sql($"""
                    IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                    BEGIN
                    CREATE INDEX [IX_{table}_company_id_source_type] ON [{table}] ([company_id], [source_type]);
                    END
                    """);

                migrationBuilder.Sql($"""
                    IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                    BEGIN
                    CREATE INDEX [IX_{table}_company_id_provider_key_provider_external_id] ON [{table}] ([company_id], [provider_key], [provider_external_id]) WHERE [provider_key] IS NOT NULL AND [provider_external_id] IS NOT NULL;
                    END
                    """);

                migrationBuilder.Sql($"""
                    IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                    BEGIN
                    CREATE INDEX [IX_{table}_finance_external_reference_id] ON [{table}] ([finance_external_reference_id]);
                    END
                    """);

                migrationBuilder.Sql($"""
                    IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                    BEGIN
                    ALTER TABLE [{table}] ADD CONSTRAINT [FK_{table}_finance_external_references_finance_external_reference_id] FOREIGN KEY ([finance_external_reference_id]) REFERENCES [finance_external_references] ([id]) ON DELETE NO ACTION;
                    END
                    """);
            }

            foreach (var table in SimulationBackfillTables)
            {
                migrationBuilder.Sql($"""
                    IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
                    BEGIN
                    UPDATE [{table}]
                    SET [source_type] = N'simulation'
                    WHERE [source_simulation_event_record_id] IS NOT NULL;
                    END
                    """);
            }

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[bank_transactions]', N'U') IS NOT NULL
                BEGIN
                UPDATE [bank_transactions]
                SET [source_type] = N'simulation'
                WHERE [source_simulation_event_record_id] IS NOT NULL;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                return;
            }

            foreach (var table in SourceTrackedTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE [{table}] DROP CONSTRAINT [FK_{table}_finance_external_references_finance_external_reference_id];
                    DROP INDEX [IX_{table}_finance_external_reference_id] ON [{table}];
                    DROP INDEX [IX_{table}_company_id_provider_key_provider_external_id] ON [{table}];
                    DROP INDEX [IX_{table}_company_id_source_type] ON [{table}];
                    ALTER TABLE [{table}] DROP CONSTRAINT [CK_{table}_source_type];
                    ALTER TABLE [{table}] DROP CONSTRAINT [DF_{table}_source_type];
                    ALTER TABLE [{table}] DROP COLUMN [finance_external_reference_id];
                    ALTER TABLE [{table}] DROP COLUMN [provider_external_id];
                    ALTER TABLE [{table}] DROP COLUMN [provider_key];
                    ALTER TABLE [{table}] DROP COLUMN [source_type];
                    """);
            }

            migrationBuilder.DropTable(name: "finance_integration_audit_events");
            migrationBuilder.DropTable(name: "finance_external_references");
            migrationBuilder.DropTable(name: "finance_integration_sync_states");
            migrationBuilder.DropTable(name: "finance_integration_tokens");
            migrationBuilder.DropTable(name: "finance_integration_connections");
        }

        private static readonly string[] SourceTrackedTables =
        [
            "finance_accounts",
            "finance_counterparties",
            "finance_invoices",
            "finance_bills",
            "finance_transactions",
            "finance_balances",
            "finance_payments",
            "company_bank_accounts",
            "bank_transactions",
            "finance_assets"
        ];

        private static readonly string[] SimulationBackfillTables =
        [
            "finance_invoices",
            "finance_bills",
            "finance_transactions",
            "finance_balances",
            "finance_payments",
            "finance_assets"
        ];
    }
}
