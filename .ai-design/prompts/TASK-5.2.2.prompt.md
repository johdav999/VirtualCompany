# Goal
Implement backlog task **TASK-5.2.2 — Expose correlated timeline API for tasks workflows approvals and tool executions** for story **US-5.2 Correlated action timeline across tasks, workflows, approvals, and tools**.

Deliver a backend-first implementation in the existing **.NET modular monolith** that exposes a tenant-aware, authorization-aware API for retrieving a **single correlated timeline view** by `correlationId`, linking related **task**, **workflow instance**, **approval**, and **tool execution** records.

The implementation must satisfy these outcomes:

- Events sharing the same `correlationId` are returned as one grouped timeline.
- Selecting an activity item can fetch linked entities and their current statuses via API without full page reload assumptions.
- Missing/deleted linked entities are represented with a visible unavailable state in the API contract, not as hard failures.
- Query performance for a single activity item remains **under 1 second** against fixtures with up to **10,000 related events**.
- Correlation is strictly limited to the **same tenant/company** and must respect existing authorization rules for tasks, workflows, approvals, and tools.

# Scope
In scope:

- Add or complete persistence support for correlation-aware audit/timeline querying.
- Define application-layer query models and handlers for correlated timeline retrieval.
- Expose API endpoint(s) from ASP.NET Core for:
  - retrieving a correlated timeline by activity item or `correlationId`
  - retrieving linked entity summaries/statuses for a selected timeline item
- Enforce tenant scoping and authorization filtering per linked entity type.
- Return resilient DTOs that preserve the timeline even when some linked entities are missing/deleted/inaccessible.
- Add tests for:
  - grouping by `correlationId`
  - tenant isolation
  - authorization filtering
  - missing entity handling
  - performance-oriented query behavior/fixture coverage where practical
- Add any required DB migration(s), indexes, and repository/query-layer changes.

Out of scope unless required to support the API contract:

- Full Blazor UI implementation
- Mobile changes
- Broad audit UX redesign
- Refactoring unrelated modules
- Introducing new infrastructure beyond current stack

Assumptions to verify in code before implementing:

- `audit_events` likely already exists or is partially modeled; confirm whether `correlation_id`, `target_type`, `target_id`, and timestamps are already present in domain/infrastructure mappings.
- Existing authorization policies for tasks/workflows/approvals/tools may already exist; reuse them rather than inventing a parallel model.
- Existing activity/audit endpoints may provide a base to extend rather than creating duplicate APIs.

# Files to touch
Touch only the minimum necessary files, but expect changes in these areas:

- `src/VirtualCompany.Domain/**`
  - audit/timeline domain entities or enums if missing
- `src/VirtualCompany.Application/**`
  - query DTOs
  - query handlers
  - authorization service interfaces
  - timeline response models
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configuration / repository / query services
  - SQL query optimization
  - migrations
- `src/VirtualCompany.Api/**`
  - controller or minimal API endpoint(s)
  - request/response contracts if API-specific
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/integration tests
- `tests/**` or other relevant test projects
  - application/infrastructure query tests if present

Likely file categories to inspect first:

- Existing audit event entity/configuration
- Existing task/workflow/approval/tool execution read models
- Existing tenant resolution and authorization helpers
- Existing migrations strategy and where new migrations belong
- Existing API route conventions for audit/history endpoints

If a migration is needed, place it in the project/location already used by the solution’s EF Core migration workflow.

# Implementation plan
1. **Inspect current audit and correlation model**
   - Find the current `AuditEvent` entity/table mapping and confirm whether `correlationId` is already persisted.
   - Confirm whether tasks, workflow instances, approvals, and tool executions already carry correlation metadata directly or are only linkable through audit events.
   - Identify the canonical source for timeline events:
     - preferred: `audit_events` as the event spine
     - fallback: compose from multiple tables if audit coverage is incomplete
   - Do not invent duplicate event storage if the audit module already supports this.

2. **Define the API contract**
   - Add a response model for a correlated timeline, for example:
     - `correlationId`
     - `activityItems[]`
     - each item includes:
       - event id
       - occurred at
       - actor summary
       - action
       - target type/id
       - outcome/status
       - rationale summary if available
       - linked entities summaries
   - Add linked entity summary DTOs for:
     - task
     - workflow instance
     - approval
     - tool execution
   - Include an explicit unavailable shape for missing/deleted/inaccessible entities, e.g.:
     - `isAvailable: false`
     - `unavailableReason: "missing" | "deleted" | "forbidden"`
   - Keep the contract UI-friendly and stable.

3. **Add/complete persistence fields and indexes**
   - If missing, add `correlation_id` to the relevant event spine table and any supporting entity tables only if truly needed.
   - Add indexes to support the acceptance criteria, likely:
     - `(company_id, correlation_id, created_at desc)` on `audit_events`
     - targeted indexes on linked entity lookup tables if not already present
   - Ensure migrations are backward-safe and nullable where needed for existing data.
   - If soft-delete exists on linked entities, account for it in queries.

