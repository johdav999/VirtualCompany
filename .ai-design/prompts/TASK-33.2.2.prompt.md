# Goal
Implement backlog task **TASK-33.2.2 — Build normalized mappers from Fortnox entities into internal finance records and external reference upsert logic** for story **US-33.2 Build production Fortnox sync pipeline and normalized finance data mapping**.

The coding agent must extend the existing .NET modular monolith so that Fortnox sync imports real production Fortnox entities, maps them into the platform’s existing internal finance contracts, and persists tenant-scoped external reference mappings that support idempotent re-syncs.

This task is specifically about:
- normalized mapping from Fortnox entities into internal finance records
- external reference upsert/idempotency behavior
- tenant-safe persistence and permission-aware sync write paths
- sync history/result metadata needed by the acceptance criteria
- plain-English Fortnox error translation with detailed internal logging preserved

Do **not** redesign the architecture. Fit into the existing solution structure and conventions.

# Scope
Implement the following behavior:

1. **Fortnox entity normalization**
   - Map these Fortnox entities into existing internal finance contracts/models:
     - company information
     - customers
     - suppliers
     - invoices
     - supplier invoices
     - vouchers
     - accounts
     - articles
     - projects
   - Use production API response models/adapters already present if available; otherwise add minimal provider DTOs needed for mapping.
   - Do not store raw provider payloads as the system of record.

2. **External reference persistence**
   - For every imported internal finance record, persist or update a tenant-scoped external reference containing:
     - provider name
     - external id
     - internal id
     - entity type
     - last synced timestamp
   - Ensure repeated syncs upsert the mapping rather than duplicating it.

3. **Idempotent sync behavior**
   - Re-syncing unchanged Fortnox records must not create duplicate internal records.
   - Existing internal records should be found through external references and updated or left unchanged as appropriate.
   - Mapping/upsert logic must be safe under retries.

4. **Sync history/result metadata**
   - Ensure sync execution records final status, entity counts, duration, retry/failure outcome, and a safe error summary.
   - Integrate with existing sync history/job tracking abstractions if present.

5. **Error handling**
   - Translate Fortnox API failures into plain-English user-facing messages.
   - Preserve detailed diagnostics in structured server logs.
   - Distinguish transient retryable failures from permanent/business failures where existing retry infrastructure supports it.

6. **Tenant isolation and permissions**
   - All reads/writes must be scoped to the active tenant/company.
   - Respect existing authorization/policy patterns for manual sync execution and finance data writes.

Out of scope unless required to complete this task cleanly:
- new UI flows beyond minimal API contract adjustments
- storing raw Fortnox payload archives
- unrelated connector refactors
- broad schema redesigns outside required persistence support

# Files to touch
Inspect the solution first and then update the most relevant files in these areas as needed.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to inspect/touch:
- Fortnox integration client/service classes
- finance sync command handlers / application services
- normalized finance domain models or contracts
- external reference entity/repository/persistence configuration
- sync history entities/services
- authorization/tenant context helpers
- EF Core DbContext and entity configurations
- migrations if schema changes are required
- API endpoint/controller/minimal API handler for manual sync
- logging/error translation utilities
- unit/integration tests

Before coding, identify the actual existing files for:
- Fortnox connector/client
- finance record contracts/entities
- sync history persistence
- external reference persistence, if any
- tenant resolution and authorization
- retry/job execution infrastructure

Prefer modifying existing files over creating parallel abstractions.

# Implementation plan
1. **Discover existing finance sync and integration structure**
   - Search for:
     - `Fortnox`
     - `Sync`
     - `ExternalReference`
     - `Finance`
     - `CompanyId`
     - `tenant`
     - `manual sync`
   - Identify:
     - current manual sync endpoint
     - Fortnox API client(s)
     - existing internal finance entities/contracts
     - existing sync history model
     - existing retry/background execution mechanism
     - whether an external reference table/entity already exists

2. **Define/confirm normalized mapping targets**
   - For each Fortnox entity type, map into the existing internal finance contract rather than inventing a new parallel model.
   - If the internal contract is incomplete, extend it minimally and consistently.
   - Ensure mappings are deterministic and null-safe.
   - Normalize key fields such as:
     - names/descriptions
     - external document numbers
     - dates
     - currency
     - totals/amounts
     - status/state
     - account/article/project codes
     - customer/supplier identity fields
   - Keep provider-specific fields out of the core system-of-record model unless there is an existing extension field intended for that purpose.

3. **Implement external reference upsert logic**
   - Add or complete a tenant-scoped external reference model/repository if missing.
   - Enforce uniqueness on the tuple conceptually equivalent to:
     - `company_id + provider + entity_type + external_id`
   - External reference should store:
     - internal entity id
     - provider
     - entity type
     - external id
     - last synced timestamp
   - Upsert behavior:
     - if mapping exists, update `internal_id` if needed and refresh `last_synced_timestamp`
     - if mapping does not exist, create it
   - Ensure all lookups are tenant-scoped.

