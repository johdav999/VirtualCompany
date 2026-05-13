using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Migration("20260504123000_AddFortnoxConnectionPersistenceForPostgreSql")]
[DbContext(typeof(VirtualCompanyDbContext))]
public partial class AddFortnoxConnectionPersistenceForPostgreSql : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS fortnox_connections (
                id uuid NOT NULL,
                company_id uuid NOT NULL,
                connected_by_user_id uuid NOT NULL,
                status character varying(32) NOT NULL,
                encrypted_access_token text NULL,
                encrypted_refresh_token text NULL,
                token_encryption_key_id character varying(128) NULL,
                token_encryption_algorithm character varying(64) NULL,
                access_token_expires_at timestamp with time zone NULL,
                refresh_token_expires_at timestamp with time zone NULL,
                granted_scopes_json text NOT NULL DEFAULT '[]',
                provider_tenant_id character varying(256) NULL,
                fortnox_company_name character varying(256) NULL,
                provider_metadata_json text NOT NULL DEFAULT '{}',
                connected_at timestamp with time zone NULL,
                last_refresh_attempt_at timestamp with time zone NULL,
                last_successful_refresh_at timestamp with time zone NULL,
                last_validated_at timestamp with time zone NULL,
                last_sync_at timestamp with time zone NULL,
                last_error_summary character varying(1000) NULL,
                disconnected_at timestamp with time zone NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_fortnox_connections PRIMARY KEY (id),
                CONSTRAINT ak_fortnox_connections_company_id_id UNIQUE (company_id, id),
                CONSTRAINT ck_fortnox_connections_status CHECK (status IN ('pending', 'connected', 'needs_reconnect', 'revoked', 'error', 'disconnected')),
                CONSTRAINT fk_fortnox_connections_companies_company_id FOREIGN KEY (company_id) REFERENCES companies ("Id") ON DELETE CASCADE,
                CONSTRAINT fk_fortnox_connections_users_connected_by_user_id FOREIGN KEY (connected_by_user_id) REFERENCES users ("Id") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_fortnox_connections_company_id ON fortnox_connections (company_id);
            CREATE INDEX IF NOT EXISTS ix_fortnox_connections_company_id_status ON fortnox_connections (company_id, status);
            CREATE INDEX IF NOT EXISTS ix_fortnox_connections_connected_by_user_id ON fortnox_connections (connected_by_user_id);
            CREATE INDEX IF NOT EXISTS ix_fortnox_connections_status ON fortnox_connections (status);
            CREATE INDEX IF NOT EXISTS ix_fortnox_connections_access_token_expires_at ON fortnox_connections (access_token_expires_at);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS fortnox_oauth_states (
                id uuid NOT NULL,
                company_id uuid NOT NULL,
                user_id uuid NOT NULL,
                connection_id uuid NULL,
                state_hash character varying(128) NOT NULL,
                created_at timestamp with time zone NOT NULL,
                expires_at timestamp with time zone NOT NULL,
                consumed_at timestamp with time zone NULL,
                callback_received_at timestamp with time zone NULL,
                redirect_uri character varying(2048) NULL,
                code_verifier_ciphertext text NULL,
                failure_reason character varying(1000) NULL,
                CONSTRAINT pk_fortnox_oauth_states PRIMARY KEY (id),
                CONSTRAINT fk_fortnox_oauth_states_companies_company_id FOREIGN KEY (company_id) REFERENCES companies ("Id") ON DELETE CASCADE,
                CONSTRAINT fk_fortnox_oauth_states_users_user_id FOREIGN KEY (user_id) REFERENCES users ("Id") ON DELETE RESTRICT,
                CONSTRAINT fk_fortnox_oauth_states_fortnox_connections_company_id_connection_id FOREIGN KEY (company_id, connection_id) REFERENCES fortnox_connections (company_id, id) ON DELETE NO ACTION
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_fortnox_oauth_states_state_hash ON fortnox_oauth_states (state_hash);
            CREATE INDEX IF NOT EXISTS ix_fortnox_oauth_states_company_id_user_id ON fortnox_oauth_states (company_id, user_id);
            CREATE INDEX IF NOT EXISTS ix_fortnox_oauth_states_expires_at ON fortnox_oauth_states (expires_at);
            CREATE INDEX IF NOT EXISTS ix_fortnox_oauth_states_consumed_at ON fortnox_oauth_states (consumed_at);
            CREATE INDEX IF NOT EXISTS ix_fortnox_oauth_states_user_id ON fortnox_oauth_states (user_id);
            CREATE INDEX IF NOT EXISTS ix_fortnox_oauth_states_company_id_connection_id ON fortnox_oauth_states (company_id, connection_id);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS fortnox_sync_histories (
                id uuid NOT NULL,
                company_id uuid NOT NULL,
                fortnox_connection_id uuid NOT NULL,
                sync_type character varying(64) NOT NULL,
                direction character varying(32) NOT NULL,
                status character varying(32) NOT NULL,
                started_at timestamp with time zone NOT NULL,
                completed_at timestamp with time zone NULL,
                triggered_by_user_id uuid NULL,
                records_processed integer NOT NULL DEFAULT 0,
                records_succeeded integer NOT NULL DEFAULT 0,
                records_failed integer NOT NULL DEFAULT 0,
                correlation_id character varying(128) NULL,
                error_summary character varying(1000) NULL,
                metadata_json text NOT NULL DEFAULT '{}',
                CONSTRAINT pk_fortnox_sync_histories PRIMARY KEY (id),
                CONSTRAINT ck_fortnox_sync_histories_direction CHECK (direction IN ('import', 'export', 'bidirectional')),
                CONSTRAINT ck_fortnox_sync_histories_status CHECK (status IN ('running', 'succeeded', 'failed')),
                CONSTRAINT ck_fortnox_sync_histories_records_processed_nonnegative CHECK (records_processed >= 0),
                CONSTRAINT ck_fortnox_sync_histories_records_succeeded_nonnegative CHECK (records_succeeded >= 0),
                CONSTRAINT ck_fortnox_sync_histories_records_failed_nonnegative CHECK (records_failed >= 0),
                CONSTRAINT fk_fortnox_sync_histories_companies_company_id FOREIGN KEY (company_id) REFERENCES companies ("Id") ON DELETE CASCADE,
                CONSTRAINT fk_fortnox_sync_histories_fortnox_connections_company_id_fortnox_connection_id FOREIGN KEY (company_id, fortnox_connection_id) REFERENCES fortnox_connections (company_id, id) ON DELETE CASCADE,
                CONSTRAINT fk_fortnox_sync_histories_users_triggered_by_user_id FOREIGN KEY (triggered_by_user_id) REFERENCES users ("Id") ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS ix_fortnox_sync_histories_company_id_fortnox_connection_id_started_at ON fortnox_sync_histories (company_id, fortnox_connection_id, started_at);
            CREATE INDEX IF NOT EXISTS ix_fortnox_sync_histories_status ON fortnox_sync_histories (status);
            CREATE INDEX IF NOT EXISTS ix_fortnox_sync_histories_company_id_correlation_id ON fortnox_sync_histories (company_id, correlation_id);
            CREATE INDEX IF NOT EXISTS ix_fortnox_sync_histories_triggered_by_user_id ON fortnox_sync_histories (triggered_by_user_id);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS fortnox_external_references (
                id uuid NOT NULL,
                company_id uuid NOT NULL,
                fortnox_connection_id uuid NULL,
                entity_type character varying(64) NOT NULL,
                internal_entity_id uuid NOT NULL,
                external_entity_type character varying(64) NOT NULL,
                external_id character varying(256) NOT NULL,
                external_display_reference character varying(128) NULL,
                last_synced_at timestamp with time zone NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_fortnox_external_references PRIMARY KEY (id),
                CONSTRAINT fk_fortnox_external_references_companies_company_id FOREIGN KEY (company_id) REFERENCES companies ("Id") ON DELETE CASCADE,
                CONSTRAINT fk_fortnox_external_references_fortnox_connections_company_id_fortnox_connection_id FOREIGN KEY (company_id, fortnox_connection_id) REFERENCES fortnox_connections (company_id, id) ON DELETE NO ACTION
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_fortnox_external_references_company_id_entity_type_internal_entity_id_external_entity_type ON fortnox_external_references (company_id, entity_type, internal_entity_id, external_entity_type);
            CREATE INDEX IF NOT EXISTS ix_fortnox_external_references_company_id_external_entity_type_external_id ON fortnox_external_references (company_id, external_entity_type, external_id);
            CREATE INDEX IF NOT EXISTS ix_fortnox_external_references_company_id_fortnox_connection_id ON fortnox_external_references (company_id, fortnox_connection_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS fortnox_external_references;
            DROP TABLE IF EXISTS fortnox_sync_histories;
            DROP TABLE IF EXISTS fortnox_oauth_states;
            DROP TABLE IF EXISTS fortnox_connections;
            """);
    }
}
