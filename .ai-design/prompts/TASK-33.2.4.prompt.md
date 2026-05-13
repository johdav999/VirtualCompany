# Goal

Implement backlog task **TASK-33.2.4 — Add tenant-scoped sync now and sync history endpoints with plain-English error translation** for story **US-33.2 Build production Fortnox sync pipeline and normalized finance data mapping**.

Deliver a coding change in the existing **.NET modular monolith** that adds secure, tenant-scoped API endpoints to:

1. Trigger a **manual Fortnox sync now** for the active tenant.
2. Query **sync history** for the active tenant.
3. Fetch real Fortnox production data for:
   - company information
   - customers
   - suppliers
   - invoices
   - supplier invoices
   - vouchers
   - accounts
   - articles
   - projects
4. Normalize imported data into existing internal finance contracts/entities.
5. Persist tenant-scoped external references/mappings for idempotent re-sync.
6. Record sync execution history with status, counts, duration, retries, and safe error summaries.
7. Translate Fortnox API errors into plain-English user-facing messages while preserving detailed diagnostics in server logs.

Do not redesign the architecture. Fit the implementation into the current solution structure and existing patterns.

# Scope

In scope:

- Add or complete **API endpoints** for:
  - `POST .../sync/now`
  - `GET .../sync/history`
  - optionally `GET .../sync/history/{id}` if needed by existing API conventions
- Ensure all reads/writes are **tenant-scoped** and **permission-checked**
- Use **production Fortnox API endpoints**, not mock/sample payloads
- Implement orchestration/application flow for manual sync
- Normalize provider data into internal finance models/contracts
- Persist **external reference mappings** with:
  - tenant/company id
  - provider name
  - external id
  - internal id
  - entity type
  - last synced timestamp
- Make sync **idempotent** for unchanged records
- Record **sync history** including:
  - final status
  - per-entity counts
  - duration
  - retry attempts / retry outcome
  - safe error summary
- Add **plain-English Fortnox error translation**
- Preserve detailed diagnostics in structured logs
- Add/update tests

Out of scope unless required by existing code paths:

- New UI pages beyond minimal API contract support
- Replacing existing finance domain contracts
- Storing raw Fortnox payloads as system of record
- Broad refactors unrelated to sync/history/error translation
- New integration providers beyond Fortnox

Implementation constraints:

- Follow shared-schema multi-tenancy with `company_id`/tenant enforcement
- Keep integration adapters as adapters, not systems of record
- Use existing background/retry infrastructure if already present; otherwise implement the smallest production-suitable retry handling consistent with current architecture
- Prefer CQRS-lite patterns already used in the solution
- Keep user-facing errors safe and concise
- Keep internal logs detailed with correlation/tenant context where available

# Files to touch

Inspect the solution first and then update the most relevant files in these areas.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to touch:

1. **API layer**
   - Finance/Fortnox controller or endpoint registration
   - Request/response DTOs for sync now and sync history
   - Authorization attributes/policies
   - Exception-to-response mapping if needed

2. **Application layer**
   - Manual sync command + handler
   - Sync history query + handler
   - DTOs/view models for sync result/history
   - Permission/tenant access checks
   - Error translation abstraction/interface
   - Retry policy coordination if handled here

3. **Domain layer**
   - Sync history aggregate/entity/value objects if not already present
   - External reference mapping entity/model if not already present
   - Finance normalization contracts updates if needed
   - Status/count/error summary models

4. **Infrastructure layer**
   - Fortnox API client using production endpoints
   - Provider DTOs and mapping code
   - Repository implementations for:
     - sync history
     - external references
     - normalized finance writes
   - Logging and diagnostics
   - Retry policy implementation/integration
   - EF Core configurations/migrations if schema changes are required

5. **Database**
   - New migration(s) for:
     - sync history table(s)
     - external reference table(s)
     - indexes/uniqueness constraints for idempotency and tenant scoping
   - Update any migration archive/readme only if this repo’s conventions require it

6. **Tests**
   - API tests for authorization and tenant isolation
   - Application/integration tests for idempotent sync behavior
   - Error translation tests
   - Sync history persistence tests

