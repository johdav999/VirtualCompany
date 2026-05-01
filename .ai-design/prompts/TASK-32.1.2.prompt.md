# Goal
Implement backlog task **TASK-32.1.2** by adding a production-ready **FortnoxOAuthService** and supporting plumbing for real Fortnox OAuth in the .NET solution.

The implementation must satisfy the story **US-32.1 Fortnox configuration, OAuth connect flow, and secure token lifecycle** and meet all listed acceptance criteria.

Focus on:
- fail-fast configuration validation at startup when Fortnox integration is enabled
- authorization URL generation
- callback validation for state, nonce, authenticated user, and company scope
- authorization code exchange against real Fortnox token endpoint
- encrypted token persistence
- automatic access token refresh via `/oauth-v1/token`
- reconnect / revoked handling when refresh fails
- safe user-facing errors and no secret/token leakage in logs or UI
- documentation under the required docs paths

# Scope
In scope:
- Add or complete Fortnox integration configuration model/options validation
- Implement `FortnoxOAuthService` with methods for:
  - connect URL generation
  - callback validation
  - code exchange
  - reconnect handling
  - token refresh support
- Add secure state/nonce handling for OAuth roundtrip
- Ensure callback flow validates:
  - current authenticated user
  - company/tenant scope
  - state
  - nonce if used in roundtrip payload
  - code expiry/invalid code handling
- Persist Fortnox tokens encrypted at rest
- Ensure tokens are never logged and never rendered in UI DTOs/views
- Update connection status transitions for invalid refresh token / revoked / needs reconnect
- Ensure background sync callers can handle refresh failure without crashing jobs
- Add/update docs:
  - `/docs/integrations/fortnox.md`
  - `/docs/runbooks/fortnox-integration.md`
- Add tests for config validation, callback validation, token exchange/refresh behavior, and safe failure paths

Out of scope unless required by existing architecture:
- Full Fortnox data sync implementation
- Broad UI redesign beyond wiring connect/callback endpoints and safe error display
- New generic integration framework refactors not needed for this task
- Replacing existing encryption infrastructure if one already exists; prefer reuse

# Files to touch
Inspect first, then update the actual existing files that best match the current project structure. Likely areas:

- `src/VirtualCompany.Api/**`
  - startup / DI registration
  - options binding and validation
  - Fortnox connect/callback endpoints or controllers
- `src/VirtualCompany.Application/**`
  - service interfaces / application services for integrations
  - commands/handlers for connect/callback/reconnect status updates
- `src/VirtualCompany.Domain/**`
  - Fortnox connection entity/value objects/status enums if present
- `src/VirtualCompany.Infrastructure/**`
  - `FortnoxOAuthService`
  - HTTP client for Fortnox auth/token calls
  - token encryption/persistence implementation
  - repository updates for integration connection records
  - options validators
- `src/VirtualCompany.Web/**`
  - connect entrypoint at `/finance/integrations/fortnox/connect`
  - callback endpoint/page at `/finance/integrations/fortnox/callback`
  - safe user-facing error/success handling
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for endpoints and startup validation
- possibly additional test projects if service/unit tests belong elsewhere
- `docs/integrations/fortnox.md`
- `docs/runbooks/fortnox-integration.md`

Before coding, locate:
- existing finance integration models
- any existing OAuth abstractions
- token storage entities/tables/migrations
- encryption helpers / data protection usage
- company membership / admin authorization patterns
- background sync job entrypoints that consume Fortnox tokens

# Implementation plan
1. **Discover existing Fortnox and integration architecture**
   - Search for:
     - `Fortnox`
     - `OAuth`
     - `FinanceIntegrations`
     - `IntegrationConnection`
     - token encryption/protection services
     - company-scoped integration entities
   - Reuse existing patterns over inventing new ones.
   - Identify where connect/callback routes should live and how authenticated company admin access is enforced.

2. **Add strongly typed Fortnox options with fail-fast validation**
   - Create or update a `FortnoxOptions`/similar class bound from:
     - `FinanceIntegrations:Fortnox`
   - Include at minimum:
     - `Enabled`
     - `ClientId`
     - `ClientSecret`
     - `RedirectUri`
     - `TokenUrl`
     - `ApiBaseUrl`
     - authorization endpoint if separately configured or derivable
   - Add startup validation using `ValidateOnStart()` or equivalent.
   - Validation rule:
     - if Fortnox integration is enabled, startup must fail when any required setting is missing/empty.
   - Ensure validation error messages are explicit but do not echo secrets.

