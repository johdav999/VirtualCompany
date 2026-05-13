# Goal
Implement backlog task **TASK-33.2.3 — Create idempotent sync orchestration service with retry policy, audit logging, and sync history persistence** for story **US-33.2 Build production Fortnox sync pipeline and normalized finance data mapping**.

The coding agent must deliver a production-ready, tenant-safe Fortnox manual sync orchestration flow in the existing .NET solution that:

- Calls real Fortnox production API endpoints for:
  - company information
  - customers
  - suppliers
  - invoices
  - supplier invoices
  - vouchers
  - accounts
  - articles
  - projects
- Normalizes imported data into existing internal finance contracts/entities
- Persists tenant-scoped external references for every imported record
- Is idempotent across repeated sync runs
- Applies configurable retry behavior for transient failures
- Persists sync history with status, counts, duration, and safe error summary
- Produces business audit logging
- Translates Fortnox API failures into plain-English user-facing messages while keeping detailed diagnostics in server logs
- Enforces tenant isolation and permission checks on all reads/writes

Do not redesign the architecture. Fit the implementation into the existing modular monolith and current project boundaries.

# Scope
In scope:

- Add or complete a **manual sync orchestration application service** for Fortnox
- Add supporting domain/application/infrastructure pieces for:
  - sync execution coordination
  - retry policy
  - sync history persistence
  - external reference persistence
  - audit event creation
  - Fortnox error translation
  - tenant/authorization enforcement
- Wire the orchestration into the existing manual sync endpoint
- Ensure imported records map into existing normalized internal finance models/contracts rather than storing raw provider payloads as the source of truth
- Ensure repeated syncs update existing records/mappings idempotently
- Add tests covering orchestration behavior, idempotency, retry handling, tenant isolation, and error translation

Out of scope unless required by existing code patterns:

- New UI beyond what is necessary to expose existing endpoint behavior
- Background scheduled syncs
- Webhook ingestion
- Storing raw Fortnox payloads as canonical records
- Broad refactors unrelated to Fortnox sync
- New integration providers

Implementation constraints:

- Use existing finance/internal contracts if already present; extend minimally only where necessary
- Use existing multi-tenant patterns and authorization policies
- Use existing logging, audit, persistence, and HTTP client conventions where available
- Prefer typed clients and application services over controller-heavy logic
- Keep retry logic targeted to transient/network/rate-limit/server failures, not business validation or authorization failures
- Keep user-facing errors safe and plain English
- Preserve detailed diagnostics only in internal logs/history metadata where appropriate

# Files to touch
Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to create or modify:

1. API
- Manual Fortnox sync endpoint/controller/minimal API registration
- Request/response DTOs for manual sync result
- Authorization attributes/policies
- Error response mapping if centralized

2. Application
- Fortnox sync orchestration service
- Command/query models for manual sync
- Retry policy abstraction/config
- Sync result model with per-entity counts/status/duration
- Error translation service/interface
- Tenant-aware authorization checks
- Audit event emission
- Mapping coordination into internal finance contracts
- Idempotent upsert coordination using external references

3. Domain
- Sync history entity/value objects/enums
- External reference entity/value objects/enums if not already present
- Sync status/result types
- Provider/entity type constants or enums
- Domain rules for tenant scoping and idempotent mapping if domain-owned

4. Infrastructure
- Fortnox API client integration using production endpoints
- Repository implementations for sync history and external references
- EF Core configurations/migrations for any new persistence
- Retry implementation
- Logging around Fortnox failures
- Transaction boundaries/unit of work integration
- Possibly pagination helpers for Fortnox list endpoints

5. Tests
- Endpoint authorization/tenant isolation tests
- Orchestration service tests
- Idempotency tests
- Retry behavior tests
- Error translation tests
- Sync history persistence tests
- External reference persistence tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
for migration/build conventions if schema changes are needed.

