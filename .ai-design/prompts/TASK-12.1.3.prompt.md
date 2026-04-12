# Goal
Implement backlog task **TASK-12.1.3 — Dashboard queries are tenant-scoped and performant for interactive use** for story **ST-601 Executive cockpit dashboard**.

The coding agent should update the executive cockpit/dashboard backend and any supporting web/API layers so that:

- all dashboard data access is strictly scoped to the active tenant/company
- dashboard queries are efficient enough for interactive page loads
- expensive aggregates use appropriate caching and/or query shaping
- the implementation fits the existing **.NET modular monolith + PostgreSQL + Redis** architecture
- the result is testable and observable

Because no explicit acceptance criteria were provided for this task beyond the story text, treat the following as the effective implementation target:

1. Every dashboard query must require and apply the current `company_id`.
2. No dashboard widget may return cross-tenant data, even accidentally.
3. Dashboard reads should avoid N+1 patterns and unnecessary full-table scans.
4. Expensive summary/aggregate queries should use Redis caching where appropriate.
5. Empty states should still work correctly for tenants with no data.
6. Add or update tests proving tenant isolation and basic performance-oriented behavior.

# Scope
Focus only on the work needed to make dashboard queries tenant-scoped and performant for interactive use.

In scope:
- executive dashboard query/application service(s)
- tenant-aware query filtering
- dashboard DTO/view model shaping
- repository/query-layer improvements for aggregate reads
- Redis caching for expensive dashboard aggregates
- database indexing/migration updates if needed for dashboard access paths
- API/web endpoint integration needed to consume the improved query path
- automated tests for tenant scoping and cache/query behavior
- lightweight logging/metrics hooks if already consistent with the codebase

Out of scope unless required by existing code structure:
- redesigning the full dashboard UI
- adding entirely new dashboard widgets beyond what already exists for ST-601
- broad refactors unrelated to dashboard reads
- mobile-specific work
- unrelated audit, approval, workflow, or agent feature changes

# Files to touch
Inspect the solution first and then touch the minimum necessary files. Likely areas include:

- `src/VirtualCompany.Application/**`
  - dashboard/cockpit query handlers
  - query DTOs/view models
  - tenant context abstractions if needed
- `src/VirtualCompany.Infrastructure/**`
  - EF Core/db access implementations
  - repository/query services
  - Redis cache integration
  - migrations or SQL/index configuration
- `src/VirtualCompany.Api/**`
  - dashboard endpoints/controllers if the dashboard is API-backed
- `src/VirtualCompany.Web/**`
  - dashboard page data-loading integration if the web app calls application services directly
- `src/VirtualCompany.Domain/**`
  - only if shared domain concepts/constants are needed
- `tests/VirtualCompany.Api.Tests/**`
  - integration/API tests for tenant isolation and dashboard responses
- possibly additional test projects if dashboard query handlers are tested elsewhere

Also review:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Before coding, locate:
- how tenant/company context is currently resolved
- where ST-601 dashboard code already lives
- whether Redis caching abstractions already exist
- whether EF Core migrations are active in this repo and how indexes are managed

# Implementation plan
1. **Discover the current dashboard path**
   - Find the executive cockpit/dashboard endpoint, page model, query handler, or service.
   - Trace each widget’s data source:
     - daily briefing
     - pending approvals
     - alerts
     - department KPI cards
     - recent activity feed
   - Identify whether data is loaded through:
     - direct EF Core queries
     - repositories
     - application query handlers
     - web-layer composition

2. **Enforce tenant scoping at the query boundary**
   - Ensure every dashboard query takes the active tenant/company identifier explicitly.
   - Do not rely on UI filtering or implicit assumptions.
   - Apply `company_id` filtering to every tenant-owned table involved in dashboard reads.
   - If any widget joins across entities, verify tenant filters are applied safely on all relevant roots.
   - Prefer a single application-layer dashboard query object such as `GetExecutiveDashboardQuery(companyId, ...)` if not already present.

3. **Consolidate dashboard reads into purpose-built query models**
   - Replace generic entity loading with projection-based queries tailored for dashboard needs.
   - Select only fields required by the dashboard.
   - Use `AsNoTracking()` for read-only EF Core queries where applicable.
   - Avoid loading full aggregates/entities when only counts, summaries, or top-N lists are needed.
   - Avoid per-widget repeated queries if a shared query can serve multiple cards efficiently.

4. **Eliminate obvious performance issues**
   - Remove N+1 access patterns.
   - Prefer grouped aggregate queries for counts and KPI summaries.
   - Limit recent activity and alert feeds with explicit ordering and page size/top-N caps.
   - Ensure drill-in links use IDs already available from projected results rather than extra fetches.
   - If dashboard composition currently performs many sequential awaits, consider safe parallelization for independent reads after tenant scoping is guaranteed.

