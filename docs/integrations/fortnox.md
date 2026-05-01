# Fortnox Integration

This page describes the Fortnox finance integration used by US-32.1 for configuration, OAuth connection, callback validation, and secure token lifecycle. It is grounded in the current implementation under `src/VirtualCompany.Api`, `src/VirtualCompany.Application/Finance`, and `src/VirtualCompany.Infrastructure/Finance`.

The integration is disabled by default. When enabled, configuration is validated on startup, OAuth state is stored server-side in the distributed cache, tokens are encrypted before persistence in `fortnox_connections`, and all connection reads are scoped by `CompanyId`.

## Configuration

The API binds Fortnox settings from `FinanceIntegrations:Fortnox`.

Required when `Enabled` is `true`:

- `FinanceIntegrations:Fortnox:ClientId`
- `FinanceIntegrations:Fortnox:ClientSecret`
- `FinanceIntegrations:Fortnox:RedirectUri`
- `FinanceIntegrations:Fortnox:TokenUrl`
- `FinanceIntegrations:Fortnox:ApiBaseUrl`

Defaulted and validated when enabled:

- `FinanceIntegrations:Fortnox:AuthorizationUrl`
- `FinanceIntegrations:Fortnox:Scopes`

Safe default endpoints:

- Authorization URL: `https://apps.fortnox.se/oauth-v1/auth`
- Token URL: `https://apps.fortnox.se/oauth-v1/token`
- API base URL: `https://api.fortnox.se/3/`

Do not store `ClientId`, `ClientSecret`, access tokens, or refresh tokens in committed configuration files.

Startup validation is registered in `VirtualCompany.Infrastructure.DependencyInjection` with `FortnoxOptionsValidator` and `.ValidateOnStart()`. When `FinanceIntegrations:Fortnox:Enabled` is `true`, missing or blank required values fail startup. URI values must be absolute `http` or `https` URLs. Validation messages name configuration paths and do not echo configured secret values.

The checked-in `appsettings.json` and `appsettings.Development.json` keep Fortnox disabled and use empty placeholders for secrets. Real values belong in user-secrets locally and in a production secret store.

## Routes

The API controller is `FortnoxConnectionsController`.

- Browser connect route: `GET /finance/integrations/fortnox/connect?companyId=<company-id>&reconnect=false&returnUri=<optional-return-uri>`
- Browser callback route registered in Fortnox: `GET /finance/integrations/fortnox/callback`
- Company-scoped API status route: `GET /api/companies/{companyId}/finance/integrations/fortnox/status`
- Company-scoped API connect route: `POST /api/companies/{companyId}/finance/integrations/fortnox/connect`

## Fortnox App Registration

Create the application in Fortnox Developer and configure the callback URL to match the API route:

`https://<api-host>/finance/integrations/fortnox/callback`

For local development the callback URL normally maps to the HTTPS API launch profile:

`https://localhost:7136/finance/integrations/fortnox/callback`

Register a separate Fortnox developer app for each production-like environment where possible. Use the least privileged Fortnox scopes required for the finance features being enabled. The code does not hard-code scope names; configured `FinanceIntegrations:Fortnox:Scopes` values are trimmed, joined with spaces, and sent as the OAuth `scope` parameter. Document the exact approved scopes in the environment change record when enabling or changing production access.

The authorization request uses:

- authorization endpoint: `FinanceIntegrations:Fortnox:AuthorizationUrl`, default `https://apps.fortnox.se/oauth-v1/auth`
- token endpoint: `FinanceIntegrations:Fortnox:TokenUrl`, default `https://apps.fortnox.se/oauth-v1/token`
- response type: `code`
- callback URL: `FinanceIntegrations:Fortnox:RedirectUri`
- OAuth parameters: `client_id`, `redirect_uri`, `response_type`, `state`, `nonce`, and optional `scope`

## Local Development

The API project has a `UserSecretsId`, so ASP.NET Core user-secrets are available in development.

Set local values from the repository root:

```powershell
dotnet user-secrets set "FinanceIntegrations:Fortnox:Enabled" "true" --project src/VirtualCompany.Api
dotnet user-secrets set "FinanceIntegrations:Fortnox:ClientId" "<client-id>" --project src/VirtualCompany.Api
dotnet user-secrets set "FinanceIntegrations:Fortnox:ClientSecret" "<client-secret>" --project src/VirtualCompany.Api
dotnet user-secrets set "FinanceIntegrations:Fortnox:RedirectUri" "https://localhost:7136/finance/integrations/fortnox/callback" --project src/VirtualCompany.Api
dotnet user-secrets set "FinanceIntegrations:Fortnox:TokenUrl" "https://apps.fortnox.se/oauth-v1/token" --project src/VirtualCompany.Api
dotnet user-secrets set "FinanceIntegrations:Fortnox:ApiBaseUrl" "https://api.fortnox.se/3/" --project src/VirtualCompany.Api
```

Set scopes as configuration values using array indexes if needed:

```powershell
dotnet user-secrets set "FinanceIntegrations:Fortnox:Scopes:0" "<scope-name>" --project src/VirtualCompany.Api
```

