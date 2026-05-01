# Goal
Implement backlog task **TASK-32.1.1** for **US-32.1 Fortnox configuration, OAuth connect flow, and secure token lifecycle** in the existing .NET solution.

Deliver a production-ready first slice of Fortnox configuration and secret wiring that:
- introduces **strongly typed Fortnox options**
- validates required configuration at startup when Fortnox is enabled
- wires configuration to support **local user-secrets** and **Azure Key Vault** without hardcoding secrets
- lays the secure foundation for the real OAuth connect/callback flow and token lifecycle
- adds/update docs under:
  - `/docs/integrations/fortnox.md`
  - `/docs/runbooks/fortnox-integration.md`

This task must be implemented in a way that fits the existing modular monolith architecture, keeps tenant boundaries intact, and avoids leaking secrets into logs, UI, or exceptions.

# Scope
Focus on the configuration, startup validation, and secure secret-provider wiring required by the acceptance criteria. Implement the minimum necessary code to support the real Fortnox integration path without overbuilding unrelated sync features.

In scope:
- Add a **strongly typed configuration model** for `FinanceIntegrations:Fortnox`
- Add an `Enabled` flag if one does not already exist, and validate required fields only when enabled
- Validate at startup that these values are present and non-empty when enabled:
  - `ClientId`
  - `ClientSecret`
  - `RedirectUri`
  - `TokenUrl`
  - `ApiBaseUrl`
- Register options using the standard ASP.NET Core options pattern with **startup validation / fail-fast**
- Wire configuration loading so local development can use **user-secrets**
- Wire production-ready secret loading for **Azure Key Vault**
- Ensure secret values are never logged
- Add safe defaults and validation messages that identify missing keys by config path, but do not echo secret values
- Add or prepare the Fortnox HTTP/OAuth service registration to consume the typed options
- Add/update docs for:
  - Fortnox app registration
  - scopes
  - callback URL
  - local user-secrets setup
  - production secret storage with Azure Key Vault
  - operational guidance / runbook

Out of scope unless required by existing code paths:
- Full Fortnox domain sync implementation
- UI polish beyond what is needed to support the connect/callback route wiring
- Full token encryption persistence if no storage model exists yet
- Background sync job redesign
- Broad refactors unrelated to Fortnox configuration and secret-provider setup

If the repository already contains partial Fortnox code, extend and align it rather than duplicating patterns.

# Files to touch
Inspect the solution first and then update the most appropriate files. Likely candidates include:

- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/appsettings.json`
- `src/VirtualCompany.Api/appsettings.Development.json`
- `src/VirtualCompany.Api/Properties/launchSettings.json` if needed for local behavior
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj` for user-secrets / Azure packages if missing
- `src/VirtualCompany.Infrastructure/...` Fortnox integration service registration and implementation files
- `src/VirtualCompany.Application/...` contracts or abstractions for Fortnox auth/config if needed
- `src/VirtualCompany.Domain/...` only if a domain type is truly needed
- `tests/VirtualCompany.Api.Tests/...` for startup/options validation tests
- `docs/integrations/fortnox.md`
- `docs/runbooks/fortnox-integration.md`

Also search for existing patterns around:
- options binding and validation
- external integration registration
- secret-provider wiring
- OAuth callback endpoints
- token storage / encryption
- structured logging and exception handling

Prefer modifying existing integration infrastructure over creating parallel patterns.

# Implementation plan
1. **Inspect the current solution structure**
   - Find where configuration, DI registration, and external integrations are currently wired.
   - Search for any existing Fortnox-related code, finance integration modules, OAuth endpoints, token entities, or encryption helpers.
   - Search for existing Azure Key Vault integration or secret-provider patterns already used elsewhere.
   - Search for existing options validation patterns such as `ValidateOnStart`, `IValidateOptions<T>`, data annotations, or custom startup checks.