3. **Define/confirm service contract**
   - Add or update an interface such as `IFortnoxOAuthService`.
   - Include methods along these lines:
     - `BuildAuthorizationUrlAsync(...)`
     - `HandleCallbackAsync(...)`
     - `ExchangeCodeAsync(...)`
     - `RefreshAccessTokenAsync(...)`
     - `MarkNeedsReconnectAsync(...)`
   - Keep signatures tenant-aware and user-aware:
     - company id
     - authenticated user id
     - cancellation token
   - Return structured result types, not raw Fortnox payloads.

4. **Implement secure OAuth state and nonce roundtrip**
   - Generate cryptographically secure `state` and `nonce`.
   - Persist or protect roundtrip context containing:
     - company id
     - authenticated user id
     - issued at / expiry
     - nonce
     - optional reconnect flag
   - Prefer existing secure server-side ephemeral storage if present; otherwise use ASP.NET Core Data Protection for tamper-proof payloads plus short expiry.
   - Callback must reject:
     - missing state
     - invalid state
     - expired state
     - mismatched authenticated user
     - mismatched company scope
   - Do not trust callback query values alone for tenant/user context.

5. **Implement authorization URL generation**
   - Build the real Fortnox authorization URL using configured endpoint and required query parameters.
   - Include:
     - client id
     - redirect uri
     - response type/code as required
     - state
     - scope if required by Fortnox app registration
     - nonce if supported/needed in your roundtrip model
   - Expose this through `/finance/integrations/fortnox/connect`.
   - Ensure only a company admin can initiate the flow.
   - If reconnecting an existing revoked/expired connection, preserve that intent in the roundtrip state.

6. **Implement callback endpoint and validation**
   - Wire `/finance/integrations/fortnox/callback`.
   - Validate current authenticated user and company membership/role.
   - Parse callback parameters safely.
   - Handle provider error responses with safe user-facing messaging.
   - On success path:
     - validate state/nonce/context
     - exchange authorization code for tokens
     - persist encrypted tokens
     - update connection status to connected/active
     - store metadata like expiry time, scopes, provider subject/tenant if available
   - On invalid/expired code:
     - return safe user-facing error
     - do not expose Fortnox response body
     - log only sanitized metadata

7. **Implement code exchange against Fortnox token endpoint**
   - Use `HttpClientFactory`.
   - POST to configured token endpoint using Fortnox-required content type and auth scheme.
   - Map response into internal DTOs.
   - Never log:
     - access token
     - refresh token
     - authorization code
     - client secret
   - Add defensive handling for:
     - non-success status codes
     - malformed payloads
     - timeout/transient failures
   - Surface internal typed exceptions/results so UI can show safe messages.

8. **Encrypt token storage at rest**
   - Reuse existing encryption abstraction if available.
   - If none exists, implement a focused infrastructure service using ASP.NET Core Data Protection or the project’s established secret protection mechanism.
   - Persist only encrypted token values.
   - Ensure any entity/DTO exposed to UI omits token fields entirely.
   - Review logs and exception messages to ensure tokens are never included.
   - If there are audit events, record connection state changes without secrets.

9. **Implement automatic access token refresh**
   - Add logic used before Fortnox API calls:
     - if access token is expired or near expiry, refresh via `/oauth-v1/token`
   - Persist new encrypted tokens and updated expiry.
   - Handle concurrency safely if multiple jobs refresh simultaneously:
     - optimistic concurrency, lock, or compare-and-swap pattern if existing
   - Ensure refresh failures are classified:
     - transient failure => retryable
     - invalid refresh token / revoked => mark connection `NeedsReconnect` or `Revoked`
   - Do not crash background sync jobs; they should fail gracefully and record status.

10. **Reconnect and revoked handling**
   - Define/confirm connection statuses such as:
     - `Connected`
     - `NeedsReconnect`
     - `Revoked`
     - `Error`
   - On invalid refresh token or provider revocation signal:
     - transition status appropriately
     - preserve enough metadata for operator troubleshooting
     - avoid repeated noisy retries
   - Ensure reconnect flow can be initiated again from the connect endpoint.

