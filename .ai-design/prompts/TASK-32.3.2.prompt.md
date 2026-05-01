# Goal
Implement backlog task **TASK-32.3.2** by adding a real Fortnox integration layer that can sync finance master and transaction data into local finance models.

Deliver:
- A production Fortnox API client using base URL `https://api.fortnox.se/3`
- `FortnoxMappingService` to normalize Fortnox payloads into local finance entities
- `FortnoxSyncService` to perform idempotent incremental sync with per-entity cursors in `FinanceIntegrationSyncStates`
- Support for customers, suppliers, accounts, articles, projects, invoices, supplier invoices, vouchers, and payment-related activity
- A manual **Sync now** path that triggers a real sync job and records created/updated/skipped/error counts in sync history
- Finance pages/query paths for overview, invoices, supplier bills, and payments/activity to read synced Fortnox-backed data instead of simulation-only services for synced records

Work within the existing modular monolith and .NET solution structure. Prefer extending current finance/integration abstractions rather than introducing parallel patterns.

# Scope
In scope:
- Real Fortnox HTTP client with authenticated `GET`, `POST`, `PUT`, `DELETE`
- Query parameter support where Fortnox supports it:
  - `lastmodified`
  - `fromdate`
  - `todate`
  - `sortby`
  - `sortorder`
  - `page`
  - `limit`
- Safe Fortnox error translation for user-facing flows, with detailed internal logging
- Incremental sync state persistence per entity type in `FinanceIntegrationSyncStates`
- Idempotent upsert behavior so repeated syncs do not create duplicate local records
- Mapping and persistence of:
  - customers
  - suppliers
  - accounts
  - articles
  - projects
  - invoices
  - supplier invoices
  - vouchers
  - payment-related activity
- Maintenance of `FinanceExternalReferences` for every synced external record
- Sync history counters for created, updated, skipped, and errors
- Wiring manual sync action to real sync execution

Out of scope unless already partially implemented and trivial to finish:
- New OAuth/connect UX beyond what is required to use existing Fortnox credentials/tokens
- Webhook ingestion
- Full bidirectional writeback for all entities
- Reworking unrelated finance domain models
- Replacing all simulation code globally; only ensure synced Fortnox-backed records are used by relevant finance pages/query paths

# Files to touch
Inspect the solution first and update the exact files that match existing patterns. Expect to touch files in these areas:

- `src/VirtualCompany.Infrastructure/`
  - Fortnox API client implementation
  - HTTP/auth configuration
  - integration repositories
  - sync job/service implementations
- `src/VirtualCompany.Application/`
  - finance integration commands/queries
  - sync orchestration services/interfaces
  - DTOs/contracts for Fortnox sync results/history
- `src/VirtualCompany.Domain/`
  - finance entities/value objects if required
  - sync state/history models if incomplete
  - external reference handling if missing
- `src/VirtualCompany.Api/`
  - manual sync endpoint/handler wiring if API-triggered
- `src/VirtualCompany.Web/`
  - only if needed to ensure finance pages use real synced data paths
- `tests/VirtualCompany.Api.Tests/`
- any relevant application/infrastructure/domain test projects already present in the repo

Also inspect for existing types with names similar to:
- `FortnoxApiClient`
- `FinanceIntegrationSyncStates`
- `FinanceExternalReferences`
- finance sync history entities/tables
- simulation/demo finance services
- invoice/supplier bill/payment overview query handlers

Do not create duplicate concepts if equivalents already exist.

# Implementation plan
1. **Discover existing finance integration architecture**
   - Search the solution for Fortnox, finance integrations, sync state/history, external references, and simulation services.
   - Identify:
     - current finance entities and repositories
     - existing integration provider abstractions
     - current manual sync flow
     - current finance page query sources
   - Reuse naming and layering conventions already in the codebase.

2. **Implement/complete `FortnoxApiClient`**
   - Use base URL exactly: `https://api.fortnox.se/3`
   - Support authenticated `GET`, `POST`, `PUT`, `DELETE`
   - Centralize header handling and token/auth injection according to existing credential storage
   - Add support for query parameter composition for supported endpoints
   - Implement endpoint methods for at least:
     - company information
     - customers
     - suppliers
     - invoices
     - supplier invoices
     - vouchers
     - accounts
     - articles
     - projects
   - Handle pagination where Fortnox returns paged collections
   - Translate Fortnox API errors into:
     - safe user-facing messages
     - structured internal logs with status code, endpoint, tenant/company context, correlation id, and response body when safe
   - Avoid leaking secrets or raw Fortnox internals in user-facing exceptions.

3. **Add Fortnox DTOs/contracts**
   - Create provider DTOs for the Fortnox resources being synced.
   - Keep provider DTOs isolated from domain entities.
   - Include only fields needed for mapping, cursors, and display/query requirements.
   - Preserve external IDs and modified timestamps needed for incremental sync.

4. **Implement `FortnoxMappingService`**
   - Map Fortnox DTOs into local finance models using existing domain entities where available.
   - Maintain `FinanceExternalReferences` for each synced record.
   - Ensure mappings cover:
     - customers
     - suppliers
     - accounts
     - articles
     - projects
     - invoices
     - supplier invoices
     - vouchers
     - payment-related activity
   - If payment-related activity has no single existing entity, map it into the closest existing local payment/activity model already used by overview/payments pages. Do not invent a broad new subsystem unless necessary.
   - Normalize dates, currency/amount fields, status fields, and external metadata consistently.
   - Preserve tenant/company scoping on all mapped records.