2. **Add a strongly typed Fortnox options class**
   - Create a typed options class for `FinanceIntegrations:Fortnox`, likely in Infrastructure or Api depending on current conventions.
   - Include at minimum:
     - `Enabled`
     - `ClientId`
     - `ClientSecret`
     - `RedirectUri`
     - `TokenUrl`
     - `ApiBaseUrl`
     - optionally `AuthorizationUrl`, `Scopes`, and other fields if already used by current code
   - Keep secret-bearing properties clearly identified and avoid `ToString()` or debug helpers that could expose them.

3. **Add robust conditional validation**
   - Implement validation so that when `Enabled == true`, the required fields must be non-null/non-whitespace.
   - Use one of:
     - `IValidateOptions<FortnoxOptions>`
     - `.Validate(...)` plus `.ValidateOnStart()`
   - Validation errors must:
     - fail application startup
     - mention the missing config keys by path
     - not include actual secret values
   - Validate URI-shaped fields sensibly:
     - `RedirectUri`
     - `TokenUrl`
     - `ApiBaseUrl`
   - If `AuthorizationUrl` exists in current implementation, validate it too, but do not expand scope unnecessarily.

4. **Register options with fail-fast startup behavior**
   - In `Program.cs` or the existing composition root, bind `FinanceIntegrations:Fortnox` to the typed options.
   - Ensure validation runs on startup, not lazily on first request.
   - Keep registration consistent with existing project conventions.

5. **Wire local development secrets via user-secrets**
   - Ensure the API project supports user-secrets if not already configured.
   - Add the necessary project metadata/package setup only if missing.
   - Do not commit real secrets.
   - Ensure docs explain how to set:
     - `FinanceIntegrations:Fortnox:ClientId`
     - `FinanceIntegrations:Fortnox:ClientSecret`
     - `FinanceIntegrations:Fortnox:RedirectUri`
     - `FinanceIntegrations:Fortnox:TokenUrl`
     - `FinanceIntegrations:Fortnox:ApiBaseUrl`
   - If development config files contain placeholders, keep them non-secret and clearly marked.

6. **Wire Azure Key Vault for production secret loading**
   - Add Azure Key Vault configuration provider wiring in the startup path, preferably conditional on configuration presence such as vault URI or environment.
   - Reuse existing Azure configuration patterns if present.
   - Keep the implementation production-safe:
     - use managed identity / default Azure credential pattern where appropriate
     - do not require secrets in source control
   - Ensure the app can resolve Fortnox secrets from Key Vault-backed configuration keys.
   - Document expected key naming conventions and environment setup.

7. **Prepare or align Fortnox service registration**
   - Register any Fortnox auth/client services to consume `IOptions<FortnoxOptions>` or `IOptionsMonitor<FortnoxOptions>`.
   - If there is already a Fortnox OAuth/connect flow, update it to use the typed options instead of raw configuration access.
   - If there are existing hardcoded URLs or magic strings, replace them with typed options.
   - Ensure no logs print token payloads, client secrets, or raw Fortnox error bodies.

8. **Support the real connect/callback route foundation**
   - Verify whether `/finance/integrations/fortnox/connect` and `/finance/integrations/fortnox/callback` already exist.
   - If they exist, align them to use the typed options and configured endpoints.
   - If they do not exist and this task must cover the acceptance criteria foundation, add minimal endpoint/controller/page wiring needed for the real OAuth flow path.
   - Ensure callback error handling is safe:
     - validate state/nonce/user/company scope if that code path exists in this task slice
     - return user-safe errors for invalid/expired codes
     - never expose raw Fortnox payloads

9. **Protect secrets and tokens**
   - Review logging around Fortnox configuration, OAuth exchange, and HTTP failures.
   - Remove or prevent any logging of:
     - `ClientSecret`
     - access tokens
     - refresh tokens
     - raw Fortnox token/error payloads
   - If token persistence already exists, verify it is encrypted at rest or clearly leave a TODO only if another task owns persistence. Do not claim completion unless implemented.
   - Prefer structured logs with safe metadata only, such as company ID, user ID, correlation ID, and status category.

