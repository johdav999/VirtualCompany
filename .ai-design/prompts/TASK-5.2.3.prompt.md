# Goal
Implement backlog task **TASK-5.2.3 — Drill-down correlation panel in the activity timeline UI** for story **US-5.2 Correlated action timeline across tasks, workflows, approvals, and tools**.

Deliver a tenant-safe, authorization-aware correlation experience in the web UI that:
- groups activity events by `correlationId`
- lets users select an activity item and view linked task, workflow, approval, and tool execution entities
- updates the drill-down panel without a full page reload
- renders missing/deleted linked entities as visible unavailable states
- performs correlation queries for a single activity item in **< 1 second** against fixtures up to **10,000 related events**
- enforces same-tenant and existing authorization rules across all linked entity types

Use the existing architecture conventions:
- ASP.NET Core modular monolith
- CQRS-lite application layer
- PostgreSQL-backed persistence
- Blazor Web App frontend
- tenant-scoped and policy-based authorization
- no direct DB access from UI

# Scope
In scope:
- Add/extend backend query models and handlers for correlation timeline drill-down
- Add repository/query-layer support for fetching correlated activity data by `correlationId`
- Enforce tenant isolation and per-entity authorization filtering
- Return resilient view models that include unavailable/missing entity states
- Build or extend Blazor timeline UI to:
  - render grouped correlated activity items
  - support selecting an item
  - load/update linked entity details asynchronously without full page reload
  - show current statuses for linked entities
  - show unavailable state for missing/deleted entities
- Add tests for:
  - grouping behavior
  - tenant isolation
  - authorization filtering
  - missing entity handling
  - performance-oriented query behavior where feasible in automated tests

Out of scope unless required by existing code patterns:
- broad redesign of audit/event schema
- mobile UI changes
- unrelated dashboard refactors
- introducing new infrastructure beyond current stack
- changing core authorization model beyond what is needed to apply existing rules

Assume correlation is driven by persisted `correlationId` already present or derivable in the relevant activity/audit records. If the current schema does not expose it cleanly, add the minimum necessary persistence/query support consistent with existing patterns.

# Files to touch
Inspect first, then update the appropriate files in these areas.

Likely backend:
- `src/VirtualCompany.Application/**`
  - queries/handlers/DTOs/view models for correlated activity timeline
- `src/VirtualCompany.Domain/**`
  - domain models/enums/value objects only if needed
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/query services/repositories for correlation lookups
  - authorization-aware query composition
- `src/VirtualCompany.Api/**`
  - endpoints/controllers for correlation timeline and drill-down data

Likely frontend:
- `src/VirtualCompany.Web/**`
  - activity timeline page/component
  - drill-down correlation panel component
  - client/service for async loading
  - status/unavailable state rendering

Likely tests:
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for correlation endpoint behavior
- add application/infrastructure tests if the solution already has matching test projects/patterns

Also inspect for relevant existing files around:
- activity timeline / audit trail / explainability views
- task/workflow/approval/tool execution query endpoints
- tenant resolution and authorization helpers
- shared DTOs in `src/VirtualCompany.Shared/**` if used by both API and Web

# Implementation plan
1. **Discover existing correlation and activity timeline model**
   - Search for:
     - `correlationId`
     - activity timeline
     - audit trail / explainability
     - recent activity feed
     - task/workflow/approval/tool execution list/detail queries
   - Identify the canonical source of activity events:
     - likely `audit_events`, possibly joined with tasks/workflows/approvals/tool executions
   - Reuse existing query and authorization patterns rather than inventing a parallel stack.

2. **Define the drill-down contract**
   - Add a query/response model for a single selected activity item’s correlation view.
   - Recommended response shape:
     - selected activity item summary
     - `correlationId`
     - grouped related events ordered by timestamp
     - linked entities collection with:
       - entity type
       - entity id
       - display label
       - current status
       - availability state (`available`, `missing`, `deleted`, `unauthorized`, or equivalent)
       - optional navigation URL if authorized/available
     - summary counts by entity type/status if useful for UI
   - Keep DTOs UI-friendly and explicit; do not leak EF entities.

3. **Implement tenant-safe correlation query**
   - In infrastructure/application layers, implement a query that:
     - resolves current tenant/company context
     - fetches all activity events sharing the selected item’s `correlationId`
     - restricts results to the same tenant only
     - joins or separately resolves linked tasks, workflows, approvals, and tool executions
   - Ensure query shape is efficient for up to 10,000 related events:
     - filter by tenant + `correlationId` first
     - project only needed columns
     - avoid N+1 lookups
     - batch linked entity resolution by type and ids
     - use `AsNoTracking()` for read models
   - If indexes are managed in code migrations and missing for this query path, add the minimum required index support for `(company_id, correlation_id)` or equivalent source table index.

4. **Apply authorization rules per linked entity**
   - Respect existing authorization rules for:
     - tasks
     - workflows
     - approvals
     - tools/tool executions
   - Do not expose unauthorized entity details.
   - Preferred behavior:
     - correlated event can remain in timeline if the event itself is visible
     - linked entity details should be filtered or marked unavailable/unauthorized according to current app conventions
   - Reuse existing policy services/authorization evaluators where available.
   - Never allow cross-tenant correlation leakage even if `correlationId` collides.

