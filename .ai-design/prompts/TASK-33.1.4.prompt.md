# Goal
Implement backlog task **TASK-33.1.4 — Expose tenant-scoped connection status and lifecycle endpoints with permission checks for company admins** for story **US-33.1 Implement Fortnox connection persistence, OAuth security, and token lifecycle management**.

Deliver a production-ready vertical slice in the existing .NET solution that:

- Persists Fortnox tenant-scoped connection and OAuth lifecycle data in PostgreSQL via EF Core migrations.
- Exposes secure API endpoints for:
  - OAuth start
  - OAuth callback
  - connection status
  - disconnect
- Enforces tenant scoping on every read/write.
- Restricts lifecycle/status endpoints to authorized **company admins** only using existing policy-based authorization patterns.
- Encrypts access and refresh tokens before persistence and never returns token values in API responses or logs.
- Supports automatic token refresh during downstream API usage through an application/infrastructure token service.
- Marks broken connections as needing attention with a safe error summary when refresh fails.
- Records an audit event on disconnect.

Work within the existing modular monolith / clean architecture style. Prefer CQRS-lite application handlers and thin API endpoints/controllers. Do not introduce a separate microservice.

# Scope
In scope:

1. **Persistence**
   - Add EF Core entities/configuration/migrations for tenant-scoped Fortnox integration data:
     - connection records
     - encrypted token storage metadata
     - OAuth state records
     - sync history
     - external record references
   - Ensure all tenant-owned tables include `company_id` and appropriate indexes/constraints.

2. **OAuth lifecycle**
   - Start endpoint:
     - validates tenant/user context
     - creates a single-use state value
     - binds it to tenant and user
     - persists expiry and usage flags
     - redirects to Fortnox authorization URL
   - Callback endpoint:
     - validates state
     - rejects missing/expired/reused/tenant-mismatched state with HTTP 400
     - exchanges code for tokens
     - encrypts tokens before persistence
     - stores/update tenant connection state
     - never logs or returns token values

3. **Connection management endpoints**
   - Tenant-scoped status endpoint for company admins.
   - Disconnect endpoint for company admins.
   - Disconnect removes active Fortnox connection state for the tenant and writes an audit event.

4. **Token lifecycle**
   - Add token encryption abstraction and implementation.
   - Add token refresh service used by Fortnox API client/integration service.
   - On expired access token, refresh automatically using stored refresh token.
   - Persist refreshed encrypted token set.
   - On refresh failure:
     - mark connection status as needing attention
     - store safe error summary
     - avoid token leakage in logs/responses

5. **Authorization**
   - Reuse existing tenant resolution and membership/role authorization mechanisms.
   - Ensure only company admins (or stricter existing role if already established) can access these endpoints.

6. **Tests**
   - Add/extend API/application/integration tests for:
     - permission checks
     - tenant scoping
     - OAuth state validation
     - token encryption persistence behavior
     - disconnect audit event
     - refresh failure status transitions

Out of scope unless required by existing code patterns:
- Full Fortnox data sync implementation beyond minimal sync history/reference persistence support.
- UI/Blazor pages.
- Mobile changes.
- Broad refactors unrelated to Fortnox integration.
- Returning decrypted tokens anywhere.

# Files to touch
Inspect the solution first and adapt to actual conventions. Likely files/folders to touch include:

- `src/VirtualCompany.Domain/**`
  - Add Fortnox domain entities/value objects/enums:
    - `FortnoxConnection`
    - `FortnoxOAuthState`
    - `FortnoxSyncHistory`
    - `FortnoxExternalRecordReference`
    - token metadata/value objects
    - connection status enum
- `src/VirtualCompany.Application/**`
  - Commands/queries/handlers for:
    - start OAuth
    - handle callback
    - get connection status
    - disconnect connection
  - Interfaces for:
    - token encryption
    - Fortnox OAuth client
    - token refresh service
    - tenant/user context access
    - audit writer if not already present
- `src/VirtualCompany.Infrastructure/**`
  - EF Core DbContext updates
  - EntityTypeConfiguration classes
  - Migrations
  - encryption implementation
  - Fortnox OAuth/token client
  - Fortnox connection repository/service
  - refresh logic
  - safe logging helpers if needed
