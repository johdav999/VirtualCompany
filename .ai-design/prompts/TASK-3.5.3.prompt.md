# Goal
Implement `TASK-3.5.3` for **US-3.5 Dashboard performance, caching, and refresh behavior** by enabling **partial widget refresh and loading states in the executive cockpit UI**, while wiring backend/dashboard query behavior to support scoped caching, refresh invalidation, and performance observability.

This task must satisfy these outcomes:

- Executive cockpit supports **refreshing individual widgets without reloading the full dashboard shell**.
- Widgets show clear **loading, success, and error states** during initial load and partial refresh.
- Dashboard data APIs use **cache keys scoped by tenant, user role, department filters, and time range**.
- Relevant updates to tasks, workflows, approvals, and agent status trigger **cache invalidation within 60 seconds**.
- Performance instrumentation exists for:
  - endpoint latency
  - cache hit rate
  - widget render timing
- Changes align with the existing **Blazor Web App + ASP.NET Core modular monolith + Redis cache** architecture.

# Scope
In scope:

- Executive cockpit web UI in `src/VirtualCompany.Web`
- Dashboard/cockpit query endpoints and application query handlers
- Redis-backed cache key composition and invalidation hooks
- Partial widget refresh interaction model
- Widget-level loading/error/empty states
- Observability/metrics for dashboard endpoints and widget rendering
- Automated tests for cache scoping, refresh behavior, and invalidation wiring
- Performance validation support for staging

Out of scope unless required by existing code patterns:

- Full redesign of cockpit layout or visual system
- Mobile app changes
- New dashboard business widgets beyond what already exists
- Large-scale architecture refactors
- Replacing current caching stack
- Building a full synthetic load platform from scratch

Implementation should prefer incremental changes that fit current project structure and conventions.

# Files to touch
Inspect the solution first and update the exact files that match existing patterns. Likely areas include:

- `src/VirtualCompany.Web/**`
  - executive cockpit pages/components
  - widget components
  - shared loading/error UI components
  - any client-side service used to fetch dashboard/widget data
- `src/VirtualCompany.Api/**`
  - dashboard/cockpit controllers or minimal API endpoints
  - observability/metrics registration
- `src/VirtualCompany.Application/**`
  - dashboard query handlers/services
  - cache key builders
  - DTOs/view models for widget payloads
- `src/VirtualCompany.Infrastructure/**`
  - Redis cache implementation
  - cache invalidation subscribers/services
  - metrics/telemetry plumbing
- `src/VirtualCompany.Domain/**`
  - only if domain events or contracts are needed for invalidation triggers
- `src/VirtualCompany.Shared/**`
  - shared contracts if dashboard widget responses are shared across layers
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint tests
  - cache scoping/invalidation tests
- Add tests in other test projects if they already exist for Web/Application/Infrastructure

Also review:

- `README.md`
- any existing docs for architecture, observability, caching, or dashboard behavior
- any existing performance test assets or scripts in the repo

# Implementation plan
1. **Discover current dashboard architecture before changing anything**
   - Find the executive cockpit page, widget components, and how data is currently loaded.
   - Identify whether the dashboard currently:
     - loads as one aggregated payload,
     - loads per widget,
     - or mixes SSR and interactive fetches.
   - Identify existing caching abstractions, Redis usage, and any event/domain-event/outbox hooks that can support invalidation.
   - Identify existing telemetry stack:
     - OpenTelemetry
     - `System.Diagnostics.Metrics`
     - Application Insights
     - Prometheus-style metrics
     - logging conventions

2. **Design a widget refresh model that preserves the dashboard shell**
   - Keep the dashboard shell/page mounted.
   - Introduce a widget-level refresh contract so each widget can independently request fresh data.
   - Prefer one of these approaches based on current codebase patterns:
     - a dashboard endpoint per widget, or
     - a shared dashboard endpoint with widget-specific query parameters and partial fetch support.
   - Ensure refreshing one widget does not trigger a full page navigation or full dashboard rerender.
   - Preserve current filters such as department and time range across refreshes.

3. **Add widget state management in the Blazor UI**
   - Each widget should explicitly support:
     - initial loading
     - refresh loading
     - success
     - empty
     - error
   - During refresh, keep prior successful data visible when possible and show a non-blocking loading indicator.
   - Add a refresh affordance per widget if not already present.
   - If there is a dashboard-wide refresh, ensure it orchestrates widget refreshes without reloading the shell.
   - Avoid introducing unnecessary JS unless required; prefer idiomatic Blazor patterns.

4. **Refactor dashboard data contracts for partial refresh**
   - Define or update DTOs so widget payloads can be fetched independently.
   - Include metadata if useful, such as:
     - widget key
     - generated timestamp
     - cache status if already exposed internally
   - Keep contracts tenant-safe and role-aware.