5. **Implement idempotent upsert logic**
   - For each external record:
     - resolve by existing `FinanceExternalReference` first
     - otherwise use safe provider-specific unique keys if already modeled
   - Repeated sync runs must update existing local records rather than insert duplicates.
   - Record whether each item was created, updated, skipped, or errored.
   - Ensure external reference creation is transactional with local entity persistence.

6. **Implement `FortnoxSyncService`**
   - Orchestrate per-entity sync in a deterministic order, e.g.:
     - accounts
     - customers
     - suppliers
     - articles
     - projects
     - invoices
     - supplier invoices
     - vouchers
     - payment-related activity
   - Read and persist per-entity cursors in `FinanceIntegrationSyncStates`
   - Use incremental filters such as `lastmodified`, `fromdate`, `todate` where supported by the endpoint
   - Update cursor only after successful processing of the relevant entity batch/page
   - Make sync resilient to partial failures:
     - one entity failure should be recorded clearly
     - do not corrupt cursors
   - Return a structured sync result with counts:
     - created
     - updated
     - skipped
     - errors

7. **Persist sync history**
   - Ensure manual and background sync runs create sync history records.
   - Store:
     - provider/integration id
     - entity or overall sync scope
     - started/completed timestamps
     - status
     - created/updated/skipped/error counts
     - error summary if applicable
   - Reuse existing history tables/models if present.

8. **Wire manual `Sync now` to real sync**
   - Find the current manual sync action and replace or extend it so it triggers the real Fortnox sync job/service.
   - Ensure the action is tenant/company scoped.
   - If the app uses background jobs, enqueue the real sync job; otherwise invoke the service through the existing application flow.
   - Surface sync history/counts to the caller/UI using existing patterns.

9. **Switch finance read paths away from simulation-only data for synced records**
   - Identify overview, invoices, supplier bills, and payments/activity query handlers or services.
   - Ensure they read from normalized local finance tables populated by sync.
   - If simulation services are still needed for unsynced/demo scenarios, gate them so Fortnox-synced records come from real persisted data and are not overwritten or hidden by simulation-only sources.

10. **Logging, observability, and safety**
   - Add structured logs around:
     - sync start/end
     - per-entity progress
     - page processing
     - cursor updates
     - Fortnox failures
   - Include correlation IDs and tenant context where available.
   - Keep technical logs separate from business sync history.

11. **Tests**
   - Add unit/integration tests for:
     - Fortnox query parameter generation
     - error translation behavior
     - mapping for each supported entity family
     - idempotent sync behavior across repeated runs
     - cursor persistence and incremental reads
     - manual sync history counts
     - finance queries preferring synced persisted data over simulation-only services for synced records

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Add or update automated tests to verify:
   - `FortnoxApiClient` uses `https://api.fortnox.se/3`
   - authenticated `GET/POST/PUT/DELETE` requests are formed correctly
   - supported query parameters are passed through correctly
   - Fortnox error responses are converted to safe user-facing messages and detailed logs
   - repeated sync of the same external entities does not create duplicates
   - `FinanceIntegrationSyncStates` stores separate cursors per entity
   - sync history records created/updated/skipped/error counts
   - `FinanceExternalReferences` are created/maintained for all synced records
   - invoices, supplier bills, payments/activity, and overview queries can return Fortnox-backed persisted data

4. If there are existing integration tests or HTTP mocking patterns, use them to simulate Fortnox responses for:
   - customers
   - suppliers
   - invoices
   - supplier invoices
   - vouchers
   - accounts
   - articles
   - projects
   - company information

5. Manually verify code paths:
   - trigger manual **Sync now**
   - confirm real sync service/job is invoked
   - inspect persisted sync state/history
   - confirm finance pages/query handlers no longer depend on simulation-only services for synced records

6. Include in your final implementation notes:
   - files changed
   - any schema/model assumptions
   - any endpoints/entities partially supported due to existing domain constraints
   - follow-up items if payment-related activity required approximation to existing local models

# Risks and follow-ups
- **Fortnox auth/header specifics may already exist**: reuse existing credential abstractions and do not hardcode secrets handling.
- **Endpoint capabilities vary**: some query parameters may not be supported uniformly across all Fortnox endpoints; implement only where supported and guard unsupported combinations.
- **Payment-related activity may be ambiguous**: map to the closest existing local payment/activity model and document any limitations.
- **Cursor correctness is critical**: never advance a cursor before successful persistence of the relevant batch/page.
- **Duplicate prevention depends on external references**: ensure uniqueness constraints or repository checks exist for provider + entity type + external id.
- **Finance page behavior may be split across app/web layers**: trace actual query sources before changing UI code.
- **Schema gaps may exist**: if `FinanceIntegrationSyncStates`, `FinanceExternalReferences`, or sync history structures are incomplete, extend them minimally and consistently with existing migrations/persistence patterns.
- **Do not overdesign**: prefer incremental additions that satisfy acceptance criteria and fit the current modular monolith.