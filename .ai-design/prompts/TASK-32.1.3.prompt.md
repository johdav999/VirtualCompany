# Goal
Implement backlog task **TASK-32.1.3 — Build encrypted FortnoxTokenStore with refresh coordination, redacted logging, and tenant-scoped persistence** for story **US-32.1 Fortnox configuration, OAuth connect flow, and secure token lifecycle**.

Deliver a production-ready Fortnox OAuth/token lifecycle implementation in the existing **.NET modular monolith** that:

- fails fast on startup when Fortnox integration is enabled but required config is missing
- supports real Fortnox OAuth connect and callback endpoints
- validates authorization state, nonce, authenticated user, and company scope
- stores access/refresh tokens encrypted at rest with strict tenant scoping
- never exposes tokens in UI or logs
- refreshes expired access tokens automatically before API calls
- coordinates refresh to avoid concurrent refresh races
- degrades safely when refresh tokens are invalid by marking the connection as needing reconnect/revoked
- adds setup and runbook documentation

Work within the current architecture and code conventions already present in the repo. Prefer incremental changes over broad refactors.

# Scope
In scope:

- Configuration model and startup validation for:
  - `FinanceIntegrations:Fortnox:ClientId`
  - `ClientSecret`
  - `RedirectUri`
  - `TokenUrl`
  - `ApiBaseUrl`
- Fortnox OAuth connect endpoint:
  - `/finance/integrations/fortnox/connect`
- Fortnox OAuth callback endpoint:
  - `/finance/integrations/fortnox/callback`
- Secure authorization state handling:
  - state
  - nonce
  - authenticated user binding
  - company/tenant binding
  - expiration
- Tenant-scoped persistence for Fortnox connection/token records
- Encryption at rest for access token and refresh token
- Redacted logging and safe error handling
- Automatic token refresh via `/oauth-v1/token`
- Refresh coordination/locking to prevent duplicate concurrent refreshes per tenant/company connection
- Safe handling of invalid refresh tokens:
  - mark status as `NeedsReconnect` or `Revoked`
  - do not crash background jobs
- Documentation:
  - `/docs/integrations/fortnox.md`
  - `/docs/runbooks/fortnox-integration.md`

Out of scope unless required to complete acceptance criteria:

- broad redesign of the integrations module
- unrelated UI polish
- adding new external integrations
- full sync implementation beyond what is needed to consume a valid Fortnox access token
- exposing tokens anywhere in web/mobile/API responses

# Files to touch
Inspect the repo first, then update the closest existing Fortnox/integration/auth/configuration files. Expected areas:

- `src/VirtualCompany.Api/**`
  - startup/program registration
  - endpoint/controller definitions
  - auth/user/company context access
  - exception mapping
- `src/VirtualCompany.Application/**`
  - commands/queries/services for connect/callback/token refresh
  - DTOs and result models
  - validation
- `src/VirtualCompany.Domain/**`
  - Fortnox connection aggregate/entity/value objects/status enums
  - domain rules for reconnect/revoked states
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence
  - Fortnox HTTP client
  - encrypted token store implementation
  - data protection/encryption service
  - distributed/local refresh coordination lock
  - logging redaction helpers
  - options binding and startup validation
- `src/VirtualCompany.Web/**`
  - connect initiation UI entry point if route is web-backed
  - safe user-facing callback result/error page
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- potentially `tests/**` in other test projects if present for application/infrastructure
- `docs/integrations/fortnox.md`
- `docs/runbooks/fortnox-integration.md`

Also update migrations if persistence schema changes are required. Place new migration files in the repo’s established migration location after inspecting current conventions.

# Implementation plan
1. **Inspect existing integration patterns before coding**
   - Find any existing:
     - finance integration abstractions
     - OAuth flows
     - token persistence
     - encryption helpers
     - tenant/company context services
     - background job patterns
     - EF entities/configurations
   - Reuse existing abstractions and naming where possible.
   - Do not invent parallel patterns if a house style already exists.

