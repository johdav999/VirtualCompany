# Goal
Implement backlog task **TASK-33.1.3 — Integrate encrypted token storage and log redaction for Fortnox credentials and callback payloads** for story **US-33.1 Implement Fortnox connection persistence, OAuth security, and token lifecycle management**.

The coding agent should deliver a production-ready, tenant-safe Fortnox OAuth persistence flow in the existing .NET solution, with:
- tenant-scoped EF Core persistence for connection state, encrypted token metadata, OAuth state, sync history, and external record references
- secure OAuth start/callback handling with single-use state validation
- encryption of access/refresh tokens before persistence
- automatic token refresh with encrypted token rotation
- safe failure handling that marks the connection as needing attention
- strict redaction so secrets and callback payload token values never appear in logs, API responses, exceptions, or audit payloads
- disconnect behavior that clears active tenant connection state and records an audit event

Use the existing architecture and coding patterns already present in the repo. Prefer extending current Integration, Audit, Infrastructure, and API layers rather than inventing parallel patterns.

# Scope
In scope:
- Add/extend domain entities and EF Core mappings for Fortnox integration persistence
- Create EF Core migrations for required tenant-scoped tables
- Implement OAuth state creation, persistence, expiry, single-use semantics, and tenant/user binding
- Implement OAuth callback validation and token exchange persistence
- Encrypt token values before database persistence using an application service in Infrastructure
- Ensure API/query DTOs never expose token values
- Implement token refresh flow for Fortnox API calls
- Persist refreshed encrypted token set
- On refresh failure, mark connection status appropriately and store only safe error summaries
- Add logging redaction/safe logging around OAuth callback payloads and token operations
- Implement disconnect flow and audit event creation
- Add/extend automated tests

Out of scope unless required by existing code structure:
- Full Fortnox data sync implementation beyond minimal sync history/reference persistence support
- UI work beyond any API contract changes already consumed by web/mobile
- Reworking unrelated integration abstractions
- Introducing external secret managers unless already wired; use app-level encryption service backed by current configuration patterns

Non-negotiable acceptance constraints:
- No plaintext access tokens or refresh tokens in DB
- No token values in logs, API responses, problem details, audit events, or test snapshots
- Callback must reject missing/expired/reused/tenant-mismatched state with HTTP 400 and persist no tokens
- Disconnect must remove active connection state for the tenant and record an audit event

