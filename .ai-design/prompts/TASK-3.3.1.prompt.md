# Goal
Implement backlog task **TASK-3.3.1 — Implement KPI query layer and baseline comparison calculations** for **US-3.3 KPI and anomaly surfacing across departments** in the existing .NET modular monolith.

Deliver a tenant-aware KPI/anomaly query capability that powers dashboard KPI tiles and anomaly surfacing with:
- current KPI value
- comparison baseline
- trend direction
- anomaly detection from threshold breaches and baseline deviation percentages
- severity mapping
- filtering by time range and department without full page reload
- automated tests for anomaly detection logic, severity mapping, and filter behavior

The implementation must align with the architecture:
- CQRS-lite query layer
- ASP.NET Core backend
- PostgreSQL-backed analytics data access
- Blazor Web App dashboard consumption
- strict tenant scoping
- modular monolith boundaries

# Scope
In scope:
- Add or extend domain/application contracts for KPI dashboard queries and anomaly results
- Implement backend query layer in the Analytics & Cockpit area for department KPI tiles and anomaly lists
- Implement baseline comparison calculation logic and trend direction derivation
- Implement anomaly detection logic for:
  - configured absolute/threshold breaches
  - configured percentage deviation from baseline
- Implement severity mapping for anomalies
- Expose API endpoints or existing query endpoints needed by the web dashboard
- Update Blazor dashboard components/pages to support interactive filtering by:
  - time range
  - department
  without full page reload
- Add automated tests covering:
  - anomaly detection logic
  - severity mapping
  - filter behavior
- Keep tenant isolation enforced in all queries

Out of scope unless required by existing code patterns:
- full forecasting
- mobile app changes
- broad dashboard redesign
- introducing new infrastructure beyond current stack
- unrelated audit/inbox features
- speculative schema redesign outside what is necessary for KPI/anomaly support

If the codebase already contains partial KPI/dashboard models, extend them rather than duplicating them.

# Files to touch
Inspect the solution first and then touch the minimum necessary files in these likely areas.