5. **Add caching for expensive aggregates**
   - Use Redis for expensive but short-lived dashboard summary data, consistent with the architecture notes.
   - Cache only tenant-scoped results.
   - Cache keys must include at least:
     - company/tenant ID
     - dashboard segment name
     - any relevant filter parameters
   - Use conservative TTLs suitable for interactive dashboards, e.g. short-lived cache for KPI summaries.
   - Do not cache user-unsafe cross-tenant/global results.
   - If some widgets are highly volatile, leave them uncached and document why.

6. **Review and add database indexes if needed**
   - Based on the actual queries, add indexes for common dashboard filters/sorts, likely combinations involving:
     - `company_id`
     - status fields
     - created/updated timestamps
     - workflow/task/approval state fields
   - Keep indexes targeted and justified by query patterns.
   - Add migration(s) if the project uses active EF migrations or the repo’s established migration mechanism.
   - Document any index rationale in code comments or PR-style notes where appropriate.

7. **Preserve correct empty-state behavior**
   - Ensure tenants with no agents, workflows, approvals, or knowledge still receive valid empty dashboard payloads.
   - Return zero counts and empty collections rather than nulls where appropriate.
   - Do not let cache or aggregate logic fail on empty datasets.

8. **Add tests**
   - Add/extend tests to prove:
     - tenant A cannot see tenant B dashboard data
     - dashboard counts/feeds only include records for the active company
     - empty tenant returns safe empty dashboard results
     - cache keys/results are tenant-scoped if cache behavior is testable in the current setup
   - Prefer integration tests around the real endpoint/query path.
   - Add focused unit tests for any cache key builder or query service logic if useful.

9. **Keep implementation aligned with architecture**
   - CQRS-lite: commands separate from dashboard queries.
   - Tenant isolation enforced in application/infrastructure layers.
   - Redis used for dashboard query cache.
   - PostgreSQL remains source of truth.
   - No direct DB access from UI if the current architecture already avoids that.

10. **Document assumptions in code comments or concise notes**
   - If no explicit performance budget exists, optimize for “interactive use” pragmatically:
     - efficient projections
     - bounded result sets
     - short-lived caching
     - proper indexes
   - If exact dashboard widgets are incomplete in the repo, implement the tenant-scoped/performance foundation around the existing dashboard contract rather than inventing a large new feature.

# Validation steps
1. Restore/build/test the solution:
   - `dotnet build`
   - `dotnet test`

2. Run any targeted tests for dashboard/API layers if available.

3. Verify tenant isolation manually or via tests:
   - create/separate data for two companies
   - request dashboard for company A
   - confirm no counts, alerts, approvals, activity, or KPIs include company B data

4. Verify empty-state behavior:
   - request dashboard for a company with no relevant data
   - confirm successful response with empty collections/zero values

5. Verify caching behavior:
   - confirm expensive aggregate path uses Redis abstraction if present
   - confirm cache keys include tenant/company ID
   - confirm cached data for one tenant is not reused for another

6. Verify query shaping/performance-oriented implementation:
   - inspect for `AsNoTracking()` on read queries where appropriate
   - inspect for projection instead of full entity materialization
   - inspect for bounded `Take(...)` on feeds/lists
   - inspect for removal of obvious N+1 patterns

7. If migrations/indexes were added:
   - ensure migration builds cleanly
   - verify schema changes are included in the correct project/location
   - confirm no broken startup or test database initialization

# Risks and follow-ups
- **Risk: tenant scoping applied inconsistently**
  - Mitigation: centralize dashboard query entry points and add integration tests with two tenants.

- **Risk: caching introduces stale or unsafe data**
  - Mitigation: keep TTL short, scope keys by tenant, and avoid caching highly volatile or user-specific data unless safe.

- **Risk: over-indexing or incorrect indexes**
  - Mitigation: add only indexes justified by actual dashboard query predicates/orderings.

- **Risk: hidden dashboard composition in web layer**
  - Mitigation: trace the full request path before changing code; move logic into application/infrastructure query services if needed.

- **Risk: no existing Redis abstraction**
  - Mitigation: use the project’s current caching pattern if present; if absent, add a minimal abstraction rather than coupling dashboard code directly to low-level cache calls everywhere.

Follow-ups to note if not completed in this task:
- add telemetry/metrics for dashboard query timings and cache hit rate
- consider materialized/precomputed aggregates if dashboard load grows
- review other cockpit/drill-down queries for the same tenant-scope guarantees
- define explicit performance SLOs for dashboard interactive load times