## Production Secrets

Production secrets must be stored in the environment secret store. The current API supports Azure Key Vault through configuration loaded in `Program.cs`. Configure one of these non-secret values for the API:

- `AzureKeyVault:Uri`
- `KeyVault:Uri`

The application uses `DefaultAzureCredential`, so prefer managed identity in Azure hosting environments.

Use Key Vault secret names that map to configuration keys by replacing `:` with `--`:

- `FinanceIntegrations--Fortnox--ClientId`
- `FinanceIntegrations--Fortnox--ClientSecret`
- `FinanceIntegrations--Fortnox--RedirectUri`
- `FinanceIntegrations--Fortnox--TokenUrl`
- `FinanceIntegrations--Fortnox--ApiBaseUrl`
- `FinanceIntegrations--Fortnox--AuthorizationUrl`

The startup validator reports missing configuration by key path and never echoes configured values.

Rotate `ClientSecret` through the secret store, not through source control. Keep old and new secrets available only for the shortest practical overlap supported by the Fortnox developer app, deploy the updated secret, verify a new connect flow and a refresh flow, then revoke the old secret.

## Connect Flow

The connect flow starts from the browser route or the company-scoped API route. Both require an authenticated user with the company admin policy and resolved company context.

1. `FortnoxOAuthService.BuildAuthorizationUrlAsync` checks the resolved company and user through `ICompanyContextAccessor`.
2. A random nonce and `FortnoxOAuthState` are created with a 10 minute TTL.
3. `DistributedCacheFortnoxOAuthSessionStore` stores protected state in the distributed cache using a company-scoped key. The browser receives only an opaque state handle.
4. `FortnoxOAuthClient.BuildAuthorizationUrl` builds the Fortnox authorization URL from configured endpoints, client id, redirect URI, state, nonce, and optional scopes.
5. The browser is redirected to Fortnox.

`returnUri` is optional. When supplied, it must be an absolute `http` or `https` URL on the current host, or `localhost` in development, and its path must start with `/finance/integrations/fortnox`.

## Callback Validation

The callback route safely handles cancelled, invalid, or expired authorization attempts.

Current validation includes:

- state: required in the callback and consumed from the server-side distributed cache
- nonce: generated at connect time and compared in constant time when Fortnox returns a nonce
- authenticated user: the callback uses the resolved current user and rejects mismatched users
- company scope: the callback requires `companyId`, consumes state under that company, and rejects mismatched company/user state
- state expiry: expired state fails with a safe reconnect message
- invalid or expired authorization code: token exchange failures from Fortnox are mapped to safe messages and do not expose provider payloads

The callback redirects back to `/finance/integrations/fortnox` or the validated return URI with `fortnoxConnection`, optional `fortnoxMessage`, and `companyId` query parameters. Raw tokens and raw Fortnox token responses are never rendered in the UI.

## Token Storage And Refresh

`FortnoxTokenStore` persists connection state in `fortnox_connections`. Access and refresh tokens are encrypted through `IFieldEncryptionService` before they are stored. Encryption purposes are separated for access tokens and refresh tokens, and encryption is company-scoped.

`FortnoxOAuthClient` exchanges authorization codes and refresh tokens with `POST /oauth-v1/token` using HTTP Basic authentication with the configured client id and client secret. It sends form-encoded bodies:

- authorization code exchange: `grant_type=authorization_code`, `code`, and `redirect_uri`
- refresh: `grant_type=refresh_token` and `refresh_token`

`FortnoxOAuthService.GetValidAccessTokenAsync` returns a still-valid access token when one exists. If the token is missing or inside the refresh skew window, refresh is coordinated with `IDistributedLockProvider` using a company and connection scoped lock so only one node refreshes a connection at a time.

Invalid refresh tokens or revoked authorization responses from Fortnox mark the connection as `NeedsReconnect` and return a reconnect-required result. Transient failures mark the connection as `Error` with a safe reason and let background retry paths try again later. `Revoked` and `Disconnected` statuses are treated as reconnect-required when read.

## Logging And Secret Handling

- Do not log Fortnox client secrets, authorization codes, access tokens, refresh tokens, encrypted token payloads, or raw provider token responses.
- Existing Fortnox logs include company, user, connection, reconnect, and coordination metadata only.
- User-facing callback errors use safe messages from `FortnoxOAuthException` or a generic authorization failure.
- Support tickets and incident notes must use connection ids, company ids, timestamps, and correlation ids instead of token material.

## Verification Checklist

- Fortnox is disabled by default in committed configuration.
- Enabling Fortnox without required configuration fails startup.
- The Fortnox developer app callback URL exactly matches `FinanceIntegrations:Fortnox:RedirectUri`.
- Local secrets are set with `dotnet user-secrets` for `src/VirtualCompany.Api`.
- Production secrets are loaded from Key Vault or the approved production secret store.
- Connect and callback routes require an authenticated company admin and resolved company context.
- Token rows in `fortnox_connections` contain encrypted token fields only.
- Logs and UI output do not include token or client secret values.