# Files to touch
Inspect first, then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Infrastructure`
- `src/VirtualCompany.Api`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to create or modify:
- Domain entities/value objects/enums for:
  - Fortnox connection
  - encrypted token metadata / token envelope
  - OAuth state record
  - sync history
  - external record reference
  - connection status / sync status enums
- Application contracts/services/commands/queries for:
  - OAuth start
  - OAuth callback
  - disconnect
  - token refresh orchestration
  - Fortnox API client abstraction
  - audit event creation
- Infrastructure persistence:
  - `DbContext`
  - entity type configurations
  - repositories
  - migrations
- Infrastructure security:
  - token encryption service
  - redaction helpers / logging sanitization
  - Fortnox HTTP client / delegating handler / token refresh support
- API endpoints/controllers/minimal APIs for:
  - OAuth start
  - OAuth callback
  - disconnect
- Tests:
  - integration/API tests for OAuth state validation
  - persistence tests for encrypted storage
  - refresh flow tests
  - log redaction tests if feasible in current test setup

Also inspect:
- existing tenant resolution and authorization patterns
- existing audit event persistence
- existing integration module structure
- existing exception handling / logging middleware
- any existing OAuth or external integration code
- any existing encryption/data protection utilities

# Implementation plan
1. **Discover existing patterns before coding**
   - Find current module structure for integrations, audit events, tenant scoping, and API endpoint style.
   - Identify the main EF Core `DbContext`, migration assembly, and entity configuration conventions.
   - Search for any existing Fortnox code, OAuth abstractions, token handling, or external connector patterns.
   - Search for logging middleware, correlation ID handling, and any redaction/sensitive-data filtering already in place.
   - Reuse existing naming and layering conventions exactly.

2. **Design the persistence model to satisfy acceptance criteria**
   Add tenant-scoped tables/entities for at least:
   - **FortnoxConnections**
     - id
     - company/tenant id
     - user linkage if needed for created/connected by
     - status
     - authorization metadata
     - encrypted token fields or FK to token metadata
     - token expiry timestamps
     - safe error summary
     - connected/disconnected timestamps
     - created/updated timestamps
   - **FortnoxTokenStorageMetadata** or equivalent embedded/owned structure
     - encryption algorithm/version
     - key identifier
     - ciphertext
     - nonce/iv
     - auth tag if applicable
     - created/rotated timestamps
     - never plaintext
   - **FortnoxOAuthStates**
     - id
     - tenant id
     - user id
     - state value/hash
     - expires at
     - consumed at
     - created at
     - optional redirect context
   - **FortnoxSyncHistory**
     - id
     - tenant id
     - connection id
     - sync type
     - status
     - started/completed timestamps
     - safe summary/error summary
   - **FortnoxExternalRecordReferences**
     - id
     - tenant id
     - connection id
     - external entity type
     - external id
     - internal entity type
     - internal id
     - timestamps

   Requirements:
   - Every tenant-owned table must include tenant/company scoping and indexes supporting isolation.
   - Add uniqueness/index constraints where appropriate, especially for active connection per tenant and state lookup.
   - If storing raw state value is avoidable, prefer storing a hash of the generated state and compare hashed values on callback.

3. **Create EF Core mappings and migration**
   - Add entity configurations with explicit table names, keys, lengths, required fields, indexes, and concurrency handling if used in repo.
   - Generate a migration that creates all required tables.
   - Ensure migration is compatible with PostgreSQL provider conventions already used in the solution.
   - Verify migration does not break existing schema assumptions.

4. **Implement token encryption service**
   - Add an Infrastructure service interface + implementation for encrypting/decrypting token values.
   - Prefer authenticated encryption and include metadata needed for future rotation.
   - Wire configuration through existing options/config patterns.
   - Ensure service returns a structured encrypted payload object, not raw strings scattered through code.
   - Never log plaintext input/output.
   - If the repo already uses ASP.NET Core Data Protection or another crypto abstraction, reuse it only if it supports the acceptance criteria and metadata persistence cleanly.

5. **Implement OAuth start flow**
   - Add/extend endpoint/service for starting Fortnox OAuth.
   - Resolve current tenant and authenticated user from existing access context.
   - Generate a cryptographically strong single-use state value.
   - Persist state bound to tenant and user with expiry.
   - Return/redirect to the real Fortnox authorization URL with the generated state.
   - Ensure any logs only include safe metadata such as tenant id, user id, state record id, and expiry — never the raw state if current logging policy treats it as sensitive.
   - If multiple outstanding states should be prevented for same tenant/user, either invalidate prior unused states or document/implement the intended single-use behavior consistently.

6. **Implement OAuth callback flow**
   - Add/extend callback endpoint/service.
   - Validate:
     - state present
     - matching persisted record
     - not expired
     - not already consumed/reused
     - tenant/user binding matches expected context or callback correlation strategy
   - On any validation failure:
     - return HTTP 400
     - do not persist tokens
     - mark no connection as active
     - log only safe reason codes
   - On success:
     - exchange authorization code for token set via Fortnox client
     - encrypt access token and refresh token before persistence
     - persist/update Fortnox connection
     - mark OAuth state consumed atomically to prevent replay
   - Use transaction boundaries so state consumption and token persistence are consistent.

7. **Implement safe Fortnox connection read models**
   - Ensure any API response DTOs for connection status expose only safe fields, e.g.:
     - connected/disconnected
     - status
     - token expiry timestamps
     - last refresh attempt
     - safe error summary
     - connected by / connected at if appropriate
   - Explicitly exclude token values, ciphertext blobs, refresh token metadata not needed by clients, and raw callback payloads.

8. **Implement automatic token refresh**
   - In the Fortnox API client path, detect expired or near-expiry access tokens before making API calls.
   - Decrypt stored refresh token only within the secure service boundary.
   - Request a new token set from Fortnox.
   - Encrypt and persist the new token set and updated expiry metadata.
   - Ensure refresh is safe under concurrency:
     - either optimistic concurrency, transaction, or per-connection lock pattern
     - avoid duplicate refresh races if feasible within current architecture
   - Keep all logs redacted.

9. **Handle refresh failure safely**
   - If refresh fails due to invalid_grant or equivalent permanent auth issue:
     - mark connection status as `NeedsAttention` or repo-equivalent
     - persist a safe error summary only
     - do not persist raw provider response if it may contain secrets
   - Ensure downstream API calls fail safely with a non-secret-bearing application error.
   - Add audit/business event if consistent with existing patterns.

10. **Implement disconnect**
    - Add/extend disconnect endpoint/command for current tenant.
    - Remove or deactivate active Fortnox connection state for the tenant.
    - Ensure encrypted token material is no longer active/usable after disconnect.
    - Preserve auditability as required by domain policy.
    - Record an audit event with safe metadata only.

11. **Implement log redaction and safe error handling**
    - Review all logging around:
      - OAuth start
      - callback query/body payloads
      - token exchange
      - token refresh
      - disconnect
      - HTTP client request/response logging
    - Prevent token values, authorization codes, refresh tokens, bearer headers, and sensitive callback payload fields from appearing in:
      - structured logs
      - exception messages
      - problem details
      - audit events
    - If HTTP logging is enabled globally, add exclusions/redaction for Fortnox auth endpoints and headers.
    - Prefer logging reason codes and record ids over raw payloads.

12. **Add tests**
    Add or extend tests to cover:
    - migration/model creation if current test strategy supports schema verification
    - OAuth start persists state with tenant/user/expiry and returns redirect
    - callback rejects:
      - missing state
      - unknown state
      - expired state
      - reused state
      - tenant mismatch
    - rejected callback persists no tokens
    - successful callback stores encrypted token values, not plaintext
    - API responses never include token values
    - refresh flow updates encrypted token set when access token expired
    - refresh failure marks connection as needing attention with safe error summary
    - disconnect removes active connection state and creates audit event
    - logs do not contain token values or raw callback secrets where test harness allows log capture

13. **Keep implementation aligned with clean architecture**
    - Domain: statuses, entities, invariants
    - Application: use cases, validation, orchestration
    - Infrastructure: EF, crypto, HTTP clients, provider adapters
    - API: thin endpoints/controllers only
    - Avoid leaking Infrastructure crypto details into API contracts

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are part of normal workflow, create/apply/verify:
   - generate the EF Core migration in the correct startup/project combination used by the repo
   - verify the migration creates the required tenant-scoped tables and indexes
   - if a local DB/test container setup exists, apply migration and confirm schema

4. Manually verify code paths by reading and, if possible, exercising tests for:
   - OAuth start creates persisted single-use state with expiry
   - callback invalid-state cases return HTTP 400
   - successful callback persists encrypted token material only
   - no plaintext token values in entities returned by queries/DTOs
   - refresh path rotates encrypted tokens
   - refresh failure sets connection status to needing attention
   - disconnect clears active connection and writes audit event

5. Perform targeted repo searches before finalizing:
   - search for `access_token`, `refresh_token`, `authorization_code`, `Bearer`, `Fortnox`
   - confirm no new logging statements or DTOs expose secrets
   - confirm no exception messages interpolate token values or callback payloads

6. In the final implementation summary, include:
   - files changed
   - migration name
   - key design choices for encryption and redaction
   - any assumptions made about existing Fortnox/provider contracts

# Risks and follow-ups
- **Existing architecture mismatch:** The repo may already have partial integration/OAuth abstractions. Reuse them rather than duplicating patterns.
- **Crypto key management:** If no secure key configuration exists yet, implement a minimal app-config-backed encryption service now, but note follow-up to move to managed key storage/HSM/Key Vault for production hardening.
- **State binding ambiguity:** If callback endpoint lacks authenticated user context, bind state strongly to tenant/user at issuance and validate via persisted state plus callback routing/context. Document the chosen strategy clearly.
- **Concurrency during refresh:** Multiple simultaneous API calls may race token refresh. Implement the safest