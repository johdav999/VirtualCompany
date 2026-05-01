# Goal

Implement `TASK-32.3.4` by wiring Fortnox-synced finance read models into the application query layer and UI-facing finance views so that overview, invoices, supplier bills, payments, and activity queries read real synced data for Fortnox-backed records, with source-aware filtering that cleanly separates synced external data from simulation-only data.

This task sits on top of the existing Fortnox client/sync foundation from `US-32.3` and should complete the read path integration, not re-invent the sync engine. The result must ensure that:

- finance overview queries include Fortnox-backed aggregates and recent activity
- invoice queries return synced customer invoices
- supplier bill queries return synced supplier invoices
- payment/activity queries surface voucher/payment-related synced activity
- source-aware filtering allows callers/UI to request:
  - Fortnox-only
  - simulation-only
  - combined/all
- synced records are resolved from normalized local finance models and `FinanceExternalReferences`, never directly from simulation-only services once Fortnox data exists for those records
- manual “Sync now” continues to trigger real sync jobs and the resulting synced data becomes visible through these queries

# Scope

In scope:

- Inspect current finance query handlers, repositories, DTOs, and web pages/components for:
  - overview
  - invoices
  - supplier bills
  - payments
  - activity
- Add or extend source-aware query/filter contracts to support external-source filtering, specifically Fortnox-backed data.
- Update application query handlers to read from normalized local finance tables populated by sync, using `FinanceExternalReferences` and/or source metadata to identify Fortnox-backed records.
- Ensure Fortnox-backed records are included in aggregates, lists, and activity timelines.
- Prevent fallback to simulation-only services for records that should now come from synced local models.
- Preserve tenant/company scoping on all queries.
- Keep CQRS-lite boundaries intact: no UI component should query infrastructure directly.
- Add/adjust tests for query behavior, filtering, and manual sync visibility.

Out of scope unless required to make the task work:

- Rebuilding the Fortnox API client
- Reworking the full sync orchestration if already implemented
- Large UI redesigns
- New external entities beyond those already mapped by the sync story
- Direct reads from Fortnox at query time; this task must use local synced read models

# Files to touch

Start by locating and updating the concrete files that implement finance queries and pages. Expect to touch files in these areas:

- `src/VirtualCompany.Application/**`
  - finance query contracts, handlers, DTOs, filter models
  - sync history / manual sync command handlers if query invalidation or counts need exposure
- `src/VirtualCompany.Domain/**`
  - finance model enums/value objects for source typing if missing
  - source classification helpers if domain-owned
- `src/VirtualCompany.Infrastructure/**`
  - EF Core query repositories / projections
  - Fortnox-backed read model lookup logic
  - any source-aware repository methods
- `src/VirtualCompany.Web/**`
  - finance overview page/component
  - invoices page/component
  - supplier bills page/component
  - payments/activity page/component
  - filter UI bindings if source filter is exposed in the web app
- `src/VirtualCompany.Api/**`
  - finance endpoints if queries are API-exposed rather than web-only
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/query integration tests
- potentially additional test projects if present for application/infrastructure layers

Also inspect these likely concepts and update where found:

- `FinanceExternalReferences`
- `FinanceIntegrationSyncStates`
- sync history entities/DTOs
- finance overview query/result models
- invoice list query/result models
- supplier bill list query/result models
- payment/activity query/result models
- any simulation finance service abstractions currently feeding the UI

Do not invent file names prematurely. First search the solution for:

- `FinanceExternalReferences`
- `FinanceIntegrationSyncStates`
- `Fortnox`
- `Invoice`
- `SupplierInvoice`
- `Voucher`
- `Payment`
- `Activity`
- `Overview`
- `Sync now`
- `simulation`
- `source`
- `finance overview`

# Implementation plan

1. **Map the existing finance read path before changing code**
   - Identify how the following screens/queries are currently populated:
     - finance overview
     - invoices
     - supplier bills
     - payments
     - activity
   - Determine whether they currently use:
     - simulation-only services
     - direct domain repositories
     - application projections
     - mixed sources
   - Document the exact handlers/components/endpoints to update in your work log or PR notes.

2. **Identify the normalized local finance entities populated by Fortnox sync**
   - Confirm which local entities already store synced:
     - customers
     - suppliers
     - invoices
     - supplier invoices
     - vouchers
     - accounts
     - articles
     - projects
   - Confirm how payment-related activity is represented:
     - explicit payment table
     - voucher rows
     - activity/audit projection
     - invoice payment status fields
   - Confirm how `FinanceExternalReferences` links local records to Fortnox external IDs and source system metadata.

3. **Introduce or standardize source-aware filtering**
   - Add a source filter enum/value object if one does not already exist, e.g. conceptually:
     - `All`
     - `Fortnox`
     - `Simulation`
   - Thread this filter through relevant query contracts for:
     - overview
     - invoice list
     - supplier bill list
     - payment list/activity list
   - Keep backward compatibility where possible by defaulting to `All`.
   - Ensure source filtering is tenant-safe and composable with existing filters like date range, status, paging, etc.

4. **Update invoice queries to use synced local models**
   - Replace or bypass simulation-only invoice reads for Fortnox-backed records.
   - Query normalized local invoice entities joined or correlated with `FinanceExternalReferences` to identify source.
   - Return DTOs with enough metadata for the UI to distinguish source if needed.
   - Ensure duplicate external records are not surfaced multiple times if local sync has already deduplicated them.
   - Preserve sorting, paging, and status filtering behavior.

5. **Update supplier bill queries similarly**
   - Use local supplier invoice / supplier bill models populated by sync.
   - Apply the same source-aware filtering pattern.
   - Ensure supplier identity and amount/status/date fields are mapped consistently with existing UI expectations.

