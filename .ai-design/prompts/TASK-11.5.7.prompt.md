# Goal
Implement backlog task **TASK-11.5.7 — Use cached dashboard aggregates to reduce generation cost** for story **ST-505 Daily briefings and executive summaries**.

The coding agent should update the daily/weekly briefing generation flow so it **reuses existing cached executive/dashboard aggregate data** instead of recomputing expensive KPI/alert/approval/activity summaries during briefing generation whenever possible.

This should:
- reduce LLM/context assembly cost,
- reduce repeated aggregate query cost,
- preserve tenant isolation,
- keep briefing content consistent with the executive cockpit/dashboard,
- degrade safely when cache is missing or stale.

No explicit acceptance criteria were provided, so infer completion from the story notes and architecture:
- briefings should aggregate alerts, approvals, KPI highlights, anomalies, and notable agent updates,
- summaries should be stored as messages/notifications and visible in the dashboard,
- cached dashboard aggregates should be used to reduce generation cost.

# Scope
In scope:
- Identify the current dashboard aggregate query/service and the current daily/weekly briefing generation path.
- Introduce or extend a **tenant-scoped cached aggregate contract** that briefing generation can consume.
- Refactor briefing generation to prefer cached dashboard aggregates for reusable summary inputs.
- Add fallback behavior when cache is unavailable, expired, or incomplete.
- Ensure cache usage is safe for multi-tenant operation and does not bypass authorization/data scoping assumptions.
- Add/adjust tests around cache hit, cache miss, and stale/incomplete aggregate scenarios.
- Keep implementation aligned with the modular monolith and CQRS-lite approach.

Out of scope unless already trivial in the codebase:
- Building a brand new dashboard feature from scratch.
- Redesigning the briefing prompt/output format beyond what is needed to consume cached aggregates.
- Adding email delivery.
- Large-scale cache invalidation redesign across the whole product.
- Introducing new infrastructure beyond the existing stack hint (.NET + Redis/PostgreSQL).

# Files to touch
Start by locating the actual implementations, then update the relevant files. Likely areas:

- `src/VirtualCompany.Application/**`
  - briefing generation commands/handlers/services
  - dashboard/executive cockpit query services
  - cache contracts/interfaces
  - DTOs/view models for aggregate snapshots
- `src/VirtualCompany.Infrastructure/**`
  - Redis-backed cache implementation
  - repository/query implementations for dashboard aggregates
  - serialization/deserialization for cached aggregate payloads
- `src/VirtualCompany.Api/**`
  - only if DI registration or endpoint wiring must change
- `src/VirtualCompany.Domain/**`
  - only if a domain-level value object or contract is clearly warranted
- `src/VirtualCompany.Shared/**`
  - shared DTOs/constants only if already used for cross-layer contracts
- `src/VirtualCompany.Web/**`
  - only if dashboard/briefing UI depends on changed DTOs
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests if briefing generation is exercised there
- other test projects if present for application/infrastructure behavior

