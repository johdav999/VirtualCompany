# Goal

Implement `TASK-12.1.7` for `ST-601 Executive cockpit dashboard` by adding Redis-backed caching for expensive executive dashboard aggregate queries in the .NET backend, while preserving tenant isolation, correctness, and safe fallback behavior when cache is unavailable.

# Scope

In scope:

- Identify the executive cockpit/dashboard aggregate query path(s) that are expensive and used to populate summary widgets such as:
  - pending approvals
  - alerts
  - KPI summary cards
  - recent activity summaries
  - daily briefing summary inputs if they share the same aggregate source
- Add Redis caching around those aggregate query results in the application/infrastructure layer, not in UI components.
- Ensure cache keys are tenant-scoped using `company_id` and any other relevant query dimensions.
- Add a clear cache TTL strategy appropriate for interactive dashboard use.
- Ensure cache failures do not break dashboard rendering; system must fall back to database queries.
- Add cache invalidation or cache-busting hooks for the most relevant write paths if practical; otherwise use conservative TTLs and document tradeoffs.
- Add tests for tenant isolation, cache hit/miss behavior, and fallback behavior.
- Add configuration wiring for Redis cache usage if not already present.

Out of scope unless required by existing code structure:

- Broad caching of unrelated modules
- UI redesign of the dashboard
- Introducing a new message broker or background invalidation system
- Premature optimization of every query in analytics/cockpit
- Reworking domain models or adding new product features beyond caching support

# Files to touch

Inspect first, then update only the necessary files. Likely candidates include:

- `src/VirtualCompany.Application/...`
  - dashboard/cockpit query handlers
  - query DTOs / view models
  - cache abstraction interfaces if needed
- `src/VirtualCompany.Infrastructure/...`
  - Redis cache implementation
  - DI registration
  - cache key helpers
  - resilience/fallback logic
- `src/VirtualCompany.Api/...`
  - service registration / configuration binding
  - endpoint wiring only if needed
- `src/VirtualCompany.Web/...`
  - only if the dashboard currently bypasses application query handlers and needs to call the cached path
- `tests/VirtualCompany.Api.Tests/...`
  - integration or API tests covering dashboard responses and cache behavior
- `README.md` or relevant docs/config samples
  - only if configuration/setup documentation needs updating

Also inspect for existing patterns before adding anything new:

- existing Redis usage
- existing `IDistributedCache` or `StackExchange.Redis` registration
- existing CQRS query handlers for dashboard/cockpit
- existing tenant context abstractions
- existing caching helpers or policies

# Implementation plan

1. **Locate the executive dashboard aggregate query flow**
   - Find the query/handler/controller/component used for `ST-601` dashboard data.
   - Identify which parts are expensive aggregate queries against PostgreSQL.
   - Prefer caching the final aggregate response model for the dashboard or for the most expensive subqueries, depending on current architecture.

2. **Follow existing architecture and patterns**
   - Reuse existing CQRS-lite query handlers, tenant context, and infrastructure abstractions.
   - If a cache abstraction already exists, extend it.
   - If not, add a minimal, focused abstraction for dashboard aggregate caching rather than a generic overengineered framework.

3. **Add tenant-safe cache key design**
   - Cache keys must include:
     - story area / feature prefix, e.g. `cockpit` or `dashboard`
     - `company_id`
     - query shape/version
     - any relevant dimensions such as date range, timezone, or filter set if applicable
   - Example pattern:
     - `cockpit:dashboard:v1:company:{companyId}`
     - or include dimensions if the dashboard query is parameterized
   - Never allow cross-tenant cache reuse.

4. **Implement Redis-backed read-through caching**
   - On dashboard aggregate query:
     - attempt cache read first
     - if hit, deserialize and return
     - if miss, execute DB query, materialize result, serialize, store in Redis with TTL, return result
   - Use async APIs throughout.
   - Keep serialization deterministic and compatible with current DTOs.

