# Goal
Implement backlog task **TASK-3.5.1 — Add cache layer for dashboard aggregates and widget payloads** for story **US-3.5 Dashboard performance, caching, and refresh behavior** in the existing .NET solution.

The coding agent should add a production-ready Redis-backed caching layer for executive cockpit dashboard queries and widget payload endpoints, with scoped cache keys, event-driven invalidation, partial widget refresh support, and performance/observability coverage.

The implementation must satisfy these acceptance criteria:

- Executive cockpit initial load completes in under **2.5 seconds at p95 for cached requests** in the staging performance environment.
- Dashboard data endpoints use cache keys scoped by **tenant, user role, department filters, and time range**.
- Cache invalidation occurs within **60 seconds** after relevant **task, workflow, approval, or agent status** updates.
- Frontend supports **partial widget refresh** without reloading the full dashboard shell.
- Performance tests and observability dashboards report **endpoint latency, cache hit rate, and widget render timing**.

# Scope
In scope:

- Backend cache abstraction and Redis-backed implementation for dashboard aggregate queries and widget payloads.
- Cache key design that includes:
  - tenant/company
  - effective user role
  - department filters
  - time range
  - widget or dashboard query identity
- Dashboard query handlers/endpoints updated to use cache-aside behavior.
- Invalidation strategy triggered by relevant domain/application updates:
  - tasks
  - workflows
  - approvals
  - agent status/activity affecting dashboard widgets
- Frontend changes in Blazor Web to:
  - load dashboard shell independently from widget data
  - refresh individual widgets without full page reload
  - surface loading/error state per widget
- Observability:
  - structured logs
  - metrics for cache hit/miss, endpoint latency, widget render timing
- Tests:
  - unit tests for cache key generation and invalidation behavior
  - integration/API tests for cached dashboard endpoints
  - performance test hooks or scripts if a performance test project already exists

Out of scope unless already trivially supported by existing patterns:

- Re-architecting the entire dashboard domain.
- Adding a new message broker.
- Full mobile caching behavior.
- Broad refactors unrelated to dashboard performance.
- Building a full custom observability platform; only instrument what the app can emit now.

Assume the architecture is a **modular monolith** with **ASP.NET Core**, **Blazor Web App**, **PostgreSQL**, and **Redis**. Prefer clean application/infrastructure boundaries and CQRS-lite patterns already present in the solution.

# Files to touch
Inspect the solution first and then update the most relevant files. Expect to touch files in these areas:

- `src/VirtualCompany.Api`
  - dashboard/cockpit endpoints or controllers
  - DI/service registration
  - telemetry/metrics wiring if API owns it
- `src/VirtualCompany.Application`
  - dashboard query handlers/services
  - cache contracts/interfaces
  - invalidation orchestration
  - DTOs/query models for widget payloads
- `src/VirtualCompany.Infrastructure`
  - Redis cache implementation
  - cache key builder
  - invalidation publisher/subscriber or background invalidation support
  - observability metric emitters if infra owns them
- `src/VirtualCompany.Web`
  - executive cockpit page/components
  - widget components
  - partial refresh logic
  - client-side timing instrumentation
- `src/VirtualCompany.Domain`
  - only if needed for domain events or event contracts already used by task/workflow/approval/agent modules
- `tests/VirtualCompany.Api.Tests`
  - endpoint/integration tests
  - cache behavior tests
- Potentially:
  - `README.md`
  - appsettings files for Redis/cache settings
  - any existing dashboard docs or architecture notes

Do not invent random files if equivalent files already exist. Reuse existing naming, folder structure, DI patterns, MediatR/CQRS conventions, and telemetry conventions.

# Implementation plan
1. **Discover existing dashboard flow and caching primitives**
   - Inspect current executive cockpit implementation in API/Application/Web.
   - Identify:
     - current dashboard endpoints
     - current widget composition model
     - whether Redis is already configured
     - whether there is an existing cache abstraction
     - whether domain/application events already exist for task/workflow/approval/agent updates
   - Summarize findings in code comments only where useful; do not add unnecessary docs.

2. **Define a dashboard cache abstraction**
   - Add an application-level interface for dashboard caching, such as:
     - get/set cached dashboard aggregate payloads
     - get/set cached widget payloads
     - invalidate by tenant and affected dashboard scope
   - Keep the abstraction generic enough for dashboard use, but do not over-generalize into a platform-wide cache framework unless one already exists.

3. **Implement scoped cache key generation**
   - Create a deterministic cache key builder for dashboard data.
   - Keys must include at minimum:
     - company/tenant id
     - effective user role
     - department filter set
     - time range
     - widget identifier or dashboard aggregate identifier
     - version segment for future invalidation/schema changes
   - Normalize filter ordering to avoid duplicate keys for equivalent requests.
   - Avoid including raw PII in keys.
   - If user-specific visibility materially affects payloads beyond role, inspect existing authorization behavior and include the minimum additional scope needed to prevent data leakage.

4. **Add Redis-backed cache implementation**
   - Use Redis via existing infrastructure patterns.
   - Implement cache-aside reads for expensive dashboard aggregate and widget queries.
   - Add sensible TTLs aligned with the invalidation requirement:
     - enough to improve performance
     - not so long that stale data persists if invalidation fails
   - Prefer JSON serialization already used in the solution.
   - Handle Redis failures gracefully:
     - log
     - emit miss/failure metrics
     - fall back to source query without breaking the request

5. **Update dashboard endpoints/query handlers**
   - Wrap expensive executive cockpit aggregate queries with the cache layer.
   - If the current API returns one large dashboard payload, preserve compatibility if possible but also add or expose widget-specific endpoints for partial refresh.
   - Ensure widget endpoints can independently fetch:
     - approvals
     - alerts
     - KPI cards
     - recent activity
     - daily briefing
     - or whatever widgets currently exist
   - Keep tenant scoping and authorization intact.

