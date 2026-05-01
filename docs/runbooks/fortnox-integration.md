# Fortnox Integration Runbook

## Purpose

This runbook covers operational setup, secret handling, rotation, callback troubleshooting, token refresh failures, and reconnect handling for the Fortnox finance integration. The developer setup guide is `../integrations/fortnox.md`.

## Production Setup

1. Create or identify the production Fortnox app registration.
2. Configure the Fortnox callback URL:

   `https://<api-host>/finance/integrations/fortnox/callback`

3. Create Azure Key Vault secrets using configuration-compatible names:

   - `FinanceIntegrations--Fortnox--Enabled`
   - `FinanceIntegrations--Fortnox--ClientId`
   - `FinanceIntegrations--Fortnox--ClientSecret`
   - `FinanceIntegrations--Fortnox--RedirectUri`
   - `FinanceIntegrations--Fortnox--TokenUrl`
   - `FinanceIntegrations--Fortnox--ApiBaseUrl`
   - `FinanceIntegrations--Fortnox--AuthorizationUrl`

4. Set `FinanceIntegrations--Fortnox--Enabled` to `true` only after the remaining required secrets are present.
5. Configure the application with `AzureKeyVault:Uri` or `KeyVault:Uri`.
6. Grant the application managed identity permission to read secrets from the vault.
7. Restart the API and verify startup succeeds.

## Required Secrets

Production must keep these values in Key Vault or the approved production secret store:

- `FinanceIntegrations--Fortnox--ClientId`
- `FinanceIntegrations--Fortnox--ClientSecret`
- `FinanceIntegrations--Fortnox--RedirectUri`
- `FinanceIntegrations--Fortnox--TokenUrl`
- `FinanceIntegrations--Fortnox--ApiBaseUrl`

`FinanceIntegrations--Fortnox--AuthorizationUrl` and `FinanceIntegrations--Fortnox--Scopes--<index>` should also be managed in the secret store or environment configuration when they differ from defaults.

Never commit Fortnox client secrets, authorization codes, access tokens, refresh tokens, or encrypted token payloads. Never paste them into logs, tickets, screenshots, or chat.

## Startup Validation

When Fortnox is enabled, the API fails fast if any required key is missing or blank:

- `FinanceIntegrations:Fortnox:ClientId`
- `FinanceIntegrations:Fortnox:ClientSecret`
- `FinanceIntegrations:Fortnox:RedirectUri`
- `FinanceIntegrations:Fortnox:TokenUrl`
- `FinanceIntegrations:Fortnox:ApiBaseUrl`

URI values must be absolute HTTP or HTTPS URLs.

Validation errors name the missing or invalid configuration path. They do not print the configured client secret or token values.

## Secret Rotation

Use this procedure for planned client secret rotation or emergency exposure response:

1. Create or prepare the new Fortnox client secret in the Fortnox developer app.
2. Add the new secret to the production secret store as `FinanceIntegrations--Fortnox--ClientSecret`.
3. Keep the old secret active until the deployed application is confirmed to use the new value, if Fortnox supports overlap.
4. Deploy or restart the API so configuration is reloaded and startup validation runs.
5. Verify a new connect flow reaches Fortnox and returns through `/finance/integrations/fortnox/callback`.
6. Verify an existing connection can refresh through `POST /oauth-v1/token`, or reconnect a test tenant if refresh tokens were invalidated by the rotation.
7. Revoke the old Fortnox client secret.
8. Confirm logs and UI output contain no client secret, authorization code, access token, or refresh token values.

If the old secret was exposed, treat all Fortnox refresh tokens as potentially at risk. Coordinate tenant reconnects and inspect logs for accidental token leakage.

## Redirect URI Changes

Changing the callback URL requires both Fortnox developer app configuration and application configuration.

1. Register the new callback URL in Fortnox, for example `https://<api-host>/finance/integrations/fortnox/callback`.
2. Update `FinanceIntegrations--Fortnox--RedirectUri` in the secret store.
3. Deploy or restart the API.
4. Start a connect flow from `/finance/integrations/fortnox/connect`.
5. Confirm Fortnox redirects to `/finance/integrations/fortnox/callback` and the application redirects back with `fortnoxConnection=connected`.
6. Remove the old callback URL from Fortnox after verification.

## Token Refresh And Reconnect

Access token retrieval is handled by `FortnoxOAuthService.GetValidAccessTokenAsync`.

- Valid access tokens are reused until they approach expiry.
- Refreshes use the configured token endpoint, normally `https://apps.fortnox.se/oauth-v1/token`.
- Refreshes are coordinated with the application distributed lock provider using a company and connection scoped lock.
- If another node is already refreshing the same connection, callers wait briefly, re-read the tenant-scoped token row, and return a transient retry result if the lock does not clear.
- Bad request, unauthorized, and forbidden responses from Fortnox are treated as expired or revoked authorization and mark the connection as needing reconnect.
- Missing refresh tokens, revoked connections, disconnected connections, and needs-reconnect statuses return reconnect-required results.
- Other transient token failures mark the connection as error with a safe reason so background retry paths can try again later.

Operationally, ask the tenant admin to reconnect Fortnox when the connection status is `NeedsReconnect`, `Revoked`, or `Disconnected`, or when support sees the safe message "Fortnox needs to be reconnected."

## Callback Failures

Use safe metadata only when troubleshooting callback failures:

- company id
- user id
- connection id when available
- callback timestamp
- correlation id
- safe callback message

Do not ask users to send authorization codes or full callback URLs if those URLs include `code`, `state`, or `nonce`.

## Troubleshooting

If startup fails with a missing Fortnox key:

1. Confirm `FinanceIntegrations:Fortnox:Enabled` is expected to be `true`.
2. Check the effective configuration source for the named path.
3. In Azure, confirm the Key Vault secret name uses `--` separators.
4. Confirm the app has managed identity enabled and secret read permissions.
5. Restart the API after fixing missing Key Vault values.

If Key Vault is not loading:

1. Confirm `AzureKeyVault:Uri` or `KeyVault:Uri` is set to the vault URI.
2. Confirm the URI is absolute and points to the intended vault.
3. Check Azure identity assignment and Key Vault access policy or RBAC role.
4. Use application logs only for provider and startup status. Do not add temporary logging of secret values.

## Safe Operations

- Never paste Fortnox client secrets, access tokens, or refresh tokens into logs, tickets, screenshots, or chat.
- Rotate the Fortnox client secret if it is exposed.
- Keep production and local Fortnox app registrations separate where possible.
- Keep scopes minimal and review scope changes before enabling new finance behavior.
- Treat callback errors as user-safe status messages. Do not surface raw Fortnox token or error payloads to users.

## Production Rollout Checklist

- Fortnox app exists for the target environment.
- Callback URL exactly matches `FinanceIntegrations:Fortnox:RedirectUri`.
- Required Fortnox configuration is present in the secret store.
- `FinanceIntegrations:Fortnox:Enabled` is set to `true` only after required values exist.
- API startup succeeds with options validation enabled.
- Test tenant can start the connect flow from `/finance/integrations/fortnox/connect`.
- Callback completes through `/finance/integrations/fortnox/callback`.
- Token refresh succeeds through `/oauth-v1/token`.
- Invalid refresh token behavior marks the connection as needing reconnect and does not expose token material.
- Logs and UI output contain no token, authorization code, or client secret values.
