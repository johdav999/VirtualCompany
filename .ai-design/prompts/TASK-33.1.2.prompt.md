# Goal
Implement TASK-33.1.2 for US-33.1 by adding Fortnox OAuth and connection lifecycle application services that are tenant-scoped, secure, and production-safe.

The implementation must cover:
- OAuth start
- OAuth callback
- reconnect
- token refresh during API usage
- disconnect

The implementation must enforce tenant-bound, single-use OAuth state validation and secure encrypted token persistence, while fitting the existing modular monolith and shared-schema multi-tenant architecture.

# Scope
In scope:
- Add or complete domain/application/infrastructure support for Fortnox connection persistence.
- Add EF Core entities/configurations and migrations for:
  - tenant-scoped Fortnox connections
  - encrypted token storage metadata
  - OAuth state records
  - sync history
  - external record references
- Implement application services for:
  - starting OAuth
  - handling OAuth callback
  - reconnect flow
  - refreshing expired access tokens
  - disconnecting a Fortnox connection
- Enforce tenant/user-bound OAuth state creation, expiry, single use, and mismatch rejection.
- Encrypt access and refresh tokens before persistence.
- Ensure tokens never appear in API responses, exceptions, structured logs, or audit payloads.
- Mark connection status as needing attention on refresh failure and persist only a safe error summary.
- Record an audit event on disconnect.
- Add/update API endpoints or handlers that expose these application services.

Out of scope unless required by existing code structure:
- Full Fortnox data sync implementation beyond persistence/history/reference scaffolding.
- UI work beyond any minimal API contract changes.
- Background sync orchestration beyond what is necessary for token lifecycle support.
- Returning raw token details to clients.

# Files to touch
Inspect the solution first and then update the actual matching files. Expected areas:

- `src/VirtualCompany.Domain/**`
  - Add Fortnox integration domain entities/value objects/enums if missing.
  - Add statuses for connected / needs_attention / disconnected or equivalent.
- `src/VirtualCompany.Application/**`
  - Add commands/queries/handlers or service interfaces for:
    - OAuth start
    - OAuth callback
    - reconnect
    - refresh
    - disconnect
  - Add DTOs that never expose token values.
  - Add validation and safe error/result models.
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - DbContext updates
  - migrations
  - Fortnox OAuth/token client adapter
  - token encryption service / data protection integration
  - repository implementations
  - logging redaction safeguards where relevant
- `src/VirtualCompany.Api/**`
  - Endpoints/controllers for start, callback, reconnect, disconnect
  - tenant/user context wiring
  - safe HTTP 400 behavior for invalid callback state
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests for OAuth start/callback/disconnect
  - invalid state scenarios
- `tests/**` or matching application/infrastructure test projects if present
  - token encryption persistence tests
  - refresh success/failure tests
  - tenant mismatch tests
  - single-use state tests

Also inspect:
- existing tenant resolution abstractions
- audit event persistence
- existing integration module patterns
- any existing encryption or secrets abstraction
- any existing outbox/audit conventions

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect how the solution currently models:
     - tenant-scoped entities
     - application commands/handlers
     - API endpoints
     - audit events
     - EF Core migrations
     - encryption/data protection
     - external integrations
   - Reuse naming and folder conventions already present in the repo.

2. **Model the Fortnox persistence layer**
   - Add tenant-owned entities/tables for at least:
     - `FortnoxConnections`
     - `FortnoxOAuthStates`
     - `FortnoxSyncHistory`
     - `FortnoxExternalRecordReferences`
   - Include encrypted token storage metadata on the connection record or a dedicated owned structure, such as:
     - encrypted access token ciphertext
     - encrypted refresh token ciphertext
     - encryption key/version metadata
     - token expiry timestamps
     - scopes if available
     - last refresh attempt/result summary
   - Ensure every tenant-owned record includes the tenant/company id and appropriate indexes.
   - Add uniqueness constraints where appropriate, e.g. one active Fortnox connection per tenant.
   - Add migration(s) for PostgreSQL via EF Core.

3. **Implement secure token encryption**
   - Introduce or reuse an infrastructure encryption abstraction, e.g. `ITokenEncryptionService`.
   - Encrypt access and refresh tokens before persistence.
   - Store only ciphertext plus safe metadata.
   - Never log plaintext tokens.
   - Ensure API DTOs and exceptions never include token values.
   - If structured logging is used, avoid logging request/response payloads from Fortnox token endpoints unless fully redacted.

4. **Implement OAuth state generation and persistence**
   - Add an application service/command for OAuth start.
   - Generate a cryptographically secure random state value.
   - Persist a record containing:
     - state hash or state value per existing security conventions
     - tenant/company id
     - user id
     - expiry timestamp
     - created timestamp
     - used timestamp/null
     - optional reconnect intent / return URL metadata if needed
   - Prefer storing a hash of the outbound state if practical; if not, store the raw state securely and compare exactly.
   - Build the real Fortnox authorization URL using configured client settings and redirect URI.
   - Return/redirect to the Fortnox authorization URL.
   - Ensure state is single-use.

5. **Implement OAuth callback validation**
   - Add callback handler/service that accepts code and state.
   - Reject with HTTP 400 and persist no tokens when:
     - state is missing
     - state record is not found
     - state is expired
     - state is already used
     - tenant/user binding does not match the current callback context
   - Mark the state as used only once validation passes and the flow is committed safely.
   - Exchange authorization code with Fortnox for tokens through an infrastructure client.
   - Encrypt and persist the token set to the tenant’s Fortnox connection record.
   - Update connection status to connected/healthy.
   - Avoid race conditions on state reuse by using transactional update semantics or concurrency control.