Do not assume exact filenames. Discover existing finance/Fortnox/integration patterns first and extend them consistently.

# Implementation plan

1. **Discover existing implementation and conventions**
   - Search for:
     - Fortnox integration code
     - finance entities/contracts
     - tenant resolution/access services
     - authorization policies
     - retry/background job patterns
     - external reference or integration mapping concepts
     - sync history or job execution history concepts
   - Identify whether there is already:
     - a Fortnox client
     - finance normalization pipeline
     - integration credential storage
     - a generic sync execution model
   - Reuse existing abstractions where possible.

2. **Define endpoint contracts**
   - Add a tenant-scoped manual sync endpoint, likely under an integration/finance/Fortnox route.
   - Add a tenant-scoped sync history endpoint returning paged or bounded recent history.
   - Response shape should include enough detail for acceptance criteria:
     - sync id
     - status
     - started/completed timestamps
     - duration
     - entity counts
     - safe error summary
     - retry summary if available
   - Ensure endpoints require authenticated membership and appropriate finance/integration permission.

3. **Implement tenant and permission enforcement**
   - Resolve active tenant/company from the existing request context.
   - Verify the caller has permission to trigger sync and view sync history.
   - Ensure all repository queries and writes filter by tenant/company id.
   - Return forbidden/not found according to existing API conventions.

4. **Implement/complete sync orchestration**
   - Create an application command/handler for manual sync.
   - Flow should:
     1. validate tenant and Fortnox connection/configuration
     2. create sync history record with started status
     3. fetch Fortnox data from production endpoints for all required entity types
     4. normalize and upsert into internal finance contracts/entities
     5. upsert external references
     6. update sync history with counts, duration, final status
     7. on failure, apply retry policy and record final outcome
   - Keep orchestration deterministic and testable.

5. **Use production Fortnox endpoints**
   - Implement or verify real API calls for:
     - company information
     - customers
     - suppliers
     - invoices
     - supplier invoices
     - vouchers
     - accounts
     - articles
     - projects
   - Handle pagination if Fortnox requires it.
   - Respect configured auth/token handling already present in the repo.
   - Do not persist raw provider payloads as the source of truth.

6. **Normalize into internal finance contracts**
   - Map each Fortnox entity into the existing internal finance model.
   - If internal contracts already exist, use them directly.
   - If there is an anti-corruption layer, keep provider DTOs isolated in infrastructure and normalized models in application/domain.
   - Persist only normalized records needed by the system of record.
   - Avoid leaking provider-specific shapes into core domain models.

7. **Persist external references for idempotency**
   - Add or reuse a tenant-scoped external reference table/entity with fields:
     - company/tenant id
     - provider name
     - entity type
     - external id
     - internal id
     - last synced timestamp
   - Add a uniqueness constraint on `(company_id, provider_name, entity_type, external_id)`.
   - During sync:
     - if mapping exists, update the existing internal record rather than creating a duplicate
     - if no mapping exists, create internal record and mapping
   - Ensure repeated sync of unchanged records is idempotent.

8. **Implement sync history persistence**
   - Add or reuse sync history storage with:
     - sync id
     - company/tenant id
     - provider
     - trigger type/manual
     - status
     - started at
     - completed at
     - duration
     - per-entity counts
     - retry attempts
     - safe error summary
     - optional correlation id
   - Consider a child table or JSON column for entity counts if that matches existing conventions.
   - Ensure history queries are tenant-scoped and ordered by most recent first.

9. **Add retry handling**
   - Use existing retry policy infrastructure if available.
   - Distinguish transient Fortnox/API/network failures from permanent validation/business failures.
   - Record retry attempts and final status in sync history.
   - Avoid duplicate writes by making upserts idempotent and using external references.

10. **Add plain-English error translation**
    - Introduce a translator component that maps Fortnox/API failures to safe user-facing messages, for example:
      - authentication/authorization issues
      - rate limiting
      - validation errors
      - missing/disabled integration
      - upstream service unavailable
      - unknown provider error
    - Return translated messages in API responses and sync history safe error summary.
    - Log detailed diagnostics separately:
      - HTTP status
      - provider error code/body
      - correlation/request ids
      - stack trace where applicable
      - tenant context
   - Do not expose raw provider payloads, secrets, or internal exception details to clients.