Also inspect:
- `README.md`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj`

If migrations are required for persistence changes, check the project’s current migration approach first. Do **not** add schema changes unless clearly necessary for this task.

# Implementation plan
1. **Discover the current flow**
   - Find the implementation for:
     - scheduled daily/weekly briefing generation,
     - executive/dashboard aggregate queries,
     - any Redis caching already used for dashboard widgets or KPI summaries,
     - message/notification persistence for generated briefings.
   - Trace the dependency chain from scheduler/background worker to briefing content assembly.

2. **Identify reusable aggregate shape**
   - Define the minimum aggregate snapshot needed by briefing generation, ideally reusing an existing dashboard aggregate DTO/query result.
   - The snapshot should cover, where available:
     - KPI highlights,
     - alerts/anomalies,
     - pending approvals,
     - notable agent/task/workflow updates,
     - generated-at timestamp / freshness metadata.
   - Prefer extending an existing dashboard aggregate response rather than creating a parallel model.

3. **Introduce a cache-facing application contract**
   - Add or refine an interface in the application layer for something like:
     - `IExecutiveDashboardAggregateCache`
     - or `IExecutiveDashboardSnapshotProvider`
   - The contract should support:
     - `GetAsync(companyId, ...)`
     - optional freshness metadata,
     - fallback to source query when cache is absent if that pattern already exists,
     - explicit tenant scoping in method signatures.
   - Keep the contract application-oriented, not Redis-specific.

4. **Implement/extend Redis-backed aggregate caching**
   - In infrastructure, implement the cache provider using Redis if not already present.
   - Use a deterministic tenant-scoped cache key, for example conceptually:
     - `dashboard:aggregate:{companyId}:{periodOrView}`
   - Include:
     - serialization of the aggregate snapshot,
     - TTL appropriate for dashboard/briefing reuse,
     - versioning in the key or payload if needed to avoid deserialization issues.
   - Do not leak cross-tenant data through shared keys.

5. **Refactor dashboard aggregate generation to populate cache**
   - Ensure the dashboard aggregate query path writes the reusable snapshot into cache.
   - If the dashboard already caches results, standardize the format so briefing generation can consume the same cached object.
   - Avoid duplicating aggregate-building logic in both dashboard and briefing services.

6. **Refactor briefing generation to prefer cached aggregates**
   - Update the daily/weekly briefing generation service/handler so it:
     1. requests the cached dashboard aggregate snapshot,
     2. validates freshness/completeness,
     3. uses it as the primary source for summary assembly,
     4. falls back to direct aggregate query/build only when necessary.
   - If the briefing uses an LLM, pass the cached aggregate snapshot as structured context rather than re-querying all underlying sources.
   - Preserve existing behavior for final message persistence and notification creation.

7. **Handle freshness and fallback explicitly**
   - Define practical rules in code for:
     - cache hit and fresh => use cached snapshot,
     - cache hit but stale => either refresh or fallback to source query,
     - cache hit but incomplete/invalid => ignore and rebuild,
     - cache miss => build from source and optionally backfill cache.
   - Add logs/telemetry around which path was used.
   - Keep fallback safe and deterministic.

8. **Preserve consistency and tenant isolation**
   - Verify all aggregate retrieval and cache access paths require `companyId`.
   - Ensure no background worker can accidentally generate a briefing using another tenant’s cached snapshot.
   - If user-specific delivery preferences exist, keep them separate from company-level aggregate caching.

9. **Add tests**
   - Add/adjust tests for:
     - briefing generation uses cached dashboard aggregates on cache hit,
     - briefing generation falls back on cache miss,
     - stale or malformed cache payload triggers safe fallback,
     - tenant-scoped cache keys do not collide,
     - generated briefing still persists correctly as message/notification.
   - Prefer unit tests for provider/handler behavior and integration tests where the existing test suite supports them.

10. **Keep changes minimal and idiomatic**
   - Follow existing naming, DI registration, logging, and result/error handling patterns in the repo.
   - Do not introduce speculative abstractions beyond what this task needs.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted tests for briefing/dashboard behavior, run those specifically as well.

4. Manually verify in code or via tests:
   - a dashboard aggregate request populates or refreshes the cache,
   - a scheduled or invoked briefing generation path reads the cached aggregate first,
   - on cache hit, the briefing path does not recompute all expensive aggregates,
   - on cache miss/stale payload, briefing generation still succeeds,
   - persisted briefing messages/notifications remain unchanged in outward behavior.

5. If logging exists, confirm there is enough observability to distinguish:
   - cache hit,
   - cache miss,
   - stale cache fallback,
   - aggregate rebuild.

# Risks and follow-ups
- **Risk: duplicate aggregate models**
  - Creating a separate briefing-only aggregate DTO may drift from the dashboard model.
  - Prefer one reusable snapshot contract.

- **Risk: stale briefing content**
  - Overly long TTLs may produce outdated summaries.
  - Use freshness metadata and explicit fallback rules.

- **Risk: tenant leakage**
  - Cache key mistakes in a multi-tenant SaaS are high severity.
  - Ensure company ID is mandatory in all cache operations and tests cover isolation.

- **Risk: hidden recomputation remains**
  - The briefing service may still call lower-level expensive queries indirectly.
  - Trace the full path and remove redundant aggregate fetches where practical.

- **Risk: malformed cache payloads after model changes**
  - Consider payload/key versioning if the project already uses versioned cache entries.

Follow-ups after this task, if not already covered:
- add metrics for briefing generation cost and cache hit rate,
- consider prewarming dashboard aggregates before scheduled briefing windows,
- align weekly summary aggregation windows with dashboard snapshot semantics,
- document the cache contract and TTL rationale in code comments or project docs.