4. **Implement idempotent import flow**
   - For each fetched Fortnox record:
     - resolve active tenant/company context
     - look up external reference by tenant + provider + entity type + external id
     - if found, load the mapped internal record and update normalized fields
     - if not found, create a new internal record and then create the external reference
   - Avoid duplicate creation on retries by:
     - using unique constraints where appropriate
     - handling race/duplicate exceptions safely if the architecture already supports concurrent retries
   - If an internal record referenced by an external reference is missing, decide on the existing project convention:
     - recreate and repair mapping, or
     - fail safely with diagnostics
   - Prefer the convention already used elsewhere in the codebase.

5. **Wire entity-specific mappers**
   - Create focused mapper classes or methods per Fortnox entity type if that matches project style.
   - Suggested shape:
     - `FortnoxCustomer -> InternalCustomer`
     - `FortnoxSupplier -> InternalSupplier`
     - `FortnoxInvoice -> InternalInvoice`
     - etc.
   - Keep mapping logic out of controllers/endpoints.
   - Keep provider DTOs separated from internal domain/application contracts.

6. **Integrate sync history updates**
   - Ensure manual sync execution records:
     - started timestamp
     - completed timestamp
     - duration
     - final status
     - per-entity counts imported/updated/skipped/failed if supported
     - safe error summary on failure
   - If sync history already has a schema, populate existing fields rather than adding redundant ones.
   - If counts are stored as JSON/metadata, use that pattern consistently.

7. **Implement Fortnox error translation**
   - Add a translator that maps common Fortnox/API failures into user-safe messages, for example:
     - authentication/authorization issues
     - expired/invalid connection
     - rate limiting
     - not found/resource unavailable
     - validation/data issues
     - temporary upstream outage/timeouts
   - Return plain-English messages from the sync endpoint/application result.
   - Log full exception details, response codes, and correlation/context internally.
   - Do not leak secrets, tokens, or raw provider payloads in user-facing responses.

8. **Respect retry policy**
   - Reuse existing retry/background execution policy if present.
   - Mark transient failures as retryable where the current infrastructure supports it.
   - Ensure final sync history reflects whether retries were exhausted and the final outcome.
   - Do not implement a second retry framework.

9. **Enforce tenant isolation and permissions**
   - Verify manual sync endpoint requires authenticated tenant context and appropriate permission.
   - Ensure all repository queries and writes include tenant/company scoping.
   - Add guards/tests for cross-tenant access denial or isolation.

10. **Add/update persistence**
   - If schema changes are required:
     - add EF configuration
     - add migration
     - ensure indexes/unique constraints support idempotent upsert
   - Keep schema changes minimal and production-safe.

11. **Add tests**
   - Add focused tests for:
     - mapping correctness for representative Fortnox entities
     - external reference upsert behavior
     - idempotent repeated sync
     - tenant isolation
     - safe error translation
     - sync history status/count recording
   - Prefer unit tests for mappers and application services, plus integration/API tests for end-to-end sync behavior where feasible.

12. **Implementation constraints**
   - Follow existing naming, layering, and dependency direction.
   - Keep domain/application boundaries clean.
   - Do not bypass application services with direct controller-to-DbContext logic unless that is already the established pattern.
   - Do not introduce raw payload storage as a shortcut.

# Validation steps
1. Inspect and build the solution:
   - `dotnet build`

2. Run the relevant test suite:
   - `dotnet test`

3. Add/verify automated coverage for these scenarios:
   - first sync creates internal finance records and external references
   - second sync with unchanged Fortnox data does not create duplicates
   - second sync refreshes `last synced timestamp` and updates existing mappings idempotently
   - sync writes are tenant-scoped
   - cross-tenant records are not visible or mutable
   - sync history captures status, counts, duration, and safe error summary
   - Fortnox API exceptions are translated to plain-English user-facing messages
   - detailed diagnostics remain in logs or internal error structures
   - retryable failures are classified correctly if retry infrastructure exists

4. If migrations are added:
   - generate/apply migration per repo conventions
   - verify unique constraints/indexes for external references
   - verify application starts cleanly after migration

5. Manually review code for acceptance criteria alignment:
   - production Fortnox endpoints used by manual sync path
   - normalized internal records persisted
   - no provider payloads used as system of record
   - external references persisted for each imported record
   - idempotent re-sync behavior
   - permission-checked tenant isolation
   - safe user messaging + detailed internal diagnostics

# Risks and follow-ups
- The exact internal finance contract models may not yet cover all Fortnox entity fields; extend minimally and avoid provider-shaped leakage.
- If no external reference abstraction exists, introduce one carefully with strong uniqueness constraints and tenant scoping.
- If sync history schema is immature, use the existing pattern first and note any gaps rather than overbuilding.
- If the current manual sync endpoint only triggers jobs and does not execute inline, integrate at the application/job layer rather than forcing synchronous behavior.
- If Fortnox pagination/rate limiting is not yet handled, preserve current client patterns and note any remaining production hardening gaps.
- If there is ambiguity about mapping of vouchers/accounts/articles/projects into internal contracts, prefer the existing finance domain vocabulary and document assumptions in code comments/tests.
- After implementation, note any follow-up backlog items needed for:
  - richer field coverage
  - pagination/performance tuning
  - reconciliation reporting
  - webhook/incremental sync support
  - operational dashboards for sync health