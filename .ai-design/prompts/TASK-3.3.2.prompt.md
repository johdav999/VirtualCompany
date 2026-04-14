# Goal
Implement **TASK-3.3.2 — Build anomaly detection rules engine for dashboard metrics** for **US-3.3 KPI and anomaly surfacing across departments** in the existing .NET modular monolith.

Deliver a tenant-aware KPI/anomaly capability in the **Analytics & Cockpit Module** that:
- computes department KPI tiles with current value, baseline, and trend direction
- evaluates anomaly rules using configured thresholds and baseline deviation percentages
- returns anomaly records with severity, department, timestamp, and drill-through link/context
- supports filtering by department and time range without full page reload
- includes automated tests for detection logic, severity mapping, and filter behavior

Use the current architecture and codebase conventions. Prefer incremental, production-ready implementation over speculative abstraction.

# Scope
In scope:
- Add or extend domain/application/infrastructure/web/API support for:
  - KPI metric tile query models
  - anomaly rule evaluation engine
  - severity mapping
  - tenant-scoped filtering by department and time range
  - interactive dashboard filtering without full page reload
  - automated tests
- Reuse existing CQRS-lite patterns where present.
- Keep implementation inside the modular monolith.
- Keep all data tenant-scoped via `company_id`.

Out of scope unless already trivially supported:
- forecasting/predictive anomaly models
- mobile-specific UI
- broad dashboard redesign outside KPI/anomaly widgets
- introducing microservices or external rules engines
- full generic analytics platform beyond this task

Assumptions to validate from the codebase before coding:
- There is already some dashboard or analytics page/API to extend.
- There is an existing source of KPI metric data or a placeholder aggregate query path.
- There may not yet be dedicated persistence tables for KPI definitions/anomaly events; if missing, add the minimum viable schema and migration needed.

# Files to touch
Inspect first, then update only the necessary files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - add KPI/anomaly domain models, value objects, enums, or rule definitions
- `src/VirtualCompany.Application/**`
  - add commands/queries/handlers for dashboard KPI + anomaly retrieval
  - add anomaly detection service interface and implementation
  - add DTO/view models for tiles, anomalies, filters
- `src/VirtualCompany.Infrastructure/**`
  - persistence mappings/repositories/query services
  - SQL/EF configuration
  - migration support if this repo uses in-app migrations
- `src/VirtualCompany.Api/**`
  - dashboard/analytics endpoints for filtered KPI/anomaly data
- `src/VirtualCompany.Web/**`
  - dashboard page/components for KPI tiles and anomaly list
  - interactive filtering UX without full page reload
- `tests/VirtualCompany.Api.Tests/**`
  - API/filter behavior tests