5. **Handle missing/deleted linked entities gracefully**
   - If an event references a task/workflow/approval/tool execution that no longer exists:
     - return a stable unavailable state in the response
     - include enough metadata for the UI to render “Unavailable” without throwing
   - Do not fail the whole correlation response because one linked entity is missing.
   - Preserve the rest of the correlation group.

6. **Expose API endpoint**
   - Add or extend an endpoint for:
     - fetching grouped timeline data by selected activity item or `correlationId`
   - Keep route naming consistent with existing API conventions.
   - Return appropriate status codes:
     - `404` if the selected activity item itself is not found or not visible in tenant scope
     - `200` with partial unavailable states for missing linked entities
     - `403` only if that is the established pattern; otherwise prefer not-found masking for unauthorized tenant data

7. **Build the Blazor drill-down panel**
   - Extend the activity timeline UI so that:
     - events with the same `correlationId` appear as a grouped timeline view
     - selecting an activity item updates a side panel/detail panel asynchronously
     - no full page reload occurs
   - Recommended UI behavior:
     - left/main timeline list with grouped activity items
     - right/detail panel showing:
       - selected event summary
       - related linked entities and statuses
       - correlated event sequence
       - visible unavailable badges/messages for missing/deleted items
   - Use existing Blazor patterns in the repo:
     - component state + async data loading
     - loading indicators
     - empty states
     - error boundaries or safe error rendering

8. **Optimize for responsiveness**
   - Keep the selected-item drill-down query fast:
     - avoid over-fetching large payloads
     - paginate only if existing UX requires it, otherwise return enough for the correlation group
     - consider lightweight summary projections for related events
   - If the app already uses caching for read-heavy dashboard queries, only reuse it if safe and simple; do not add risky cache invalidation complexity unless already established.

9. **Add tests**
   - Add automated coverage for:
     - grouping events by shared `correlationId`
     - selecting an item returns linked task/workflow/approval/tool execution records
     - missing/deleted linked entity returns unavailable state and does not break response
     - cross-tenant data is excluded
     - unauthorized linked entities are not exposed
     - performance-sensitive query path avoids pathological N+1 behavior where testable
   - If there are integration test fixtures, add a large fixture/perf-oriented test or at least a bounded query test around 10,000 related events.
   - Prefer deterministic tests over brittle timing assertions, but include a performance validation path if the test suite already supports it.

10. **Keep implementation aligned with existing architecture**
   - Use CQRS-lite query handlers
   - Keep business logic out of controllers/components
   - Keep UI consuming API/application DTOs only
   - Preserve modular boundaries and tenant-aware access patterns

# Validation steps
1. **Code discovery**
   - Inspect existing activity/audit timeline implementation and identify extension points.
   - Confirm where `correlationId` is stored and whether indexes already exist.

2. **Build**
   - Run:
     - `dotnet build`

3. **Automated tests**
   - Run:
     - `dotnet test`

4. **Functional verification**
   - Start the app and navigate to the activity timeline UI.
   - Verify:
     - events with same `correlationId` are grouped together
     - clicking/selecting an activity item updates the drill-down panel without full page reload
     - linked task/workflow/approval/tool execution entities display current statuses
     - missing/deleted linked entities render a visible unavailable state
     - the rest of the correlation view still renders when one linked entity is unavailable

5. **Security verification**
   - Test with at least:
     - user in tenant A
     - user in tenant B
     - user with limited permissions inside same tenant
   - Verify:
     - no cross-tenant correlated entities appear
     - unauthorized linked entities are hidden or marked per existing app rules
     - direct API calls cannot retrieve another tenant’s correlation group

6. **Performance verification**
   - Using fixture/seeding approach consistent with the repo, validate a single selected activity item correlation query against up to 10,000 related events.
   - Confirm end-to-end query execution is under 1 second in the intended performance test path.
   - If exact automated timing is unstable, document measured results and ensure query plan/indexing is sound.

7. **Regression check**
   - Verify existing audit trail/activity timeline pages still work for non-correlated items.
   - Verify no full-page navigation regressions were introduced in the Blazor UI.

# Risks and follow-ups
- **Schema/index uncertainty:** `correlationId` may not yet be consistently persisted on the activity source table(s). If so, add the smallest safe schema/query change and document it.
- **Authorization complexity:** linked entities may each have different visibility rules. Reuse existing authorization services to avoid accidental overexposure.
- **N+1 query risk:** naive per-event entity lookups will fail the performance target. Batch by entity type and project summaries only.
- **UI state complexity:** async selection in Blazor can introduce stale selection/loading races. Guard against double-click/reload issues and cancel/ignore outdated requests if needed.
- **Deleted vs unauthorized ambiguity:** keep response semantics explicit enough for UI rendering, but do not leak sensitive existence details across authorization boundaries if current conventions mask them.
- **Follow-up candidates (do not implement unless necessary):**
  - add reusable correlation timeline component for dashboard/audit reuse
  - add DB indexes/migration hardening for correlation-heavy queries
  - add server-side pagination/virtualization if correlation groups can exceed practical UI rendering limits
  - add observability around correlation query latency and result sizes