# Implementation plan
1. **Discover existing finance and integration structure**
   - Search for:
     - Fortnox-related code
     - finance entities/contracts
     - external reference or integration mapping tables
     - audit event infrastructure
     - tenant resolution and authorization patterns
     - retry/background execution helpers
   - Identify the existing manual sync endpoint and current sync flow, if any.
   - Reuse existing naming and module boundaries.

2. **Define the orchestration contract**
   - Introduce or complete an application-layer service such as `IFortnoxSyncOrchestrator` with a method for manual tenant-scoped sync.
   - Input should include:
     - active tenant/company id
     - requesting user/actor context
     - integration/provider connection context if applicable
     - optional correlation/idempotency metadata if existing patterns support it
   - Output should include:
     - overall status
     - per-entity imported/updated/skipped/failed counts
     - started/completed timestamps
     - duration
     - safe error summary if failed/partial

3. **Implement sync history persistence**
   - Add a sync history model/table if not already present.
   - Persist at minimum:
     - tenant/company id
     - provider name = Fortnox
     - trigger type = manual
     - status
     - started at
     - completed at
     - duration
     - per-entity counts
     - safe error summary
     - correlation id if available
     - actor/requested by
   - Record final status for success, partial success, and failure.
   - Ensure history is tenant-scoped and query-safe.

4. **Implement or complete external reference persistence**
   - Ensure every imported normalized record has a tenant-scoped external reference containing:
     - provider name
     - external id
     - internal id
     - entity type
     - last synced timestamp
   - Add uniqueness enforcement to prevent duplicates for the same tenant/provider/entity type/external id.
   - If an external reference already exists, update mapping and `last_synced_timestamp` rather than creating a duplicate.
   - If internal records already exist by business key and current architecture supports safe matching, link them carefully; otherwise prefer external-reference-driven idempotency.

5. **Build idempotent entity sync flow**
   - For each Fortnox entity type:
     - fetch records from production API
     - map to normalized internal finance contract/entity
     - upsert internal record
     - upsert external reference
   - Required entity types:
     - company information
     - customers
     - suppliers
     - invoices
     - supplier invoices
     - vouchers
     - accounts
     - articles
     - projects
   - Repeated sync of unchanged records must not create duplicate internal records.
   - Prefer deterministic upsert logic:
     - lookup external reference by tenant + provider + entity type + external id
     - if found, update existing internal record
     - if not found, create internal record and external reference in one transaction
   - Do not store provider payloads as the system of record. If raw payload snapshots already exist for diagnostics, keep them non-canonical and minimal.

6. **Implement Fortnox API access using production endpoints**
   - Use or add a typed Fortnox client in Infrastructure.
   - Ensure it calls real production endpoints for all required resources.
   - Handle pagination if Fortnox endpoints require it.
   - Respect existing auth/token storage patterns.
   - Keep provider-specific DTOs in infrastructure/application boundaries, not domain core.

7. **Add retry policy**
   - Implement configurable retry behavior for transient failures only, such as:
     - network timeouts
     - 429/rate limiting
     - 5xx upstream failures
     - temporary transport errors
   - Do not retry:
     - 400 validation errors
     - 401/403 auth failures unless existing token refresh flow explicitly supports it
     - tenant authorization failures
     - mapping/business rule failures that are deterministic
   - Retry policy should be centralized and testable.
   - Log each retry attempt with correlation id, tenant id, entity type, and reason.
   - Persist final sync history status after retries are exhausted.

8. **Translate Fortnox errors safely**
   - Add a translator that converts provider/HTTP failures into plain-English user-facing messages, for example:
     - authentication expired
     - insufficient permissions in Fortnox
     - rate limited, try again shortly
     - Fortnox temporarily unavailable
     - requested data could not be retrieved
   - Preserve detailed diagnostics in structured server logs.
   - Sync history should store only a safe summary, not secrets or raw provider internals.

9. **Add audit logging**
   - Emit business audit events for:
     - manual sync started
     - manual sync completed
     - manual sync failed
   - Include:
     - actor
     - tenant/company
     - provider
     - outcome
     - rationale/summary
     - affected entity counts
   - Keep technical logs separate from business audit events.