11. **Protect logs and UI**
   - Review all logging in the Fortnox path.
   - Log only:
     - company id
     - user id
     - correlation id
     - provider status code/category
     - sanitized error category
   - Never log raw provider payloads if they may contain secrets.
   - Ensure UI models/pages only show connection status, timestamps, and safe messages.

12. **Add/update tests**
   - Unit/integration tests for:
     - startup fails when enabled and required config missing
     - startup succeeds when disabled even if values absent
     - connect endpoint requires authenticated company admin
     - authorization URL contains expected parameters and protected state
     - callback rejects invalid/missing/expired state
     - callback rejects mismatched user/company scope
     - callback handles provider invalid code safely
     - successful code exchange stores encrypted tokens
     - refresh path updates tokens when expired
     - invalid refresh token marks `NeedsReconnect` or `Revoked`
     - background sync caller receives safe failure result instead of crash
     - no token values appear in returned view models / serialized responses
   - Prefer deterministic tests with mocked `HttpMessageHandler`.

13. **Write documentation**
   - `docs/integrations/fortnox.md`
     - Fortnox app registration steps
     - required scopes
     - callback URL setup
     - local development config via user-secrets
     - expected connect/callback flow
   - `docs/runbooks/fortnox-integration.md`
     - production secret storage guidance
     - operational troubleshooting
     - reconnect/revoked handling
     - token refresh behavior
     - safe logging expectations
     - common failure modes and remediation

14. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - API/Web for endpoints and auth
     - Application for orchestration/use cases
     - Infrastructure for HTTP, encryption, persistence
     - Domain for statuses/rules
   - Keep tenant isolation explicit in every query/update.
   - Use CQRS-lite patterns if already established.

# Validation steps
1. **Codebase inspection**
   - Confirm all new code follows existing naming, DI, and module patterns.
   - Verify no duplicate OAuth/token abstractions were introduced unnecessarily.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Manual config validation**
   - Enable Fortnox integration with one or more required settings missing.
   - Verify application startup fails fast with a clear validation error.
   - Disable Fortnox integration and verify startup succeeds without Fortnox secrets.

5. **Manual connect flow**
   - Sign in as a company admin.
   - Navigate to `/finance/integrations/fortnox/connect`.
   - Verify redirect to Fortnox authorization endpoint with expected parameters.
   - Complete callback through `/finance/integrations/fortnox/callback`.
   - Verify connection status becomes connected and no tokens are shown in UI.

6. **Manual callback failure cases**
   - Tamper with `state`.
   - Reuse an expired/old callback.
   - Use a callback under a different authenticated user/company.
   - Verify safe user-facing error and no Fortnox payload leakage.

7. **Manual refresh behavior**
   - Seed an expired access token with valid refresh token.
   - Trigger a Fortnox API call path.
   - Verify automatic refresh occurs and updated encrypted tokens are persisted.
   - Seed invalid refresh token.
   - Verify connection transitions to `NeedsReconnect` or `Revoked` and background job path does not crash the worker.

8. **Security review**
   - Search logs and code for accidental token exposure.
   - Confirm:
     - no access token in logs
     - no refresh token in logs
     - no client secret in logs
     - no token fields in API/UI DTOs
   - Review exception handling for raw provider payload leakage.

9. **Docs review**
   - Confirm both required docs files exist and are actionable for local and production setup.

# Risks and follow-ups
- **Unknown existing schema/entity shape**: token storage and integration status models may already exist; adapt rather than creating parallel models.
- **Fortnox OAuth specifics**: verify exact authorization endpoint, required scopes, auth headers, and token response fields from current Fortnox docs before finalizing implementation.
- **Encryption consistency**: if the app already has a secret protection abstraction, using a new one would create maintenance risk; prefer reuse.
- **Concurrency during refresh**: multiple workers may race to refresh the same token; add minimal concurrency protection if sync jobs already run in parallel.
- **Role enforcement ambiguity**: confirm the existing authorization policy for “company admin” and use that exact policy.
- **Reconnect semantics**: if domain already distinguishes