- `src/VirtualCompany.Api/**`
  - Endpoints/controllers for Fortnox lifecycle/status
  - authorization attributes/policies
  - DI registration
  - request/response DTOs
- `src/VirtualCompany.Shared/**`
  - Shared contracts only if this repo uses shared DTOs/constants there
- `tests/VirtualCompany.Api.Tests/**`
  - Endpoint and authorization tests
- Potentially:
  - `README.md` or docs if integration setup docs are maintained
  - appsettings samples for Fortnox OAuth config
  - existing audit module files if audit events are persisted through a shared service

Also inspect:
- existing auth/tenant patterns
- existing audit event persistence
- existing EF migration organization
- any existing integration module or connector conventions

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect:
     - API style (controllers vs minimal APIs)
     - CQRS/mediator usage
     - DbContext and entity configuration layout
     - tenant resolution approach
     - membership role/policy authorization
     - audit event persistence
     - migration naming conventions
   - Follow existing naming and folder structure exactly.

2. **Model the Fortnox persistence layer**
   - Add tenant-scoped entities with explicit lifecycle fields.
   - Minimum recommended schema:
     - `fortnox_connections`
       - `id`
       - `company_id`
       - `status` (`connected`, `needs_attention`, `disconnected`, etc.)
       - `fortnox_company_id` or equivalent external tenant identifier if available
       - encrypted access token ciphertext
       - encrypted refresh token ciphertext
       - encryption metadata (key id/version/algorithm metadata, not plaintext secrets)
       - token expiry timestamps
       - safe error summary
       - connected/disconnected timestamps
       - created/updated timestamps
     - `fortnox_oauth_states`
       - `id`
       - `company_id`
       - `user_id`
       - state hash or opaque identifier
       - expires_at
       - consumed_at / used flag
       - created_at
     - `fortnox_sync_history`
       - `id`
       - `company_id`
       - sync type/direction/status
       - started_at/completed_at
       - safe summary/error summary
     - `fortnox_external_record_references`
       - `id`
       - `company_id`
       - external entity type/id
       - internal entity type/id
       - created_at
   - Prefer storing a **hash of OAuth state** rather than raw state if practical, while still enabling validation.
   - Add unique/index constraints to support:
     - efficient lookup by `company_id`
     - state uniqueness
     - one active connection per tenant if intended by domain
   - Keep all tables tenant-scoped.

3. **Create EF Core configurations and migration**
   - Add entity configurations with:
     - table names
     - required fields
     - max lengths
     - indexes
     - foreign keys where applicable
   - Generate a migration that creates all required tables.
   - Verify migration is PostgreSQL-compatible.

4. **Implement token encryption abstraction**
   - Add an application-facing interface such as:
     - `ITokenEncryptionService`
   - Infrastructure implementation should:
     - encrypt access/refresh tokens before persistence
     - return ciphertext + metadata
     - support decrypt for outbound Fortnox API usage only
   - Do not log plaintext inputs/outputs.
   - If the solution already has data protection/crypto abstractions, reuse them.
   - Ensure API DTOs and logs never expose token values.

5. **Implement OAuth start flow**
   - Add command/handler + API endpoint.
   - Behavior:
     - require authenticated user
     - require tenant/company context
     - require company admin authorization
     - generate cryptographically secure single-use state
     - bind to `company_id` and `user_id`
     - persist with expiry
     - build Fortnox authorize URL from configuration
     - redirect to Fortnox
   - Return redirect response only; do not expose internal token/state internals beyond what is necessary.

6. **Implement OAuth callback flow**
   - Add callback endpoint/handler.
   - Validate:
     - state present
     - matching persisted record exists
     - not expired
     - not already consumed
     - tenant/user binding rules are satisfied
   - On any invalid state:
     - return HTTP 400
     - do not persist tokens
     - mark state safely if needed
   - On valid state:
     - exchange authorization code for token set via Fortnox OAuth client
     - encrypt tokens
     - upsert tenant Fortnox connection
     - mark state consumed atomically
   - Use transaction boundaries to avoid partial persistence.
   - Ensure no plaintext token logging.