- Add tests in the appropriate test project(s) if application/domain test projects already exist; if not, place focused tests in the existing test project structure.

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`
- existing dashboard, analytics, CQRS, and tenant-scoping patterns across the solution

# Implementation plan
1. **Discover existing patterns before changing anything**
   - Identify:
     - how tenant context is resolved
     - whether EF Core, Dapper, or mixed query access is used
     - how dashboard data is currently queried
     - whether there are existing KPI, analytics, alert, or notification models
     - how Blazor interactivity is implemented for partial updates
   - Follow existing naming, folder structure, and dependency direction.

2. **Model the minimum viable KPI/anomaly domain**
   - Introduce clear types for:
     - KPI tile data
     - anomaly result
     - anomaly severity
     - trend direction
     - anomaly rule configuration
   - Support at minimum:
     - absolute threshold breach rules
     - percentage deviation from baseline rules
   - Severity should be deterministic and testable, e.g. mapped from rule config or breach magnitude.
   - Include fields required by acceptance criteria:
     - current value
     - comparison baseline
     - trend direction
     - severity
     - impacted department
     - timestamp
     - link/context reference to metric or workflow

3. **Add persistence only if needed**
   - If KPI configuration and/or anomaly rule configuration do not exist, add a minimal schema.
   - Prefer PostgreSQL tables with `company_id` and auditable timestamps.
   - Suggested pragmatic additions if absent:
     - `department_kpi_configs`
     - `metric_observations` or reuse existing metric source
     - optional `metric_anomalies` if anomaly events should be persisted
   - If the dashboard can compute anomalies on read from existing metric data, do that first unless persistence is required by current architecture.
   - Add migration(s) using the repo’s established migration approach.

4. **Implement anomaly detection rules engine**
   - Create an application/domain service that:
     - accepts metric series/current observation + baseline + rule config
     - evaluates threshold breaches
     - evaluates percentage deviation from baseline
     - determines trend direction
     - emits zero or more anomaly results
   - Keep logic pure where possible for easy unit testing.
   - Handle edge cases:
     - missing baseline
     - zero baseline when calculating percentage deviation
     - null/empty metric history
     - conflicting rules
     - negative values if valid for the metric
   - Ensure deterministic timestamps/source references.

5. **Build tenant-scoped dashboard query path**
   - Add or extend a query/endpoint that returns:
     - KPI tiles per department
     - anomaly list
     - filter metadata if useful
   - Filters:
     - `department`
     - `timeRange` (or start/end)
   - Ensure no full page reload is required by returning data suitable for async UI refresh.
   - Keep query performance reasonable; use projection queries and existing caching patterns if present.

6. **Implement web UI updates in Blazor**
   - Extend the executive dashboard or relevant analytics page with:
     - KPI tiles showing current value, baseline, trend direction
     - anomaly list/table/cards with severity, department, timestamp, and link/context
     - department and time-range filters
   - Use Blazor interactivity/event-driven refresh so filtering updates data without full page reload.
   - Preserve empty/loading/error states.
   - Keep UX simple and aligned with existing dashboard styling.

7. **Add drill-through/context links**
   - Each anomaly should expose a route or context payload linking to:
     - the underlying metric detail page if one exists, or
     - the related workflow/task context if that is the available source
   - If no dedicated detail page exists, provide a stable route/query contract or placeholder link target consistent with current navigation patterns.

8. **Automated tests**
   - Add focused tests for:
     - threshold breach detection
     - baseline deviation detection
     - severity mapping
     - trend direction calculation
     - zero/missing baseline handling
     - tenant-safe filtered API/query behavior
     - department/time-range filter behavior from the web/API layer
   - Prefer unit tests for rules engine and integration/API tests for filtering behavior.

9. **Quality pass**
   - Confirm code compiles cleanly.
   - Remove dead code and speculative abstractions.
   - Ensure all new endpoints/queries enforce tenant scope.
   - Keep comments concise and only where they add value.

# Validation steps
Run and report results for the relevant commands:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If there are targeted test projects or filters for faster iteration, run those first, then full suite.

4. Manually validate in the web app if runnable:
   - open dashboard
   - verify KPI tiles render per department
   - verify each tile shows current value, baseline, trend direction
   - verify anomalies appear for threshold and baseline deviation cases
   - verify anomaly entries show severity, department, timestamp, and link/context
   - verify changing department filter updates results without full page reload
   - verify changing time range updates results without full page reload
   - verify tenant isolation still holds

5. Include in your final implementation notes:
   - files changed
   - schema/migration changes
   - assumptions made
   - any acceptance criteria not fully completed and why

# Risks and follow-ups
- **Metric source ambiguity:** The repo may not yet have a canonical KPI metric store. If missing, implement the smallest viable schema/query path and document assumptions.
- **Severity mapping ambiguity:** If backlog/docs do not define exact thresholds for severity, implement a configurable mapping and document defaults.
- **Trend calculation ambiguity:** Use a simple, deterministic comparison against baseline or prior period unless an existing convention exists.
- **UI route availability:** Underlying metric/workflow detail pages may not exist; provide the best available stable link/context contract and document follow-up needs.
- **Performance:** Dashboard aggregate queries can become expensive; use projection/caching patterns already present, but avoid premature optimization.
- **Persistence choice:** Persist anomaly events only if needed by current architecture; otherwise compute on read for v1 simplicity.
- **Follow-up candidates:** anomaly history, user-configurable rule management UI, persisted alert acknowledgements, richer trend visualizations, forecast-based anomaly detection.