10. **Enforce tenant isolation and permissions**
   - Ensure the manual sync endpoint requires appropriate authorization.
   - Ensure all repository queries and writes are scoped to the active tenant/company.
   - Never read or update external references, sync history, or finance records across tenants.
   - Add tests proving cross-tenant access is forbidden or not found according to existing conventions.

11. **Use transactional boundaries correctly**
   - For each record upsert, ensure internal record + external reference remain consistent.
   - For overall sync history:
     - create history row at start
     - update status/counts/duration at completion/failure
   - Avoid one giant transaction across the entire sync if that conflicts with current architecture or risks lock contention; prefer resilient per-batch/per-record consistency plus final history update.

12. **Return a useful manual sync response**
   - Endpoint response should expose safe operational details only:
     - status
     - counts by entity
     - duration
     - safe message
   - Do not expose raw Fortnox payloads, secrets, or internal stack traces.

13. **Add/adjust persistence schema**
   - If tables do not already exist, add migrations for:
     - sync history
     - external references
     - indexes/unique constraints needed for idempotency and tenant scoping
   - Follow repository migration conventions in the workspace.
   - Ensure indexes support:
     - tenant/company id
     - provider
     - entity type
     - external id
     - sync history lookup by tenant + started/completed timestamps

14. **Testing**
   - Add focused tests for:
     - successful manual sync across required entity types
     - idempotent repeated sync of unchanged data
     - external reference creation/update
     - retry on transient Fortnox failures
     - no retry on permanent failures
     - sync history final status/counts/duration/error summary
     - plain-English user-facing error translation
     - tenant isolation and permission enforcement
   - Prefer integration-style tests where practical for endpoint + persistence behavior.

# Validation steps
Run these after implementation:

1. Restore/build/tests
- `dotnet build`
- `dotnet test`

2. If migrations were added, verify they are included correctly and application startup still works.

3. Validate manual sync happy path
- Trigger the manual sync endpoint for a tenant with valid Fortnox credentials
- Confirm:
  - all required entity categories are fetched
  - normalized internal finance records are written
  - external references are created
  - sync history row is created and completed successfully
  - audit events are written

4. Validate idempotency
- Run the same manual sync twice against unchanged Fortnox data
- Confirm:
  - no duplicate internal records
  - no duplicate external references
  - existing mappings are updated in place
  - `last synced timestamp` advances appropriately

5. Validate retry behavior
- Simulate transient Fortnox failures in tests or via mocked client
- Confirm retries occur according to configuration
- Confirm final success or failure is persisted in sync history

6. Validate permanent failure behavior
- Simulate non-retryable Fortnox errors
- Confirm no useless retries occur
- Confirm user-facing message is plain English
- Confirm detailed diagnostics are only in logs

7. Validate tenant isolation
- Attempt sync or data access across tenants in tests
- Confirm forbidden/not found behavior matches existing conventions
- Confirm no cross-tenant writes occur

8. Validate safe responses
- Confirm endpoint and sync history safe summary do not expose:
  - access tokens
  - raw provider payloads
  - stack traces
  - sensitive diagnostics

# Risks and follow-ups
- **Unknown existing finance model shape**: internal finance contracts may already exist with partial mappings. Reuse them rather than inventing parallel models.
- **Unknown existing Fortnox integration state**: there may already be DTOs/clients/endpoints. Extend carefully to avoid duplicate integration paths.
- **Migration risk**: if external references or sync history tables already exist under different names, align with current schema instead of duplicating concepts.
- **Idempotency edge cases**: if some entities lack stable external ids or have composite keys, document the chosen uniqueness strategy in code comments/tests.
- **Large sync volume**: full manual sync may require pagination and batching; avoid loading everything into memory if not necessary.
- **Partial success semantics**: if one entity type fails after others succeed, persist a clear final status and counts. Do not hide partial imports.
- **Token/auth refresh behavior**: if Fortnox token refresh is handled elsewhere, integrate with it; do not build a conflicting auth flow.
- **Audit consistency**: ensure business audit events summarize outcomes without duplicating verbose technical logs.
- **