6. **Update payments and/or activity queries**
   - Determine the current product model for “payments”:
     - dedicated payment records
     - payment-like activity derived from vouchers or invoice events
   - Wire Fortnox voucher/payment-related synced data into the query result.
   - If the UI has both a payments list and an activity feed, ensure both can include Fortnox-backed entries.
   - Use safe projection logic so Fortnox voucher/payment activity appears as user-meaningful rows without exposing raw external payloads.

7. **Update finance overview aggregates**
   - Ensure overview totals/cards/recent activity are computed from local normalized finance data, not simulation-only services, for synced records.
   - Include Fortnox-backed invoices, supplier bills, and payment/activity signals in overview summaries.
   - If overview combines multiple sources, make source filtering explicit and deterministic.
   - Avoid double counting when both simulation and synced data exist.

8. **Prevent simulation-only services from leaking into synced views**
   - Where current handlers call simulation services, refactor so:
     - Fortnox-backed records come from local synced models
     - simulation-only records remain available only when source filter allows them
   - If needed, introduce a composition layer that merges:
     - synced local records
     - simulation-only records
     under a single source-aware query contract
   - Prefer one application-layer query path rather than branching in UI components.

9. **Wire manual sync visibility**
   - Verify the existing “Sync now” action triggers the real sync command/job.
   - Ensure post-sync queries read the newly persisted local records without additional simulation dependencies.
   - If sync history counts are already recorded elsewhere, do not duplicate logic; only ensure the UI/query path reflects the synced data and history remains visible.

10. **Expose source metadata in DTOs where useful**
    - Add a lightweight source field to result DTOs if the UI needs badges/filters:
      - `Source`
      - `ExternalSystemName`
      - `IsExternalSynced`
   - Do not expose sensitive Fortnox raw payloads or tokens.
   - Keep user-facing labels safe and simple.

11. **Add tests**
   - Application/query tests:
     - Fortnox-only filter returns only synced Fortnox-backed records
     - Simulation-only filter excludes Fortnox-backed records
     - All/combined returns both without double counting
     - overview aggregates include synced records correctly
     - activity/payments include voucher/payment-related synced entries
   - API/web integration tests where applicable:
     - finance endpoints/pages return Fortnox-backed data after sync seed/setup
     - tenant scoping is preserved
   - Regression tests:
     - no duplicate rows for the same external entity
     - manual sync followed by query shows created/updated data

12. **Keep implementation aligned with architecture**
   - Respect modular monolith boundaries.
   - Keep query logic in application/infrastructure, not Blazor pages.
   - Use local database as source of truth for synced finance data.
   - Maintain clean separation between technical logs and user-facing safe messages.

# Validation steps

1. **Codebase discovery**
   - Search the solution for the finance query handlers/pages and Fortnox sync entities:
     - `rg "FinanceExternalReferences|FinanceIntegrationSyncStates|Fortnox|simulation|Invoice|SupplierInvoice|Voucher|Payment|Activity|Overview" src tests`
   - Confirm the exact read path before editing.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Targeted verification of query behavior**
   - Add or run tests that prove:
     - invoice query returns Fortnox-backed synced invoices
     - supplier bill query returns Fortnox-backed synced supplier invoices
     - payments/activity query returns Fortnox voucher/payment-related activity
     - overview includes synced data in aggregates
     - source filter works for `All`, `Fortnox`, and `Simulation`

5. **Manual verification if runnable locally**
   - Seed or use an environment with an existing Fortnox sync result.
   - Trigger manual `Sync now`.
   - Verify sync history records created/updated/skipped/error counts.
   - Open or call:
     - overview
     - invoices
     - supplier bills
     - payments/activity
   - Confirm synced records appear without relying on simulation-only services.

6. **Regression checks**
   - Verify no duplicate records appear for the same external entity.
   - Verify company/tenant isolation on all updated queries.
   - Verify existing paging/sorting/date filters still behave correctly.

7. **PR summary expectations**
   - Include a concise summary of:
     - which query handlers were updated
     - how source-aware filtering was implemented
     - which local synced models now back each finance screen
     - what tests were added

# Risks and follow-ups

- **Risk: unclear current source model**
  - The app may not yet have a unified source enum/flag across finance entities.
  - Mitigation: introduce a minimal shared source classification and adapt projections rather than changing every domain entity unnecessarily.

- **Risk: payments are modeled indirectly**
  - Fortnox payment information may be represented via vouchers or invoice state transitions rather than a dedicated payment table.
  - Mitigation: inspect current domain model first and project payment/activity rows from the canonical synced local representation already in use.

- **Risk: double counting in overview**
  - Combined views may count both simulation and synced records for conceptually similar items.
  - Mitigation: make source filtering explicit and ensure overview aggregation uses the same source-aware composition logic as list queries.

- **Risk: UI currently depends on simulation DTO shape**
  - Existing pages may assume fields only present in simulation services.
  - Mitigation: adapt application DTOs to preserve UI contract compatibility while changing the backing source.

- **Risk: sync foundation may be incomplete**
  - If some Fortnox entities are not yet mapped locally, this task can only wire what exists.
  - Mitigation: do not fabricate direct Fortnox reads in queries; note any missing upstream mapping as a follow-up.

Follow-ups to note if discovered during implementation:

- add explicit source badges/filter controls in the web UI if not already present
- unify finance source filtering across all finance modules/endpoints
- add cached aggregate projections for overview if query performance degrades
- extend activity normalization for richer Fortnox voucher/payment event descriptions
- add dedicated application-layer tests around `FinanceExternalReferences` correlation rules if currently under-tested