5. **Set a pragmatic TTL**
   - Use a short TTL suitable for interactive dashboards, likely in the range of 30 seconds to 5 minutes depending on data volatility.
   - Prefer a conservative default such as 60–120 seconds if no stronger product signal exists.
   - Make TTL configurable via options/app settings if the project already uses options binding.

6. **Handle cache failures safely**
   - If Redis is unavailable, times out, or deserialization fails:
     - log appropriately
     - fall back to the database query
     - do not fail the request solely because cache is unavailable
   - Avoid noisy logs for expected transient cache misses.

7. **Add invalidation hooks where low-cost and obvious**
   - If there are clear write paths for approvals, alerts, tasks, workflows, or dashboard-affecting activity, invalidate the relevant company dashboard cache after successful writes.
   - Keep invalidation targeted to the affected tenant.
   - If broad invalidation is too invasive for this task, rely on TTL and document follow-up opportunities.

8. **Preserve clean boundaries**
   - Do not put Redis logic directly in controllers or Blazor pages/components.
   - Keep caching in application/infrastructure around query services/handlers.
   - Ensure domain layer remains cache-agnostic.

9. **Add observability**
   - Add structured logs or metrics-friendly logging for:
     - cache hit
     - cache miss
     - cache set
     - cache fallback on failure
   - Include tenant/company context where existing logging conventions allow it.
   - Do not log sensitive payload contents.

10. **Add tests**
    - Cover:
      - cache key tenant isolation
      - first request miss then subsequent hit behavior
      - fallback to DB when cache read/write fails
      - no cross-tenant leakage
    - Prefer existing test style in the repo.
    - If true Redis integration tests are too heavy for current test setup, test via abstractions/fakes and add at least one API-level verification if feasible.

11. **Document assumptions**
    - If invalidation is partial and TTL is the main freshness mechanism, note that explicitly in code comments or task notes.
    - Keep implementation small, production-safe, and aligned with the architecture note: “Use Redis caching for expensive aggregate queries.”

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify dashboard behavior in code path:
   - First request for a tenant should execute DB-backed aggregate query and populate cache.
   - Second equivalent request for the same tenant should use cache.
   - Equivalent request for a different tenant must not reuse cached data from the first tenant.

4. Validate fallback behavior:
   - Simulate cache unavailability or force cache abstraction failure in tests.
   - Confirm dashboard query still succeeds from PostgreSQL path.

5. Validate configuration:
   - Confirm Redis-related settings are bound correctly.
   - Confirm app still starts when cache is configured as expected.
   - If local Redis is optional in dev, ensure behavior is documented and non-breaking.

6. Validate no boundary violations:
   - Redis/caching logic should not appear in domain entities.
   - Controllers/UI should call application services/queries, not cache directly.

# Risks and follow-ups

- **Risk: stale dashboard data**
  - Short TTL reduces risk, but some widgets may briefly lag behind writes.
  - Follow-up: add more precise invalidation on task/approval/workflow mutations.

- **Risk: over-caching the wrong layer**
  - Caching too low in the stack may still leave expensive composition work.
  - Prefer caching the final aggregate DTO if that is the expensive path.

- **Risk: tenant leakage through bad key design**
  - Must include `company_id` and relevant dimensions in every cache key.
  - Add explicit tests for this.

- **Risk: serialization/version drift**
  - If DTO shape changes, old cache entries may deserialize incorrectly.
  - Include a cache version segment in keys, e.g. `v1`.

- **Risk: Redis dependency causing request instability**
  - Must fail open to DB path.
  - Keep timeouts and exception handling tight.

- **Follow-up opportunities**
  - Add targeted invalidation via domain events/outbox consumers
  - Add metrics for cache hit ratio and query latency reduction
  - Extend caching to daily briefing aggregate generation if it shares the same expensive query path
  - Consider per-widget caching only if the current dashboard payload is too broad or has uneven freshness requirements