Likely backend areas:
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Domain/**`
- `src/VirtualCompany.Infrastructure/**`
- `src/VirtualCompany.Api/**`

Likely web UI areas:
- `src/VirtualCompany.Web/**`

Likely test areas:
- `tests/VirtualCompany.Api.Tests/**`
- any existing application/integration/unit test projects if present

Specifically look for and reuse/extend:
- dashboard or analytics query handlers
- KPI DTOs/view models
- tenant/company context abstractions
- repository/query service patterns
- existing filter models for date range/department
- existing Blazor interactive components for dashboard widgets
- existing API endpoints for executive cockpit/dashboard

Potential file categories to add or modify:
- query request/response models
- query handlers/services
- anomaly calculation service
- severity mapping helper/value object/enum
- infrastructure SQL/EF/Dapper query implementation
- API controller/minimal endpoint
- Blazor component/page and backing models
- unit/integration tests

Do not invent file names blindly; inspect the repository structure and follow established naming and placement conventions.

# Implementation plan
1. **Inspect current analytics/dashboard implementation**
   - Find existing modules/namespaces for executive cockpit, analytics, dashboard, KPI, alerts, or anomalies.
   - Identify whether the project uses MediatR, custom CQRS handlers, EF Core, Dapper, or repository/query services.
   - Identify current tenant resolution and authorization patterns.
   - Identify whether KPI configuration already exists on `agents.kpis_json`, department scorecards, or another source.

2. **Define/extend query contracts**
   - Add or extend a query model for dashboard KPI/anomaly retrieval with filters:
     - `companyId`/tenant context from authenticated scope, not client trust
     - optional `department`
     - time range start/end or preset
   - Add response DTOs for:
     - KPI tile
       - KPI identifier/name
       - department
       - current value
       - baseline value
       - comparison delta
       - delta percentage
       - trend direction
       - timestamp/as-of
       - optional context link
     - anomaly item
       - metric/KPI identifier
       - department
       - severity
       - timestamp
       - reason/type (`threshold_breach`, `baseline_deviation`)
       - current value
       - baseline value
       - threshold/deviation config used
       - link to underlying metric or workflow context

3. **Model baseline comparison and trend direction**
   - Implement a dedicated calculation service in application/domain layer, not in controllers/UI.
   - Baseline logic should be deterministic and testable.
   - Prefer a clear baseline strategy based on available data, for example:
     - compare current aggregate for selected range to prior equivalent range, or
     - compare current point/latest value to configured baseline snapshot if such data already exists
   - Use the existing data model if KPI snapshots/metric history already exist.
   - If no explicit baseline persistence exists, implement the smallest viable query-based baseline using prior equivalent period.
   - Trend direction should be derived consistently from current vs baseline:
     - up
     - down
     - flat
   - Handle edge cases:
     - missing baseline
     - zero baseline
     - null/empty metric history
     - negative values if supported
   - Document assumptions in code comments where needed.

4. **Implement anomaly detection**
   - Build anomaly detection logic as a reusable service with pure inputs where possible.
   - Detect anomalies when either:
     - metric breaches configured threshold(s), or
     - metric deviates from baseline by configured percentage
   - Support configuration-driven evaluation. Reuse existing KPI/agent config if present.
   - If multiple anomaly conditions match, either:
     - emit the highest-severity anomaly only, or
     - emit one anomaly with aggregated reasons
     based on existing UX/API patterns. Keep behavior explicit and tested.
   - Include required anomaly fields:
     - severity
     - impacted department
     - timestamp
     - link/context reference

5. **Implement severity mapping**
   - Add explicit severity mapping rules, ideally centralized:
     - e.g. info / warning / critical, or existing enum values in the codebase
   - Severity should be derived from:
     - threshold breach magnitude and/or
     - percentage deviation magnitude and/or
     - KPI-specific configuration if already modeled
   - Avoid hardcoding magic numbers in multiple places.
   - Keep mapping configurable if the codebase already supports config-driven thresholds.

6. **Implement infrastructure query layer**
   - Add or extend the infrastructure query service/repository to fetch KPI metric data scoped by:
     - tenant/company
     - department
     - time range
   - Ensure query performance is reasonable for dashboard use.
   - Prefer projection queries over loading large aggregates into memory.
   - If using SQL/EF:
     - filter by tenant first
     - apply date range and department filters in the query
     - return only fields needed for KPI/anomaly calculations
   - If there is caching already for dashboard aggregates, integrate carefully but do not overcomplicate this task.

7. **Expose API/application endpoint**
   - Add or extend endpoint(s) used by the dashboard to fetch KPI and anomaly data asynchronously.
   - Ensure endpoint authorization and tenant scoping follow existing patterns.
   - Return data suitable for partial UI refresh, not full page reload.
   - If the app already uses server-side interactive Blazor with direct service calls, follow that pattern instead of adding unnecessary HTTP endpoints.

8. **Update Blazor dashboard filtering UX**
   - Add or extend dashboard filter controls for:
     - department
     - time range
   - Wire filters to reload KPI/anomaly data asynchronously without full page reload.
   - Preserve existing UX patterns and component structure.
   - Ensure KPI tiles display:
     - current value
     - baseline
     - trend direction
   - Ensure anomalies display:
     - severity
     - department
     - timestamp
     - link to metric/workflow context

9. **Add automated tests**
   - Unit tests for baseline comparison calculations:
     - increase/decrease/flat
     - zero or missing baseline
   - Unit tests for anomaly detection:
     - threshold breach
     - baseline deviation breach
     - no anomaly
     - multiple conditions
   - Unit tests for severity mapping:
     - boundary values
     - expected severity levels
   - API/application/integration tests for filter behavior:
     - tenant scoping
     - department filter
     - time range filter
     - no full page reload is not a backend test; verify the UI triggers async data refresh or endpoint-based filtering behavior through component/API tests as appropriate
   - Reuse existing test patterns and fixtures.

10. **Keep implementation production-safe**
   - Do not break existing dashboard behavior.
   - Keep null-safe handling for incomplete KPI configuration.
   - If configuration is missing/ambiguous, fail safely:
     - no false critical anomaly from invalid config
     - log or surface non-blocking issue according to existing patterns
   - Keep code cohesive and avoid leaking infrastructure concerns into UI.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate backend behavior:
   - Confirm KPI query returns tenant-scoped results only
   - Confirm department filter changes result set correctly
   - Confirm time range filter changes current/baseline calculations correctly
   - Confirm anomaly payload includes:
     - severity
     - department
     - timestamp
     - context link

4. Manually validate UI behavior:
   - Open dashboard
   - Change department filter and verify KPI/anomaly sections update without full page reload
   - Change time range and verify KPI values, baselines, and trends update
   - Verify anomaly rows/cards show severity and navigation link/context

5. Validate edge cases:
   - no KPI data in selected range
   - missing baseline data
   - baseline value of zero
   - department with no anomalies
   - mixed KPI configurations across departments

6. If migrations/schema changes are required, ensure they are included and documented, but only add them if truly necessary after inspecting the current model.

# Risks and follow-ups
- **Risk: KPI source data model may be incomplete or not yet normalized.**
  - Mitigation: inspect existing analytics entities first and implement the smallest viable query path using current persisted metric/task/workflow data.

- **Risk: Baseline definition may be ambiguous in current product design.**
  - Mitigation: use a deterministic prior-equivalent-period baseline unless an existing configured baseline model already exists; document the chosen rule in code/tests.

- **Risk: KPI configuration may live in flexible JSON and vary by department/agent.**
  - Mitigation: centralize parsing/validation and handle missing config safely.

- **Risk: Dashboard performance may degrade if calculations are done entirely in memory.**
  - Mitigation: push filtering/projection/aggregation into the database query layer where possible.

- **Risk: Severity rules may become duplicated across UI and backend.**
  - Mitigation: keep severity mapping backend-centralized and return final severity in DTOs.

Follow-ups after this task, if not already covered elsewhere:
- persist/cache precomputed KPI snapshots for heavier dashboard workloads
- add richer anomaly explainability and audit linkage
- add configurable baseline strategies per KPI
- add trend sparkline/history visualization
- add Redis caching for expensive aggregate queries if needed