6. **Implement reconnect flow**
   - Reconnect should reuse the OAuth start behavior for an existing tenant connection that needs reauthorization.
   - Ensure reconnect still creates a fresh single-use state bound to the current tenant and user.
   - Preserve existing connection identity/history as appropriate, but replace token material only after successful callback.
   - Do not allow reconnect to bypass state validation.

7. **Implement automatic token refresh**
   - Add a token provider/service used by Fortnox API callers:
     - if access token is valid, decrypt and use it
     - if expired or near expiry, use refresh token to obtain a new token set
   - Persist the new encrypted token set atomically.
   - Handle concurrent refresh attempts safely to avoid duplicate refresh races.
   - Add a small expiry skew buffer to avoid using nearly expired tokens.
   - Ensure refresh logic is encapsulated in application/infrastructure services, not controllers.

8. **Handle refresh failures safely**
   - On refresh failure:
     - mark connection status as needing attention
     - persist a safe error summary only
     - do not persist raw provider payloads if they may contain secrets
     - do not log token values
   - Return a safe domain/application error that callers can translate into appropriate API behavior.
   - Keep the failure auditable without exposing secrets.

9. **Implement disconnect**
   - Add disconnect command/service for the tenant.
   - Remove or deactivate active Fortnox connection state for the tenant according to existing domain conventions.
   - Ensure active token material is no longer usable after disconnect.
   - Record an audit event with tenant, actor, action, target, and outcome.
   - Do not include token values in audit data.

10. **Expose API endpoints**
   - Add or update Fortnox integration endpoints, likely under an integrations route.
   - Expected endpoints:
     - start/connect
     - callback
     - reconnect
     - disconnect
   - Callback must return HTTP 400 for invalid state cases.
   - Ensure tenant and user context are resolved consistently with the rest of the API.
   - Keep responses minimal and safe.

11. **Add tests**
   - Add automated tests for:
     - migration model includes required tables
     - OAuth start persists a state bound to tenant and user with expiry
     - callback rejects:
       - missing state
       - unknown state
       - expired state
       - reused state
       - tenant mismatch
     - callback success persists encrypted tokens, not plaintext
     - refresh success updates encrypted token set
     - refresh failure marks connection as needing attention and stores safe summary
     - disconnect removes/deactivates connection and writes audit event
   - Add assertions that logs/responses do not contain token values where testable.

12. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries:
     - API = transport only
     - Application = orchestration/use cases
     - Domain = entities/rules
     - Infrastructure = EF Core, crypto, Fortnox HTTP client
   - Keep all tenant isolation enforced in queries and writes.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify EF Core migration output:
   - Confirm migration creates tenant-scoped Fortnox tables for:
     - connections
     - OAuth states
     - sync history
     - external record references
   - Confirm indexes/constraints support tenant isolation and single active connection semantics.

4. Manual/API validation:
   - Start OAuth for tenant A/user X:
     - confirm state record persisted with expiry and tenant/user binding
     - confirm redirect URL points to Fortnox auth endpoint with generated state
   - Callback with missing state:
     - expect HTTP 400
     - confirm no tokens persisted
   - Callback with expired state:
     - expect HTTP 400
     - confirm no tokens persisted
   - Callback with reused state:
     - expect HTTP 400
     - confirm no duplicate token persistence
   - Callback with tenant mismatch:
     - expect HTTP 400
     - confirm no tokens persisted
   - Successful callback:
     - confirm connection created/updated
     - confirm token columns contain encrypted values, not plaintext
   - Trigger refresh path with expired token:
     - confirm refresh token flow runs
     - confirm new encrypted token set persisted
   - Simulate refresh failure:
     - confirm status becomes needing attention
     - confirm only safe error summary stored
   - Disconnect:
     - confirm active connection state removed/deactivated
     - confirm audit event written

5. Security validation:
   - Search code/log statements for token exposure risks.
   - Confirm no API response DTO includes access token or refresh token.
   - Confirm exception messages do not include provider token payloads.

# Risks and follow-ups
- **Tenant context in callback**
  - OAuth callbacks can be tricky if tenant context is not naturally present on the callback request.
  - Ensure the persisted state is the source of truth for tenant/user binding and do not trust querystring tenant identifiers alone.

- **State replay/race conditions**
  - Reused callback requests may race.
  - Use transactional updates or optimistic concurrency to guarantee single-use semantics.

- **Refresh concurrency**
  - Multiple requests may try to refresh simultaneously.
  - Add locking/concurrency handling to avoid token thrash or stale overwrites.

- **Encryption key management**
  - If no encryption abstraction exists, choose one compatible with production key rotation.
  - Follow-up may be needed for key rotation/versioning if only a basic implementation is added now.

- **Fortnox provider nuances**
  - Token exchange/refresh payloads and expiry semantics may differ from assumptions.
  - Verify against Fortnox API docs and keep provider-specific mapping isolated in infrastructure.

- **Audit consistency**
  - Disconnect explicitly requires audit logging.
  - Consider whether connect/reconnect/refresh-failure should also emit business audit events as a follow-up if not already covered elsewhere.

- **Safe error summaries**
  - Be careful not to persist raw provider responses.
  - Normalize provider failures into sanitized summaries.

- **Migration/table naming drift**
  - Match existing naming conventions in the repo rather than inventing new ones.
  - If integration tables already exist partially, extend them instead of duplicating structures.