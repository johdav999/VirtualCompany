# Goal
Implement `TASK-3.5.4` for **US-3.5 Dashboard performance, caching, and refresh behavior** by adding a production-aligned performance test suite and telemetry for executive cockpit endpoints, plus the minimum backend/frontend changes needed to satisfy the acceptance criteria.

The coding agent should:
- measure and enforce cockpit endpoint performance in a staging-like performance environment,
- ensure dashboard cache keys are correctly scoped,
- instrument cache invalidation latency and cache effectiveness,
- support and measure partial widget refresh behavior in the Blazor frontend,
- expose observability signals for endpoint latency, cache hit rate, and widget render timing.

Do not redesign the product architecture. Extend the existing modular monolith using the current .NET stack and repository structure.

# Scope
In scope:
- Executive cockpit backend query endpoints and any supporting application/infrastructure services used by dashboard widgets.
- Redis-backed cache key composition for dashboard data.
- Cache invalidation hooks for relevant domain updates:
  - task updates,
  - workflow updates,
  - approval updates,
  - agent status updates.
- Telemetry instrumentation using the project’s existing logging/metrics/tracing approach, preferably OpenTelemetry-compatible if already present.
- Performance test suite for cached dashboard requests and widget refresh scenarios.
- Blazor Web support for partial widget refresh without full shell reload.
- Observability/dashboard definitions or documented queries for:
  - endpoint latency,
  - cache hit rate,
  - widget render timing,
  - invalidation lag.

Out of scope:
- Broad redesign of dashboard UX.
- New infrastructure platforms.
- Full mobile performance work.
- Reworking unrelated modules unless required for cache invalidation integration.
- Premature microservice extraction.