6. **Implement partial widget refresh in Blazor Web**
   - Refactor the dashboard page so the shell/layout loads once and widgets fetch independently.
   - Each widget should support:
     - initial async load
     - manual refresh
     - optional timed refresh if already supported by UX
     - isolated loading/error state
   - Do not reload the full dashboard shell when one widget refreshes.
   - Preserve existing UX and routing as much as possible.

7. **Add invalidation on relevant updates**
   - Hook invalidation into the application flow for updates to:
     - tasks
     - workflows
     - approvals
     - agent status or health inputs used by dashboard widgets
   - Prefer event-driven invalidation using existing domain events, notifications, outbox, or application event handlers.
   - Invalidation must occur within 60 seconds of relevant updates.
   - If exact key invalidation is difficult because of filter combinations, use a safe strategy such as:
     - tenant-scoped version tokens
     - per-widget tenant version segments
     - coarse-grained tenant+domain invalidation namespaces
   - Favor correctness and bounded staleness over overly clever selective invalidation.

8. **Instrument observability**
   - Add metrics/logging for:
     - dashboard endpoint latency
     - widget endpoint latency
     - cache hit count
     - cache miss count
     - cache error count
     - widget render timing on the frontend
   - Use existing telemetry stack if present; otherwise use standard .NET metrics/logging primitives.
   - Include dimensions/tags where appropriate:
     - endpoint/widget name
     - tenant optional only if safe and already allowed
     - cache result
   - Keep cardinality under control.

9. **Add tests**
   - Unit tests:
     - cache key builder normalization and scoping
     - invalidation/version token behavior
   - Integration/API tests:
     - cached endpoint returns same payload shape as uncached
     - cache hit path works after first request
     - invalidation causes refreshed data after relevant update
     - tenant/role/filter/time-range scoping prevents cross-scope reuse
   - Frontend/component tests if the project already has a pattern for them; otherwise keep frontend validation lightweight and rely on API tests plus manual validation notes.

10. **Add configuration**
   - Add cache settings to appsettings/options:
     - enable/disable flag
     - TTLs
     - key prefix/version
   - Ensure safe defaults for local development.
   - Do not hardcode connection strings or environment-specific values.

11. **Preserve backward compatibility**
   - Avoid breaking existing dashboard consumers.
   - If introducing new widget endpoints, keep the existing aggregate endpoint unless clearly unused.
   - If response contracts must change, make the smallest additive change possible.

12. **Document implementation assumptions in code**
   - Add concise comments where invalidation strategy or cache scoping is non-obvious.
   - Do not add excessive prose.

Implementation guidance:

- Prefer **cache-aside** over write-through.
- Prefer **tenant-scoped versioned invalidation** if selective invalidation across many filter combinations is too complex.
- Keep all dashboard data access tenant-safe.
- Avoid caching authorization failures or user-specific transient errors.
- Ensure serialization is stable and efficient.
- If there is already a dashboard application service, centralize caching there rather than scattering it across controllers.

# Validation steps
Run and report results for the relevant commands that exist:

1. Restore/build:
   - `dotnet build`

2. Tests:
   - `dotnet test`

3. If there are targeted test projects or filters for dashboard/API tests, run those too and report them.

4. Manual/backend validation:
   - Verify first request to a dashboard/widget endpoint populates cache.
   - Verify second equivalent request hits cache.
   - Verify changing any of these changes the cache key/result scope:
     - tenant
     - user role
     - department filter
     - time range
   - Verify relevant updates to task/workflow/approval/agent status invalidate or bypass stale cache within 60 seconds.

5. Manual/frontend validation:
   - Open executive cockpit.
   - Refresh a single widget and confirm the shell does not reload.
   - Confirm per-widget loading/error states behave correctly.
   - Confirm widget render timing instrumentation fires.

6. Performance validation:
   - If a performance test harness exists, run it.
   - Otherwise add a lightweight repeatable benchmark or test note for staging verification.
   - Capture enough evidence that cached requests are positioned to meet the **under 2.5s p95** target in staging.

7. Observability validation:
   - Confirm metrics/logs are emitted for:
     - endpoint latency
     - cache hit/miss/error
     - widget render timing

In the final implementation summary, include:
- files changed
- invalidation strategy chosen
- cache key format
- test coverage added
- any known gaps for staging/perf environment verification

# Risks and follow-ups
- **Risk: over-broad invalidation reduces cache effectiveness.**
  - Mitigation: use per-tenant/per-widget version tokens rather than global flushes where possible.

- **Risk: under-scoped keys could leak data across tenants or roles.**
  - Mitigation: explicitly include tenant and role scope; inspect whether user-specific visibility requires additional scoping.

- **Risk: Redis outages degrade dashboard availability.**
  - Mitigation: graceful fallback to source queries and emit cache error metrics.

- **Risk: partial refresh introduces inconsistent widget timestamps/data freshness.**
  - Mitigation: include fetched-at metadata per widget if useful and keep refresh behavior explicit.

- **Risk: frontend changes may tightly couple widgets to API contracts.**
  - Mitigation: keep widget DTOs stable and additive.

- **Risk: acceptance criterion for p95 latency cannot be fully proven locally.**
  - Mitigation: implement instrumentation and test hooks so staging can validate it quickly.

Follow-ups to note if not fully completed in this task:
- staging load/performance run to confirm p95 target
- observability dashboard panels/alerts in the deployed environment
- broader cache strategy reuse for other analytics/query surfaces
- optional stale-while-revalidate optimization if needed later