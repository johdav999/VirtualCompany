# Goal
Implement **TASK-3.5.2 — event-driven cache invalidation for cockpit data sources** for **US-3.5 Dashboard performance, caching, and refresh behavior** in the existing .NET solution.

Deliver a production-ready implementation that:
- Adds tenant-safe, dimension-scoped caching for executive cockpit/dashboard data endpoints.
- Invalidates affected cache entries in response to relevant domain updates using an event-driven approach.
- Enables frontend partial widget refresh without reloading the full dashboard shell.
- Adds performance/observability instrumentation for endpoint latency, cache hit rate, and widget render timing.
- Includes automated tests covering cache key scoping, invalidation behavior, and partial refresh flows.

Use the existing architecture constraints:
- ASP.NET Core modular monolith
- PostgreSQL as source of truth
- Redis for caching/ephemeral coordination
- Event-driven internal workflows via outbox/background processing
- Multi-tenant isolation enforced everywhere

Success must align with these acceptance criteria:
- Executive cockpit initial load completes in under 2.5 seconds at p95 for cached requests in staging perf env.
- Dashboard data endpoints use cache keys scoped by tenant, user role, department filters, and time range.
- Cache invalidation occurs within 60 seconds after relevant task, workflow, approval, or agent status updates.
- Frontend supports partial widget refresh without reloading the full dashboard shell.
- Performance tests and observability dashboards report endpoint latency, cache hit rate, and widget render timing.

# Scope
In scope:
- Backend cache abstraction/extensions for cockpit/dashboard query results.
- Cache key design including:
  - tenant/company
  - human role
  - department filter(s)
  - time range
  - widget/query identity
- Event publication/handling for invalidation triggers from:
  - task updates
  - workflow updates
  - approval updates
  - agent status updates
- Redis-backed invalidation coordination using a practical pattern suitable for the current modular monolith.
- API support for widget-level refresh endpoints or query contracts that allow partial refresh.
- Blazor dashboard updates so individual widgets can refresh independently.
- Metrics/logging/tracing for:
  - endpoint latency
  - cache hit/miss rate
  - invalidation processing timing
  - widget render timing
- Tests.

Out of scope unless required by existing code patterns:
- Re-architecting the entire dashboard module.
- Introducing a full external message broker.
- Broad redesign of all frontend state management.
- Premature microservice extraction.
- Non-cockpit caching unrelated to dashboard/cockpit data sources.

Implementation expectations:
- Prefer incremental changes that fit current project structure.
- Reuse existing CQRS-lite query handlers, outbox, background workers, and Redis infrastructure if present.
- If no event contracts exist yet for some entities, add minimal internal domain/application events needed for invalidation.
- Keep invalidation targeted where feasible; if exact key targeting is too invasive, implement safe namespace/version-based invalidation scoped to tenant + dashboard dimensions.

# Files to touch
Inspect the solution first, then update the relevant files. Expected areas include:

- `src/VirtualCompany.Api/**`
  - dashboard/cockpit endpoints
  - DI registration
  - observability/metrics wiring
- `src/VirtualCompany.Application/**`
  - cockpit/dashboard query handlers
  - cache interfaces/services
  - event handlers for invalidation
  - DTOs/contracts for widget refresh
- `src/VirtualCompany.Domain/**`
  - domain event definitions if needed
- `src/VirtualCompany.Infrastructure/**`
  - Redis cache implementation
  - outbox/event dispatcher integration
  - cache key builder/version store
  - metrics instrumentation plumbing
- `src/VirtualCompany.Web/**`
  - executive cockpit page/components
  - widget components
  - partial refresh behavior
  - widget render timing instrumentation
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - cache scoping tests
  - invalidation tests
- Potentially:
  - `README.md`
  - relevant docs under `docs/**` if there is an established implementation-notes pattern

Before editing, identify the actual existing dashboard/cockpit, caching, eventing, and observability files and adapt the plan to the real structure.

# Implementation plan
1. **Discover current implementation**
   - Inspect solution structure for:
     - executive cockpit/dashboard endpoints
     - query handlers/services used by dashboard widgets
     - existing Redis cache abstractions
     - existing outbox/event dispatcher/background worker patterns
     - existing domain/application events for tasks, workflows, approvals, agents
     - current Blazor dashboard shell and widget composition
     - existing metrics/logging/OpenTelemetry setup
   - Summarize findings in code comments only where useful; do not create unnecessary docs.

2. **Design cache key strategy**
   - Implement a consistent cache key builder for cockpit data.
   - Keys must include at minimum:
     - company/tenant id
     - user role
     - department filter(s), normalized deterministically
     - time range, normalized deterministically
     - widget/query name
     - optional version token/namespace
   - Example shape:
     - `cockpit:{tenantId}:{role}:{departmentsHash}:{timeRange}:{widget}:{version}`
   - Avoid embedding sensitive raw values if hashing is more appropriate.
   - Ensure deterministic ordering for multi-department filters.

3. **Implement cache namespace/version invalidation**
   - Prefer a robust pattern that avoids expensive wildcard deletes in Redis.
   - Recommended approach:
     - maintain version tokens per invalidation scope, such as:
       - tenant-wide cockpit version
       - tenant + department version where applicable
       - optionally widget family version
     - cached entries include the current version token in the key
     - invalidation increments the relevant version token(s)
   - This supports near-immediate logical invalidation without scanning Redis keys.
   - If current code already has tag-based invalidation, extend it instead of replacing it.

