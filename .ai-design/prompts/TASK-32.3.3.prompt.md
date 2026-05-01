# Goal
Implement backlog task **TASK-32.3.3** for **US-32.3 Real Fortnox API client and incremental read sync for finance master and transaction data**.

Deliver a production-ready implementation that persists Fortnox sync state and external references so incremental sync works across reruns and duplicate local records are prevented.

The implementation must ensure:

- Real Fortnox sync uses the real API base URL `https://api.fortnox.se/3`
- Incremental sync persists **per-entity cursors** in `FinanceIntegrationSyncStates`
- Synced records maintain durable mappings in `FinanceExternalReferences`
- Repeated sync runs are **idempotent** and do not create duplicate local records
- Sync history records **created, updated, skipped, and error counts**
- Finance pages can read synced Fortnox-backed data from local finance models rather than simulation-only services for synced records

# Scope
In scope:

- Review existing Fortnox integration, finance sync jobs, finance domain models, and persistence mappings
- Add or complete persistence for:
  - `FinanceIntegrationSyncStates`
  - `FinanceExternalReferences`
  - sync summary/history counts
- Ensure sync is incremental per entity using persisted cursors such as `lastmodified`, page/window markers, or equivalent supported Fortnox query strategy
- Ensure upsert/idempotent behavior for:
  - customers
  - suppliers
  - invoices
  - supplier invoices
  - vouchers
  - accounts
  - articles
  - projects
  - payment-related activity if represented in current model/sync flow
- Update manual **Sync now** flow to trigger real sync job and persist summary counts
- Update read paths used by finance pages so synced records come from local persisted finance models for Fortnox-backed data
- Add tests for duplicate prevention, cursor persistence, and sync summary recording

Out of scope unless required by existing code structure:

- New UI redesigns
- Broad refactors unrelated to Fortnox sync
- New integration providers
- Full webhook support
- Large schema redesigns beyond what is needed for this task

# Files to touch
Inspect first, then modify the actual relevant files you find. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - finance entities
  - integration entities
  - sync state / external reference models
- `src/VirtualCompany.Application/**`
  - Fortnox sync commands/handlers
  - finance sync orchestration
  - DTO mapping
  - manual sync command and sync history recording
- `src/VirtualCompany.Infrastructure/**`
  - Fortnox API client
  - repositories
  - EF Core configurations
  - migrations
  - background job implementations
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for manual sync if applicable
- `src/VirtualCompany.Web/**`
  - finance page query usage if currently reading simulation-only services
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests
- `tests/**` or other test projects if sync logic/unit tests live elsewhere

Also inspect:

- existing migration approach and whether new EF migration files belong in the normal migrations location
- any existing Fortnox-specific services, interfaces, and sync history tables/entities

# Implementation plan
1. **Discover current implementation**
   - Locate all Fortnox-related code, especially:
     - `FortnoxApiClient`
     - finance sync jobs/handlers
     - manual sync endpoint/action
     - finance read models/pages
     - `FinanceIntegrationSyncStates`
     - `FinanceExternalReferences`
     - sync history entities/tables
   - Identify current entity names and actual schema before coding
   - Document gaps against acceptance criteria in code comments or task notes as needed

2. **Confirm and enforce real Fortnox client behavior**
   - Ensure `FortnoxApiClient` uses base URL `https://api.fortnox.se/3`
   - Verify support for authenticated `GET`, `POST`, `PUT`, `DELETE`
   - Verify query parameter support for Fortnox endpoints where applicable:
     - `lastmodified`
     - `fromdate`
     - `todate`
     - `sortby`
     - `sortorder`
     - `page`
     - `limit`
   - Ensure Fortnox error responses are translated into:
     - safe user-facing messages
     - detailed internal logs with correlation/context
   - Do not leak raw Fortnox internals to end users

3. **Persist per-entity sync cursors**
   - Implement or complete persistence in `FinanceIntegrationSyncStates`
   - Store sync state per:
     - company/tenant
     - integration connection/account
     - entity type
   - Persist enough cursor metadata to support incremental reruns safely, for example:
     - last successful `lastmodified`
     - last page processed if needed
     - last sync started/completed timestamps
     - status/error metadata
   - Prefer advancing cursor only after successful processing of the relevant batch/window to avoid data loss
   - Make cursor updates resilient to partial failures

