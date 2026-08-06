# Finance provider application management

Virtual Company separates the shared provider application from each company's authorization:

- A platform administrator configures one OAuth application per finance provider.
- Company administrators use **Connect** or **Reconnect** and never handle a client ID, client secret, or command line.
- Provider application metadata is stored in SQL.
- Provider application credentials are versioned in a platform secret store and are never stored in SQL, configuration files, browser responses, logs, or audit rows.
- Company access and refresh tokens remain company-scoped and encrypted in SQL.

The implementation is provider-neutral. A provider appears in **System / Admin → Finance providers** when it supplies an `IFinanceIntegrationApplicationDefinition` and its normal finance integration adapter. Fortnox is the first registered provider.

## Platform administration

The management page is:

`/system/admin/finance-providers`

Only identities listed in `PlatformAdministration:AdministratorIdentities` receive the `PlatformAdministration` policy. Values are either the authenticated `provider:subject` pair or an exact email address. Manage this allow-list through deployment configuration or the identity platform; it is not company-controlled.

The page supports:

- enabling or disabling company connections;
- the exact OAuth callback URL;
- a client ID field that preserves the current value when left blank;
- a write-only client secret field;
- provider-supported scope selection;
- configuration validation;
- a secret-free audit history.

The API is under `/api/platform/finance-integration-applications` and applies the same platform policy.

## Secret persistence

### Production

Configure Azure Key Vault:

```text
PlatformSecrets__Provider=azure_key_vault
PlatformSecrets__KeyVaultUri=https://<vault-name>.vault.azure.net/
```

`AzureKeyVault__Uri` or `KeyVault__Uri` can also supply the vault URI. The API uses `DefaultAzureCredential`; use a managed identity with permission to read and write secret versions. Do not put provider credentials in environment variables.

The SQL row stores only the generated secret name and immutable Key Vault version. A credential rotation creates a new version and switches the metadata reference after the write succeeds.

If no supported production secret backend is configured, the admin page remains safe and read-only and company connection attempts explain that a Virtual Company administrator must finish setup.

### Development

Development defaults to a Data Protection-encrypted file:

`%LOCALAPPDATA%\VirtualCompany\Secrets\platform-secrets.json`

The file is outside the repository and build output. It is protected by the stable development Data Protection key ring, so rebuilding the application does not invalidate it. It is intentionally rejected outside the Development environment.

## Data Protection keys

Company OAuth tokens and the development secret file depend on ASP.NET Core Data Protection. Production must configure:

```text
DataProtection__KeyRingPath=<durable access-controlled shared path>
```

Every API instance must mount the same durable path. Container deployments must mount it as a persistent volume. The application refuses to start outside Development without this setting, preventing credentials from becoming undecryptable after a restart or deployment.

Back up and restore the key ring with the database. Restrict access to the API identity.

## Database and containers

Apply the `AddFinanceIntegrationProviderManagement` EF migration. It adds:

- `finance_integration_provider_configurations`
- `finance_integration_provider_configuration_audits`

The schema stores metadata and audit information only. It uses ordinary SQL Server types and works for local SQL Server and the Docker SQL Server restore flow. A Docker deployment must additionally mount the Data Protection key ring and allow the API managed identity to access the configured vault.

## Adding another finance provider

1. Implement and register the existing provider-neutral OAuth, sync, write, and mapping adapters.
2. Implement `IFinanceIntegrationApplicationDefinition` with the provider key, callback path, supported scopes, and defaults.
3. Resolve runtime credentials through `IFinanceIntegrationRuntimeSettingsProvider`; do not add provider secrets to options or controllers.
4. Keep company tokens in a company-scoped encrypted token store.
5. Add contract, authorization, tenant-isolation, callback, refresh, and reconnect tests.

No new admin page or secret schema is required.

## Operational checks

- Confirm the provider configuration is **Ready** before company rollout.
- Register the displayed callback URL exactly in the provider portal.
- Validate the setup after scope, callback, client ID, or secret changes.
- Reconnect existing companies when provider scopes change.
- Verify that logs and audit records contain identifiers and outcomes only.
- Test database plus Data Protection key-ring restore before production use.