4. **Wire event-driven invalidation**
   - Identify relevant state changes in:
     - tasks
     - workflow instances
     - approvals
     - agents
   - Publish internal events when relevant updates occur, if not already published.
   - Add invalidation handlers that map events to affected cockpit scopes.
   - Minimum behavior:
     - any relevant update invalidates tenant cockpit cache namespace
   - Better behavior if practical:
     - invalidate tenant + affected department namespace
     - invalidate specific widget families when event-to-widget mapping is clear
   - Ensure processing occurs through the app’s existing reliable async mechanism:
     - outbox + background dispatcher if available
     - otherwise safe in-process async handling with clear TODO only if unavoidable
   - The implementation must support invalidation within 60 seconds.

5. **Cache dashboard query results**
   - Apply caching to expensive executive cockpit/dashboard queries.
   - Cache only query results, not authorization decisions.
   - Ensure all queries remain tenant-scoped before cache lookup/write.
   - Add TTLs appropriate for dashboard freshness, but rely on event invalidation for correctness.
   - Prevent cache stampede if practical:
     - use short-lived distributed lock/single-flight pattern if existing infra supports it
     - otherwise keep implementation simple and safe

6. **Add partial widget refresh API behavior**
   - Support frontend refresh of individual widgets without reloading the dashboard shell.
   - If current API returns one large dashboard payload, add widget-specific endpoints or query parameters for widget-level retrieval.
   - Keep contracts consistent and reusable.
   - Ensure widget endpoints use the same scoped cache strategy.

7. **Update Blazor frontend**
   - Refactor dashboard page/components so widgets can independently:
     - load
     - refresh
     - show loading/error states
   - Do not reload the full shell for a single widget refresh.
   - Preserve current UX where possible.
   - Add lightweight timing instrumentation around widget fetch/render completion.

8. **Add observability**
   - Instrument backend with metrics/tracing/logging for:
     - dashboard endpoint latency
     - widget endpoint latency
     - cache hit/miss counts or rate
     - invalidation event handling count and duration
   - Instrument frontend for widget render timing:
     - per widget load/refresh duration
   - Reuse existing OpenTelemetry / metrics stack if present.
   - Emit structured logs with tenant context where allowed by current conventions.

9. **Testing**
   - Add automated tests for:
     - cache key scoping by tenant, role, department filters, and time range
     - cache miss then hit behavior
     - invalidation after task/workflow/approval/agent updates
     - no cross-tenant cache leakage
     - partial widget refresh endpoint/component behavior
   - If integration tests exist, include Redis-backed or abstraction-level tests as appropriate.
   - Keep tests deterministic.

10. **Keep implementation clean**
   - Respect existing architecture boundaries:
     - controllers/endpoints thin
     - application layer owns query/invalidation logic
     - infrastructure owns Redis details
   - Avoid direct DB access from UI or cache handlers.
   - Keep naming explicit: “cockpit”, “dashboard”, “widget”, “cache invalidation”.

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Verify cache key scoping in tests:
   - same widget/query with different tenant ids yields different keys
   - same tenant but different roles yields different keys
   - same tenant/role but different department filters yields different keys
   - same tenant/role/departments but different time ranges yields different keys

3. Verify invalidation behavior:
   - seed cached dashboard/widget response
   - trigger relevant task update event
   - confirm subsequent request bypasses stale cached version and repopulates
   - repeat for workflow, approval, and agent status updates
   - confirm invalidation completes within expected async window in tests where applicable

4. Verify no full shell reload requirement:
   - confirm widget refresh path only re-fetches targeted widget data
   - confirm dashboard shell state remains intact
   - confirm loading/error state is widget-local

5. Verify observability:
   - confirm metrics are emitted for:
     - endpoint latency
     - cache hit/miss
     - invalidation processing
     - widget render timing
   - confirm logs/traces include correlation context consistent with existing conventions

6. If there is an existing perf test harness, run or extend it.
   - If not, add a minimal repeatable test or benchmark hook for cached dashboard/widget requests and document how to run it in code comments or existing test conventions.
   - Do not fabricate staging p95 locally; instead ensure instrumentation and testability support the acceptance criterion in staging.

# Risks and follow-ups
- **Risk: no existing event pipeline for some entity updates**
  - Add minimal internal events now.
  - Prefer outbox-backed dispatch if available to meet reliability expectations.

- **Risk: exact key deletion in Redis is expensive**
  - Use versioned namespace invalidation instead of wildcard scans.

- **Risk: dashboard currently built as one monolithic payload**
  - Introduce widget-level query endpoints incrementally while preserving existing aggregate endpoint if needed for compatibility.

- **Risk: frontend widget refresh may be tightly coupled to page lifecycle**
  - Refactor minimally toward component-level async loading with isolated state.

- **Risk: insufficient observability foundation**
  - Reuse existing telemetry stack; if absent, add minimal metrics hooks without overbuilding dashboards in code.

- **Risk: over-invalidation reduces cache efficiency**
  - Start with safe tenant-scoped invalidation if necessary, but structure code so department/widget-level invalidation can be tightened later.

Follow-ups to note in code/TODOs only if genuinely needed:
- finer-grained widget-to-event invalidation mapping
- cache stampede protection improvements
- staging performance validation and dashboard creation in ops tooling
- optional push-based refresh later via SignalR/websocket if product direction requires near-real-time updates