5. **Implement scoped cache keys**
   - Introduce or update a cache key builder for dashboard/widget queries.
   - Cache keys must include at minimum:
     - tenant/company identifier
     - user role or effective authorization scope
     - department filters
     - time range
     - widget identifier or query shape
   - Normalize filter values to avoid cache fragmentation from equivalent inputs.
   - Ensure no cross-tenant or cross-role cache leakage is possible.

6. **Implement cache invalidation hooks**
   - Identify relevant update flows for:
     - tasks
     - workflows
     - approvals
     - agent status
   - Hook invalidation into the most reliable existing mechanism, preferring:
     - domain events
     - application events
     - outbox/background processing
   - Invalidate affected dashboard/widget cache entries within 60 seconds of relevant updates.
   - If exact key invalidation is difficult with current abstractions, implement safe prefix/tag/version invalidation consistent with existing Redis patterns.
   - Document any tradeoffs.

7. **Add observability and performance metrics**
   - Instrument dashboard endpoints for:
     - request latency
     - cache hit/miss rate
   - Instrument widget rendering/refresh timing in the web app where feasible.
   - Use existing telemetry conventions; do not invent a parallel system.
   - Ensure metrics can support acceptance criteria reporting in staging.
   - Add structured logs with tenant-safe context and correlation IDs if already used in the platform.

8. **Preserve or improve initial load performance**
   - Review whether initial dashboard load is blocked by sequential widget fetches.
   - If needed, parallelize widget loading or optimize the aggregate query path.
   - Avoid over-fetching data for widgets not visible or not needed on first render.
   - Keep cached requests optimized to support the p95 under 2.5s target in staging.

9. **Add tests**
   - Add backend tests for cache key scoping:
     - different tenant => different key
     - different role => different key
     - different department filter => different key
     - different time range => different key
   - Add tests for invalidation behavior after relevant updates.
   - Add endpoint tests for widget-specific refresh paths.
   - Add UI/component tests if the repo already uses a Blazor component testing pattern; otherwise cover behavior at the API/service level and keep UI logic simple.
   - Add telemetry-related tests where practical, especially around metric emission hooks or cache hit/miss instrumentation.

10. **Document implementation assumptions**
   - If the current codebase lacks a complete eventing/invalidation mechanism, implement the smallest reliable version and note follow-up work.
   - If staging performance tests are external to the repo, add/update scripts, docs, or instructions so the task is verifiable.

11. **Keep changes aligned with architecture**
   - Respect modular boundaries:
     - Web for presentation/interactivity
     - Api for transport
     - Application for query/use-case logic
     - Infrastructure for Redis/telemetry implementations
   - Do not bypass application services from UI.
   - Do not couple widgets directly to database access.

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Verify cockpit UI behavior manually:
   - Open executive cockpit.
   - Confirm initial shell loads without full-page reload loops.
   - Refresh a single widget and confirm:
     - only that widget enters loading state
     - other widgets remain interactive/visible
     - shell does not reload
   - Confirm empty/error states render correctly.
   - Confirm prior widget data remains visible during refresh if implemented.

3. Verify cache key scoping:
   - Confirm generated cache keys differ for:
     - different tenant
     - different user role
     - different department filter
     - different time range
   - Confirm equivalent normalized filters produce stable keys.

4. Verify invalidation:
   - Trigger representative updates for:
     - task
     - workflow
     - approval
     - agent status
   - Confirm affected dashboard/widget cache entries are invalidated or refreshed within 60 seconds.

5. Verify observability:
   - Confirm metrics/logs exist for:
     - dashboard endpoint latency
     - cache hit/miss or hit rate
     - widget render/refresh timing
   - Confirm telemetry is visible through the project’s existing observability pipeline or test hooks.

6. Verify performance readiness:
   - Run any existing performance tests or add/update a lightweight repeatable validation path.
   - Confirm cached dashboard initial load is positioned to meet the **under 2.5 seconds p95** target in staging.
   - If exact staging validation cannot be run locally, document how to execute it and what metrics to inspect.

7. Regression check:
   - Confirm tenant scoping and authorization still apply to all dashboard/widget endpoints.
   - Confirm no full dashboard shell reload occurs during partial widget refresh.
   - Confirm no cross-widget state corruption.

# Risks and follow-ups
- The current dashboard may be implemented as a single aggregate payload, making true partial refresh require endpoint and component refactoring.
- Exact cache invalidation may be difficult if current Redis usage lacks tagging/versioning; a safe coarse invalidation strategy may be necessary first.
- Widget render timing in Blazor may require pragmatic instrumentation rather than perfect browser-performance tracing.
- Achieving the p95 target may depend on broader query/index optimization outside this task’s direct UI scope.
- If no existing performance test harness exists, add minimal support now and note a follow-up for fuller staging load automation.
- If invalidation currently depends on eventual background processing, ensure the implementation still meets the 60-second acceptance threshold.
- Document any remaining gaps clearly in the final summary, especially if additional indexing, query tuning, or observability dashboard configuration is needed.