4. **Persist external references for idempotent upserts**
   - Implement or complete `FinanceExternalReferences` maintenance for every synced external record
   - Each synced local finance record should have a durable mapping including at minimum:
     - tenant/company
     - provider/integration type = Fortnox
     - external entity type
     - external id / document number / stable Fortnox identifier actually available
     - local entity id
   - Add uniqueness constraints/indexes where appropriate to prevent duplicate mappings
   - Use external references as the primary duplicate-prevention mechanism across reruns

5. **Implement idempotent entity sync/upsert flow**
   - For each supported entity:
     - fetch incrementally from Fortnox
     - resolve existing local record via `FinanceExternalReferences` first
     - if no reference exists, optionally fall back to safe natural-key matching only if already established in codebase and low risk
     - create local record + external reference when new
     - update existing local record when changed
     - skip unchanged records where possible
   - Ensure rerunning the same sync window does not create duplicates
   - Ensure related entities preserve referential consistency where dependencies exist

6. **Record sync summaries/history**
   - Update sync execution flow to persist summary counts:
     - created
     - updated
     - skipped
     - errors
   - If sync history entity/table already exists, extend it rather than creating parallel tracking
   - Record per-run metadata such as:
     - started at
     - completed at
     - entity scope or full sync scope
     - status
     - error summary if any
   - Ensure manual **Sync now** uses the real sync path and writes these counts

7. **Wire finance pages to persisted synced data**
   - Identify finance pages/views for:
     - invoices
     - supplier bills
     - payments/activity
     - overview
   - Ensure Fortnox-backed synced records are read from local persisted finance models
   - Remove or bypass simulation-only service usage for records that now exist from real sync
   - Preserve existing behavior for non-synced/demo scenarios if required by current app design

8. **Add database changes**
   - Add/update EF Core configurations and migration(s) for:
     - sync state fields
     - external reference fields
     - uniqueness/indexes
     - sync history summary fields
   - Keep migration naming clear and task-specific
   - Ensure tenant scoping and foreign keys are enforced

9. **Add tests**
   - Add focused tests covering:
     - Fortnox client base URL and request construction
     - cursor persistence after successful sync
     - rerun of same data does not create duplicate local records
     - external references are created and reused
     - sync summary counts are persisted correctly
     - manual sync triggers real sync path
     - finance queries/pages read synced persisted data instead of simulation-only data for synced records
   - Prefer deterministic tests with mocked/stubbed Fortnox responses

10. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries
   - Keep integration adapters in infrastructure
   - Keep orchestration/use cases in application layer
   - Keep persistence and tenant scoping explicit
   - Avoid direct UI-to-integration coupling

# Validation steps
Run and report the results of the relevant validation commands.

Minimum:
- `dotnet build`
- `dotnet test`

Also perform targeted validation:

1. **Code inspection validation**
   - Confirm Fortnox base URL is exactly `https://api.fortnox.se/3`
   - Confirm supported HTTP verbs and query parameter handling exist in client code

2. **Incremental sync validation**
   - Execute or test a sync run for at least one entity type
   - Verify `FinanceIntegrationSyncStates` is written
   - Re-run with same source payload and verify no duplicate local records are created

3. **External reference validation**
   - Verify `FinanceExternalReferences` entries are created for synced records
   - Verify lookup by external reference resolves existing local records on rerun

4. **Sync summary validation**
   - Trigger manual sync
   - Verify sync history contains created/updated/skipped/error counts

5. **Finance read-path validation**
   - Verify invoice/supplier bill/payment/activity/overview queries can surface persisted synced data
   - Confirm they are not dependent on simulation-only services for those synced records

If migrations are added:
- ensure migration compiles
- ensure database update path is consistent with repository conventions

# Risks and follow-ups
- Fortnox identifiers may differ by entity; use the most stable external identifier available per endpoint and do not assume one universal key shape
- Incremental sync based only on `lastmodified` can miss edge cases if cursor advancement is not handled carefully; prefer conservative cursor updates
- Partial batch failures can corrupt cursor progression if state is advanced too early
- Existing simulation services may be deeply coupled into finance pages; keep changes minimal but ensure real synced records are preferred
- If payment-related activity is modeled indirectly through vouchers or invoice payments, align implementation with the current domain model rather than inventing a parallel model
- Add or strengthen unique indexes to enforce idempotency at the database level, not only in application logic
- Preserve tenant isolation on all sync state, external reference, and finance record queries
- If current sync history model is too limited, extend it in-place rather than creating duplicate audit concepts
- Follow-up task may be needed for:
  - webhook-driven near-real-time sync
  - retry/dead-letter handling improvements
  - richer reconciliation reporting
  - backfill/full resync tooling
  - UI surfacing of sync history details and last sync status