Implementation must preserve:
- tenant isolation,
- role-aware data access,
- department/time-range filter semantics,
- CQRS-lite separation where already used.

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely backend/API/application/infrastructure files:
- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Domain/**` only if domain events/contracts are needed for invalidation triggers

Likely frontend files:
- `src/VirtualCompany.Web/**`

Likely test files:
- `tests/VirtualCompany.Api.Tests/**`
- add a dedicated performance/integration test project only if the solution structure clearly supports it

Likely docs/config files:
- `README.md`
- any existing telemetry, OpenTelemetry, appsettings, dashboard, or performance-test config files
- any docker-compose / local perf harness files if already present

Specifically look for and reuse existing implementations of:
- dashboard/cockpit endpoints,
- Redis caching abstractions,
- query handlers for cockpit widgets,
- domain event or outbox handlers,
- telemetry registration,
- Blazor widget components and dashboard shell,
- test host / integration test infrastructure.

# Implementation plan
1. **Discover existing cockpit flow and telemetry primitives**
   - Find the executive cockpit/dashboard endpoints, query handlers, DTOs, and Blazor page/components.
   - Find current cache abstractions and Redis usage.
   - Find any existing metrics/tracing/logging setup.
   - Find how task/workflow/approval/agent updates are persisted and whether domain events, notifications, or outbox messages already exist.

2. **Define a canonical dashboard cache key strategy**
   - Implement or refactor cache key generation so keys are explicitly scoped by:
     - tenant/company,
     - user role,
     - department filters,
     - time range,
     - widget/query identity.
   - If user-specific authorization affects visible data beyond role, include the minimum additional discriminator needed without exploding cardinality unnecessarily.
   - Centralize key generation in one reusable service/helper to avoid drift across endpoints.
   - Add unit tests for key composition.

3. **Instrument cache behavior**
   - Add metrics and structured logs for:
     - cache hit,
     - cache miss,
     - cache set,
     - cache invalidation,
     - invalidation lag,
     - cache lookup duration.
   - Tag metrics with low-cardinality dimensions only, such as:
     - endpoint/query name,
     - widget name,
     - tenant presence flag or hashed tenant bucket if needed,
     - result status,
     - cache outcome.
   - Do **not** emit raw tenant IDs or user IDs as high-cardinality metric labels.
   - If tracing exists, add spans/activities around:
     - cockpit endpoint execution,
     - cache lookup,
     - aggregate query execution,
     - widget refresh request handling.

4. **Add endpoint latency telemetry**
   - Instrument executive cockpit endpoints to report:
     - request duration,
     - cached vs uncached path,
     - response payload size if feasible,
     - success/failure.
   - Ensure p95 latency can be derived from emitted metrics in staging/perf environments.
   - Prefer histogram metrics for latency.

5. **Implement cache invalidation hooks**
   - Identify relevant write paths for:
     - tasks,
     - workflows,
     - approvals,
     - agent status.
   - On relevant updates, trigger dashboard cache invalidation through existing eventing/background mechanisms where possible.
   - Prefer targeted invalidation by tenant and affected dashboard scopes over global flushes.
   - If exact key enumeration is not currently feasible, implement a safe versioned namespace or invalidation token strategy per tenant/filter scope.
   - Ensure invalidation completes within 60 seconds after relevant updates.
   - Add telemetry for:
     - update event timestamp,
     - invalidation completion timestamp,
     - lag in seconds.
   - If asynchronous invalidation is used, make the path observable and retryable.

6. **Support partial widget refresh in Blazor**
   - Inspect current dashboard shell/page composition.
   - Refactor widgets so individual widgets can refresh their own data without reloading the full dashboard shell/page.
   - Use component-level loading/error states.
   - Ensure refresh requests call widget-specific or parameterized data endpoints rather than forcing a full dashboard reload.
   - Add client-side timing instrumentation for widget render/refresh duration.
   - If there is already JS interop or browser performance instrumentation, extend it; otherwise add a minimal, maintainable timing mechanism.

7. **Add frontend widget render timing telemetry**
   - Capture at least:
     - widget refresh start,
     - data fetch completion,
     - render completion,
     - success/failure.
   - Send timing data through the existing telemetry pipeline if available; otherwise log via API endpoint or existing browser telemetry mechanism already used by the app.
   - Keep payloads small and avoid noisy per-user high-cardinality dimensions.

8. **Create performance tests**
   - Add automated performance tests for executive cockpit cached requests in a staging-like/perf configuration.
   - Cover:
     - initial cached dashboard load,
     - widget-level partial refresh,
     - cache hit rate visibility,
     - invalidation after relevant updates.
   - The tests should verify or at minimum assert/report:
     - p95 under 2.5 seconds for cached requests,
     - cache key scoping behavior,
     - invalidation within 60 seconds,
     - partial widget refresh path works without full shell reload.
   - If true load/perf tests cannot run inside normal `dotnet test`, add:
     - a dedicated perf test harness,
     - clear commands,
     - environment assumptions,
     - machine-readable output.
   - Reuse existing integration test infrastructure where possible.

9. **Add observability dashboard artifacts or documentation**
   - Provide dashboard definitions, sample queries, or setup docs for:
     - cockpit endpoint latency,
     - cache hit rate,
     - invalidation lag,
     - widget render timing.
   - If the repo already stores observability assets, place them there.
   - Otherwise document the exact metric names, dimensions, and example charts in markdown.

10. **Harden and document**
   - Update README or relevant docs with:
     - how to run perf tests,
     - required environment variables,
     - expected telemetry outputs,
     - how to validate acceptance criteria in staging/perf.

Implementation notes:
- Favor additive changes over broad rewrites.
- Keep metric names consistent and stable.
- Keep logs structured and correlation-friendly.
- Preserve tenant isolation in both cache and telemetry.
- Avoid introducing high-cardinality metric labels.
- If there is no existing widget-specific endpoint structure, introduce the smallest viable API shape to support partial refresh cleanly.

# Validation steps
1. **Restore/build/test**
   - Run:
     - `dotnet build`
     - `dotnet test`
   - Fix any compile/test regressions.

2. **Backend cache key validation**
   - Add/execute automated tests proving cache keys differ appropriately for:
     - different tenants,
     - different user roles,
     - different department filters,
     - different time ranges.
   - Confirm identical requests within the same scope reuse the same key.

3. **Invalidation validation**
   - Add/execute tests that simulate updates to:
     - tasks,
     - workflows,
     - approvals,
     - agent status.
   - Verify cache invalidation is triggered and completes within the required window.
   - Verify stale cached dashboard data is not served beyond the invalidation tolerance.

4. **Partial widget refresh validation**
   - Verify a widget can refresh independently without reloading the full dashboard shell.
   - Add component/integration tests if feasible.
   - Confirm shell state remains intact during widget refresh.

5. **Telemetry validation**
   - Confirm metrics/logs/traces are emitted for:
     - endpoint latency,
     - cache hit/miss rate,
     - invalidation lag,
     - widget render timing.
   - Validate metric names and dimensions are documented and observable.

6. **Performance validation**
   - Run the performance suite against the intended environment/profile.
   - Capture p95 for cached executive cockpit initial load.
   - Ensure results clearly show pass/fail against the 2.5 second target.
   - Persist or print a concise summary artifact.

7. **Acceptance criteria traceability**
   - In the final implementation notes or PR description, map each acceptance criterion to:
     - code changes,
     - tests,
     - telemetry/dashboard evidence.

# Risks and follow-ups
- **Unknown existing telemetry stack:** If OpenTelemetry or metrics plumbing is incomplete, implement minimal compatible instrumentation without blocking delivery, and document any gaps.
- **Cache invalidation complexity:** Exact key invalidation may be difficult if current cache usage is ad hoc. A tenant-scoped version token strategy may be safer than brittle key scanning.
- **High-cardinality telemetry risk:** Avoid tenant/user IDs in metric tags; use logs/traces for detailed diagnostics.
- **Perf test realism:** Local `dotnet test` may not represent staging performance. Provide a dedicated perf profile and clearly separate correctness tests from environment-dependent performance assertions.
- **Blazor refresh behavior:** Depending on current page architecture, partial widget refresh may require moderate component refactoring. Keep the shell stable and isolate changes to widget boundaries.
- **Event propagation lag:** If invalidation relies on background workers/outbox, ensure retries and lag telemetry exist so the 60-second SLA is measurable.
- **Follow-up likely needed:** After implementation, a separate task may be required to tune slow SQL/aggregation queries if telemetry reveals backend bottlenecks preventing the p95 target.