4. **Implement application query service**
   - Create a query such as `GetCorrelatedTimelineQuery`.
   - Inputs should support one of:
     - `correlationId`
     - activity event id that resolves to a `correlationId`
   - Query flow:
     1. resolve current tenant/company
     2. resolve correlation id from the selected activity item if needed
     3. fetch all timeline events for that company + correlation id
     4. collect distinct linked entity ids by type
     5. bulk-load current entity summaries/statuses
     6. apply authorization filtering per entity type
     7. map unavailable states where entities are absent or inaccessible
     8. return ordered timeline items
   - Avoid N+1 queries; bulk-load by entity type.

5. **Authorization and tenant enforcement**
   - Every query must be scoped by `company_id`.
   - Reuse existing authorization policies/services for:
     - tasks
     - workflows
     - approvals
     - tool executions
   - If no reusable read-authorizer exists, add a small application service abstraction that can evaluate visibility per entity type.
   - Do not leak cross-tenant existence through error messages or partial metadata.
   - Prefer filtering inaccessible linked entities into unavailable/forbidden states rather than failing the whole timeline.

6. **Handle missing/deleted entities gracefully**
   - When an audit event references an entity that no longer exists:
     - keep the timeline item
     - return the linked entity as unavailable
   - If the event itself is valid but one linked record is gone, the rest of the correlation view must still render.
   - Preserve enough metadata from the event to explain what was linked even if the entity is unavailable.

7. **Expose API endpoints**
   - Add endpoint(s) under the existing audit/activity route conventions, e.g. one of:
     - `GET /api/activity/{activityId}/correlation`
     - `GET /api/timeline/correlations/{correlationId}`
   - If useful, add a second endpoint for item detail/linked entities, but prefer one efficient response if it keeps the contract simple.
   - Return:
     - `404` when the selected activity item does not exist in the tenant scope
     - `403` only where consistent with existing API conventions; otherwise prefer not found for cross-tenant access
   - Keep response payloads concise but sufficient for interactive selection without full page reload.

8. **Performance optimization**
   - Use projection queries instead of loading full aggregates.
   - Bulk-fetch linked entities by type using `IN` queries.
   - Ensure ordering/filtering happens in SQL.
   - Add or verify indexes for:
     - audit event correlation lookup
     - entity primary/status lookup paths
   - If needed, add a dedicated read-model query in infrastructure rather than forcing generic repositories.
   - Keep the 10,000-event fixture in mind; avoid per-item authorization DB roundtrips.

9. **Testing**
   - Add integration tests covering:
     - same `correlationId` groups task/workflow/approval/tool execution events into one timeline
     - selecting by activity item resolves the correct correlation set
     - missing linked entity returns unavailable state
     - unauthorized linked entity is filtered or marked unavailable without breaking the timeline
     - cross-tenant records are never included
   - Add query-level tests for ordering and bulk resolution behavior if possible.
   - Add a performance-oriented test/fixture if the test suite supports it; otherwise add a focused benchmark-style test or at minimum fixture coverage plus index verification comments.

10. **Document implementation notes**
   - Add concise comments only where the correlation behavior or unavailable-state mapping is non-obvious.
   - If there are gaps in current audit coverage, note them in code/TODOs without expanding scope beyond this task.

# Validation steps
1. Inspect and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify endpoint behavior manually or via integration tests:
   - create or seed events sharing a `correlationId` across:
     - task
     - workflow instance
     - approval
     - tool execution
   - call the new endpoint by `activityId` and/or `correlationId`
   - confirm one grouped timeline is returned in chronological order

4. Validate missing entity handling:
   - delete or simulate absence of one linked entity
   - confirm response still returns the timeline with an explicit unavailable state for that entity

5. Validate tenant isolation:
   - seed same `correlationId` in two tenants
   - confirm each tenant only sees its own records

6. Validate authorization behavior:
   - use a principal with partial access
   - confirm inaccessible linked entities do not break the response and are represented safely

7. Validate performance assumptions:
   - run against a fixture approximating 10,000 correlated events
   - confirm query path is index-backed and completes under the acceptance target in local/integration conditions as reasonably measurable
   - if exact automated timing is noisy, capture query plan/index usage and ensure no N+1 behavior

# Risks and follow-ups
- **Risk: correlationId not consistently persisted today**
  - If audit/tool/task/workflow records do not all carry correlation metadata, use `audit_events` as the canonical correlation spine and avoid widening scope into full backfill unless absolutely necessary.

- **Risk: authorization model may be fragmented**
  - Reuse existing policies where possible. If visibility checks differ by module, centralize only the minimum read-summary authorization abstraction needed for this query.

- **Risk: performance degradation from N+1 linked entity lookups**
  - Mitigate with bulk fetches by entity type and SQL projections.

- **Risk: deleted vs forbidden may be indistinguishable**
  - If current architecture cannot safely distinguish them without leaking information, prefer a generic unavailable state externally and keep internal reasoning private.

- **Risk: incomplete audit coverage**
  - If some actions are not yet emitting audit events with correlation IDs, document the gap and keep this task focused on exposing the API for the data that exists.

Follow-ups after implementation if needed:

- Add Blazor timeline UI consuming this API.
- Add caching for hot correlation queries if real-world load requires it.
- Add richer correlation source attribution and pagination if timelines become very large.
- Consider a dedicated read model/materialized view if audit volume grows beyond modular monolith query comfort.