10. **Add tests**
   - Add tests covering startup/options validation behavior:
     - enabled + missing `ClientId` => startup/options validation failure
     - enabled + missing `ClientSecret` => failure
     - enabled + missing `RedirectUri` => failure
     - enabled + missing `TokenUrl` => failure
     - enabled + missing `ApiBaseUrl` => failure
     - disabled + missing values => no failure
   - Add tests for URI validation if implemented.
   - Add tests ensuring validation messages identify config keys without exposing secret values.
   - If feasible within current test patterns, add an integration/startup test for configuration provider precedence or Key Vault wiring abstraction.

11. **Write documentation**
   - `docs/integrations/fortnox.md` should cover:
     - what the integration does
     - required configuration keys
     - Fortnox app registration
     - authorization/token endpoints
     - callback URL
     - scopes
     - local development with user-secrets
     - expected environment variables / config keys
   - `docs/runbooks/fortnox-integration.md` should cover:
     - production secret storage in Azure Key Vault
     - managed identity / access setup
     - startup validation behavior
     - troubleshooting missing config
     - safe operational practices
     - reconnect/revoked token operational notes if relevant to current codebase

12. **Keep changes minimal and idiomatic**
   - Follow existing naming, folder structure, and DI conventions.
   - Avoid introducing a new configuration framework or custom secret abstraction unless the repo already uses one.
   - Prefer small, testable additions over broad refactors.

# Validation steps
Run and verify the following:

1. **Code discovery**
   - Confirm whether Fortnox integration code already exists and note what was reused.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Manual startup validation checks**
   - With `FinanceIntegrations:Fortnox:Enabled=true` and one required key missing, verify app startup fails fast.
   - Repeat for each required key.
   - Confirm the error message references the missing config path and does not print secret values.

5. **Disabled integration behavior**
   - With `Enabled=false` and missing Fortnox values, verify startup succeeds.

6. **User-secrets validation**
   - Configure local secrets via user-secrets and verify the app resolves them correctly.
   - Confirm no secrets are stored in committed appsettings files.

7. **Azure Key Vault wiring validation**
   - Verify the configuration provider is added correctly and can resolve Fortnox settings from Key Vault-backed configuration.
   - If live Key Vault access is not available in the dev environment, validate through code path inspection/tests and document any non-executable verification clearly.

8. **Security/logging review**
   - Inspect logs and exception handling paths to confirm:
     - client secret is never logged
     - access/refresh tokens are never logged
     - raw Fortnox payloads are not surfaced to users

9. **Route verification**
   - If connect/callback endpoints exist in scope, verify they use configured URLs and safe error handling.
   - Confirm route paths match:
     - `/finance/integrations/fortnox/connect`
     - `/finance/integrations/fortnox/callback`

10. **Documentation review**
   - Ensure both docs files exist and are actionable for local dev and production ops.

In your final implementation summary, include:
- files changed
- key design decisions
- what acceptance criteria are fully satisfied by this task slice
- any acceptance criteria that depend on follow-up tasks already in backlog

# Risks and follow-ups
- The acceptance criteria include OAuth callback validation, encrypted token storage, automatic refresh, and revoked/reconnect handling. If the current task slice only establishes configuration and secret-provider wiring, clearly identify any remaining gaps and do not overstate completion.
- Azure Key Vault integration may require package additions such as Azure.Extensions.AspNetCore.Configuration.Secrets and Azure.Identity if not already present; add only what is necessary.
- If the app currently logs outbound HTTP failures verbosely, Fortnox token/error payloads may leak unless sanitized.
- If there is no existing token encryption mechanism, a follow-up task may be needed to implement encrypted-at-rest storage using the project’s chosen data protection/encryption approach.
- If callback endpoints are not yet implemented, this task should at least prepare the typed configuration and service wiring they will consume; note any follow-up needed for full OAuth flow completion.
- Be careful with configuration key naming across appsettings, environment variables, user-secrets, and Key Vault mapping so the same options bind consistently across environments.