11. **Schema and persistence updates**
    - Add EF Core entities/configurations and migration(s) if needed.
    - Add indexes for:
      - tenant-scoped history lookup
      - external reference uniqueness
      - internal id reverse lookup if needed
    - Keep schema naming consistent with existing conventions.

12. **Testing**
    - Add tests covering:
      - authorized tenant can trigger sync
      - unauthorized or wrong-tenant access is blocked
      - sync history only returns active tenant records
      - repeated sync does not create duplicate internal records
      - external references are created/updated correctly
      - sync history records counts/status/duration/error summary
      - Fortnox errors are translated to plain English
      - detailed diagnostics are not leaked in API responses
      - retry behavior records final outcome correctly
   - Prefer existing test patterns in `tests/VirtualCompany.Api.Tests`.

13. **Keep implementation production-safe**
    - Use cancellation tokens
    - Use structured logging
    - Preserve correlation ids if the app already supports them
    - Avoid long controller logic; keep orchestration in application services/handlers
    - Keep provider-specific code in infrastructure

# Validation steps

1. **Codebase inspection**
   - Search the repo for existing Fortnox, finance sync, tenant access, and authorization patterns.
   - Confirm final touched files align with current architecture.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Manual/API validation**
   - Verify manual sync endpoint:
     - authenticated user with correct tenant + permission succeeds
     - user without permission is rejected
     - user cannot trigger sync for another tenant
   - Verify sync history endpoint:
     - returns only active tenant records
     - includes status, counts, duration, and safe error summary

5. **Behavior validation**
   - Run sync twice against the same Fortnox dataset and confirm:
     - no duplicate normalized internal records
     - external references are reused/updated
     - sync history shows separate executions with correct outcomes

6. **Error validation**
   - Simulate/provider-stub Fortnox failures and confirm:
     - API/client sees plain-English safe message
     - logs retain detailed diagnostics
     - sync history stores safe error summary only

7. **Persistence validation**
   - Confirm migration applies cleanly if added
   - Confirm uniqueness/index constraints enforce idempotent mapping behavior

8. **Acceptance criteria checklist**
   - Explicitly verify each acceptance criterion is satisfied before finishing.

# Risks and follow-ups

- **Unknown existing finance model shape**
  - Risk: internal finance contracts may be incomplete for one or more Fortnox entities.
  - Follow-up: if gaps exist, implement the minimum required normalization without broad domain redesign.

- **Existing integration credential/token handling may be incomplete**
  - Risk: production endpoint access may depend on unfinished auth plumbing.
  - Follow-up: wire into existing secure token storage patterns only; do not hardcode secrets.

- **Retry ownership may be ambiguous**
  - Risk: retries may belong in background workers rather than request path.
  - Follow-up: if current architecture already has job execution infrastructure, prefer enqueueing a sync job and returning accepted/result metadata; otherwise implement the smallest compliant synchronous orchestration that still records retries/history.

- **Large sync volume/pagination**
  - Risk: fetching all entity types may be slow or timeout in a single request.
  - Follow-up: if needed, trigger a background job from the manual endpoint while still creating immediate sync history and exposing status via history endpoints.

- **Idempotency edge cases**
  - Risk: matching only on external id may not be enough if internal uniqueness rules differ by entity.
  - Follow-up: preserve provider/entity-type scoped mapping and use upsert semantics carefully per entity.

- **Error translation coverage**
  - Risk: Fortnox may return varied error shapes.
  - Follow-up: implement a robust default fallback message and log unmapped cases for future refinement.

- **Schema naming mismatch**
  - Risk: this repo may already have integration sync tables under different names.
  - Follow-up: extend existing tables/models instead of creating parallel concepts if equivalents already exist.

- **Do not leak provider payloads**
  - Risk: debug logging or history serialization may accidentally store raw Fort