2. **Add Fortnox options with fail-fast startup validation**
   - Create or extend a strongly typed options class for `FinanceIntegrations:Fortnox`.
   - Include at minimum:
     - `Enabled`
     - `ClientId`
     - `ClientSecret`
     - `RedirectUri`
     - `AuthorizationUrl` if needed by current design
     - `TokenUrl`
     - `ApiBaseUrl`
     - scopes if applicable
   - Register options validation using `ValidateOnStart()`.
   - Validation rule:
     - if Fortnox integration is enabled, required values must be non-empty
     - startup must fail with a clear configuration error
   - Ensure logs/errors do not print secrets.

3. **Model tenant-scoped Fortnox connection persistence**
   - Add or extend a persistence model keyed by company/tenant, not global user-only scope.
   - Persist:
     - company id
     - provider name (`Fortnox`)
     - connection status
     - encrypted access token
     - encrypted refresh token
     - token type if returned
     - scopes if returned
     - access token expiry
     - refresh failure metadata
     - connected user id / last connected by
     - timestamps
     - revoked/needs reconnect reason codes
   - Add optimistic concurrency token if useful.
   - Ensure all queries are company-scoped.

4. **Implement encrypted token storage**
   - Build `FortnoxTokenStore` in infrastructure behind an application abstraction.
   - Encrypt access and refresh tokens before persistence.
   - Use the project’s existing secret protection mechanism if present; otherwise use ASP.NET Core Data Protection or an equivalent app-level encryption abstraction suitable for DB-at-rest protection.
   - Never return raw tokens from query/view models.
   - Keep decryption limited to the infrastructure/service layer that needs to call Fortnox.

5. **Implement redacted logging**
   - Audit all Fortnox-related logs.
   - Never log:
     - access token
     - refresh token
     - authorization code
     - raw Fortnox error payloads
   - Add helper methods for redaction/safe logging if needed.
   - Log only safe metadata:
     - company id
     - user id
     - correlation id
     - status transitions
     - HTTP status code categories
     - safe reason codes
   - For callback/token failures, surface generic user-safe messages while keeping internal logs sanitized.

6. **Implement connect flow**
   - Add endpoint/handler for `/finance/integrations/fortnox/connect`.
   - Require authenticated user and company admin authorization.
   - Generate and persist short-lived authorization session data containing:
     - state
     - nonce
     - user id
     - company id
     - created/expiry time
   - Store this server-side in a tenant-safe persistence/cache mechanism already used by the app; if none exists, add a small persistence table or secure distributed cache entry.
   - Redirect to Fortnox authorization endpoint with required parameters.
   - Do not trust callback query values without matching stored authorization session.

7. **Implement callback flow**
   - Add endpoint/handler for `/finance/integrations/fortnox/callback`.
   - Validate:
     - authenticated user if callback requires signed-in session
     - state exists and is unexpired
     - nonce matches if used in round-trip/session binding
     - stored user id matches current user
     - stored company id matches current company context
   - Handle invalid/expired/missing authorization code safely:
     - no raw Fortnox payloads in response
     - return a safe user-facing error page/message
   - Exchange authorization code against Fortnox token endpoint.
   - Persist encrypted tokens and connection metadata on success.
   - Clear/consume the one-time authorization session after successful or terminal handling.

8. **Implement Fortnox token client**
   - Add infrastructure client for token exchange and refresh against Fortnox endpoints.
   - Use typed `HttpClient`.
   - Support:
     - authorization code exchange
     - refresh token grant to `/oauth-v1/token`
   - Parse only required fields.
   - On non-success:
     - map to sanitized internal error types
     - avoid bubbling raw response bodies to UI/logs
   - Respect cancellation tokens and timeouts.

9. **Implement automatic refresh before API calls**
   - Add a service used by Fortnox API consumers that:
     - loads the company-scoped connection
     - checks expiry with a small skew buffer
     - refreshes if expired or near expiry
     - returns a valid access token only to the infrastructure API client layer
   - Ensure downstream Fortnox API calls use this service rather than reading tokens directly.

10. **Implement refresh coordination**
    - Prevent concurrent refreshes for the same company/provider connection.
    - Prefer existing distributed lock/coordination infrastructure if present; otherwise implement a minimal per-connection coordination strategy appropriate to the app architecture.
    - Expected behavior:
      - one request/job performs refresh
      - others wait briefly and re-read updated token state
      - avoid duplicate refresh calls and token stomping
    - Combine lock + persistence re-read + optimistic concurrency where appropriate.