7. **Implement connection status endpoint**
   - Add tenant-scoped query endpoint for company admins.
   - Response should include only safe fields, e.g.:
     - provider name
     - status
     - connected/disconnected timestamps
     - token expiry timestamp if acceptable
     - needs-attention flag
     - safe error summary
     - last successful refresh/sync timestamps if available
   - Never include access token, refresh token, ciphertext, or sensitive secrets.

8. **Implement disconnect endpoint**
   - Add command/handler + API endpoint.
   - Require company admin authorization.
   - Remove active Fortnox connection state for the tenant:
     - either hard delete sensitive token-bearing record(s) or clear token fields and mark disconnected, depending on existing domain conventions and acceptance criteria
   - Record an audit event with tenant, actor, action, target, outcome.
   - Ensure disconnect is idempotent and safe if no active connection exists.

9. **Implement automatic token refresh**
   - Add a Fortnox token service used by downstream Fortnox API calls.
   - Behavior:
     - when access token is expired or near expiry, use stored refresh token
     - exchange for new token set
     - encrypt and persist updated tokens/expiry
   - On refresh failure:
     - mark connection `needs_attention`
     - persist safe error summary
     - do not leak token values in logs/exceptions/API responses
   - If there is already a Fortnox API client abstraction, integrate there rather than duplicating logic.
   - Consider concurrency protection so multiple simultaneous refreshes do not race.

10. **Authorization and tenant enforcement**
   - Reuse existing policy-based authorization.
   - Ensure all handlers query by `company_id` from resolved tenant context, never from untrusted client input alone.
   - If route includes tenant/company identifiers, validate they match resolved context.
   - Return forbidden/not found according to existing API conventions.

11. **Audit integration**
   - On disconnect, persist a business audit event.
   - If callback success/failure or refresh failure are already considered auditable in the existing architecture, add audit events there too only if consistent with current patterns.
   - Keep rationale/error summaries concise and safe.

12. **Logging and safety pass**
   - Review all logs/exceptions in the new code.
   - Never log:
     - authorization code
     - access token
     - refresh token
     - decrypted token payloads
   - Safe logs may include:
     - company id
     - user id
     - connection status transitions
     - correlation ids
     - sanitized error categories/messages

13. **Testing**
   - Add tests covering at minimum:
     - migration model includes required tables
     - OAuth start persists single-use state with tenant/user/expiry
     - callback returns 400 for:
       - missing state
       - expired state
       - reused state
       - tenant mismatch
     - invalid callback persists no tokens
     - tokens are stored encrypted, not plaintext
     - status endpoint omits token values
     - non-admin cannot access status/disconnect/start if policy requires admin
     - disconnect clears active connection and writes audit event
     - refresh success updates encrypted token set
     - refresh failure marks `needs_attention` and stores safe error summary
   - Prefer integration-style API tests where feasible.

14. **Keep implementation cohesive**
   - Avoid speculative abstractions beyond what this task needs.
   - Keep Fortnox-specific code in the integration module boundaries.
   - Preserve clean separation:
     - API = transport/auth
     - Application = use cases
     - Infrastructure = EF, crypto, Fortnox HTTP

# Validation steps
Run and verify locally using the repo’s actual commands and test setup.

1. **Build**
   - `dotnet build`

2. **Run tests**
   - `dotnet test`

3. **Migration verification**
   - Confirm EF migration was added and compiles.
   - Inspect generated migration for:
     - all required Fortnox tables
     - `company_id` on tenant-owned tables
     - indexes/constraints
   - If the repo supports migration application in tests/dev, apply it and verify schema creation.

4. **Manual/API verification**
   - Start the API and verify:
     - OAuth start endpoint requires authentication and admin permission
     - start endpoint creates persisted state and redirects
     - callback with invalid state returns HTTP 400
     - callback with valid mocked token exchange persists encrypted tokens only
     - status endpoint returns safe metadata only
     - disconnect removes/clears active connection and records audit event

5. **Security verification**
   - Search code/logging for accidental token exposure.
   - Confirm no response DTO includes token/ciphertext fields.
   - Confirm refresh failure path stores only sanitized error summary.

6. **Tenant isolation verification**
   - Verify cross-tenant access is denied or not found per existing conventions.
   - Verify state validation is bound to the correct tenant/user.

7. **Refresh lifecycle verification**
   - With a mocked/