11. **Handle invalid refresh tokens safely**
    - If refresh fails with invalid_grant or equivalent terminal auth failure:
      - mark connection status as `NeedsReconnect` or `Revoked`
      - clear or preserve encrypted tokens according to current domain policy, but do not leave the system pretending the connection is healthy
      - return a typed failure to callers
    - Background jobs must not crash the worker process:
      - catch typed auth failures
      - log sanitized warning/error
      - mark sync/job result as failed/degraded gracefully
    - Distinguish transient HTTP failures from terminal auth failures.

12. **Expose safe connection status to UI/application**
    - Add or update a query/model that exposes only safe fields:
      - connected/not connected
      - connected at
      - expires at
      - status
      - reconnect required
      - last error category/reason code
   - Never expose token values.

13. **Add tests**
   - Unit/integration tests for:
     - startup validation fails when enabled config is incomplete
     - connect endpoint requires auth/admin/company scope
     - callback rejects invalid state/user/company/expired session
     - callback success persists encrypted tokens
     - logs do not contain token/code values
     - refresh occurs automatically when token expired
     - concurrent refresh results in a single refresh call
     - invalid refresh token marks connection as reconnect/revoked and does not crash background execution path
   - Prefer deterministic tests with fake clock and fake HTTP handlers where possible.

14. **Add documentation**
   - `docs/integrations/fortnox.md`
     - Fortnox app registration
     - required scopes
     - callback URL
     - local development setup
     - user-secrets example
     - config keys
     - how connect flow works
   - `docs/runbooks/fortnox-integration.md`
     - operational checks
     - secret rotation
     - diagnosing failed callbacks
     - diagnosing refresh failures
     - reconnect/revoked handling
     - logging/redaction expectations
     - production secret storage guidance

15. **Keep implementation aligned with architecture**
   - Maintain clean boundaries:
     - API/web endpoints thin
     - application orchestrates use cases
     - infrastructure handles HTTP, encryption, persistence
     - domain owns statuses/rules
   - Preserve tenant isolation in every query and command path.

# Validation steps
1. **Repo inspection**
   - Search for existing Fortnox/integration/OAuth/token patterns.
   - Confirm migration and testing conventions before adding files.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Targeted validation scenarios**
   - Startup with `FinanceIntegrations:Fortnox:Enabled=true` and missing required values should fail immediately.
   - Startup with complete config should succeed.
   - Authenticated company admin hitting `/finance/integrations/fortnox/connect` should be redirected to Fortnox auth URL.
   - Callback with:
     - missing state
     - expired state
     - wrong user
     - wrong company
     - invalid code
     should return safe error behavior without secret/raw payload leakage.
   - Successful callback should persist encrypted token fields and healthy connection status.
   - Inspect persisted data to confirm tokens are not stored plaintext.
   - Trigger API call with expired token and verify refresh happens automatically.
   - Simulate concurrent requests/jobs for same company and verify only one refresh request is sent upstream.
   - Simulate invalid refresh token and verify:
     - connection status transitions correctly
     - caller gets typed safe failure
     - background path does not crash process
   - Inspect logs from happy and failure paths to confirm no tokens, auth codes, or raw Fortnox payloads appear.

5. **Documentation validation**
   - Ensure both docs files exist and are internally consistent with actual config keys/routes implemented.

# Risks and follow-ups
- **Unknown existing patterns**: the repo may already have integration abstractions or encryption helpers. Reuse them instead of duplicating behavior.
- **Callback auth/session assumptions**: if the current app does not guarantee an authenticated session on callback, bind state strongly to stored user/company context and handle missing session safely.
- **Encryption key management**: Data Protection is acceptable for app-level encryption, but production key persistence must be documented clearly.
- **Refresh race conditions across nodes**: in multi-instance deployments, in-memory locking alone is insufficient. Prefer distributed coordination if available.
- **Fortnox-specific response nuances**: map provider errors to sanitized internal reason codes; do not overfit to undocumented payloads.
- **Background job integration**: if sync jobs are not yet implemented, add typed auth failure handling at the service boundary so future jobs inherit safe behavior.
- **Migration impact**: schema changes for token persistence