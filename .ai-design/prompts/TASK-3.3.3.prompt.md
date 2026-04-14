# Goal
Implement backlog task **TASK-3.3.3 — Add KPI tiles and anomaly list components with interactive filters** for story **US-3.3 KPI and anomaly surfacing across departments** in the existing **.NET / Blazor Web App + ASP.NET Core modular monolith**.

Deliver a tenant-scoped dashboard enhancement that:
- renders configured **department KPI tiles**
- renders an **anomaly list**
- supports **interactive filtering by time range and department without full page reload**
- computes anomalies from configured thresholds and baseline deviation percentages
- includes automated tests for:
  - anomaly detection logic
  - severity mapping
  - filter behavior

Follow existing architecture and coding conventions in the repository. Prefer incremental, production-ready changes over speculative abstractions.

# Scope
In scope:
- Add or extend backend query/application logic to return dashboard KPI and anomaly data by:
  - `company_id`
  - department
  - time range
- Implement anomaly detection rules so anomalies are flagged when:
  - metric value breaches configured thresholds, or
  - metric deviates from baseline by configured percentage
- Include anomaly fields:
  - severity
  - impacted department
  - timestamp
  - link/reference to underlying metric or workflow context
- Add Blazor dashboard UI components for:
  - KPI tiles
  - anomaly list
  - interactive filters
- Ensure filter interactions update data dynamically without full page reload
- Add automated tests at appropriate layers

Out of scope unless required by existing code structure:
- New mobile UI
- Large-scale redesign of dashboard layout
- New external integrations
- Full analytics platform refactor
- Introducing microservices or unnecessary persistence redesign

Assumptions to validate in code before implementing:
- There is already some dashboard/cockpit page for ST-601
- There may already be KPI-related DTOs, queries, or seed/config models
- There may already be metric/workflow entities that can be reused as anomaly context sources
- If no persisted anomaly model exists, compute anomalies in application/query layer for now rather than introducing a heavy new persistence model unless clearly needed

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Web/**`
  - dashboard page/component
  - new KPI tile component
  - new anomaly list component
  - filter UI/state handling
- `src/VirtualCompany.Application/**`
  - dashboard/cockpit query handlers
  - DTO/view model definitions
  - anomaly detection service or helper
- `src/VirtualCompany.Domain/**`
  - value objects/enums for anomaly severity/trend direction if missing
  - domain-level rules only if they belong there and are reusable
- `src/VirtualCompany.Infrastructure/**`
  - query/repository implementations
  - EF/data access projections
  - caching adjustments if dashboard queries are cached
- `src/VirtualCompany.Api/**`
  - only if dashboard data is served via API endpoints rather than direct server-side app services
- `tests/VirtualCompany.Api.Tests/**`
  - API/integration tests for filter behavior if API-backed
- Other test projects if present in solution and more appropriate:
  - application/unit tests
  - web/component tests if already established

Also inspect:
- `README.md`
- solution/project structure under `src/` and `tests/`
- any existing dashboard, analytics, cockpit, KPI, anomaly, scorecard, or briefing-related files

# Implementation plan
1. **Discover existing implementation surface**
   - Search the solution for:
     - dashboard/cockpit pages
     - KPI, scorecard, anomaly, alert, briefing, analytics modules
     - department filters and time-range filters
     - existing query handlers/DTOs for ST-601
   - Identify whether the dashboard is:
     - Blazor SSR with interactive islands, or
     - fully interactive Blazor components
   - Reuse existing patterns for CQRS-lite queries, tenant scoping, and authorization.

2. **Define/extend dashboard contracts**
   - Add or extend a dashboard query request model with:
     - `CompanyId`
     - `Department` or `DepartmentId`/string filter
     - `TimeRange`
   - Add response DTOs for:
     - KPI tile item:
       - metric name
       - department
       - current value
       - baseline value/label
       - trend direction
       - optional link/context
     - anomaly item:
       - metric/workflow title
       - severity
       - impacted department
       - timestamp
       - reason
       - context link
   - Keep DTOs UI-friendly and tenant-scoped.

3. **Implement anomaly detection logic**
   - Create a focused application/domain service for anomaly evaluation if one does not already exist.
   - Rules must support:
     - threshold breach detection
     - baseline deviation percentage detection
   - Add severity mapping logic, for example:
     - low/medium/high/critical based on degree of breach/deviation or configured severity bands
   - Keep the logic deterministic and unit-testable.
   - Avoid embedding business rules directly in Razor components.

4. **Source KPI and anomaly data**
   - Extend existing analytics/cockpit query handler to:
     - load configured KPI definitions per department
     - resolve current metric values for selected time range
     - resolve baseline/comparison values
     - compute trend direction
     - compute anomalies from the same metric set and/or workflow exception context
   - Ensure all queries are filtered by tenant/company.
   - If workflow context links are available, include route-friendly identifiers.
   - If metric links exist, include those instead.
   - Prefer projection queries over loading large aggregates into memory.

5. **Implement interactive filters in Blazor**
   - Add filter controls for:
     - time range
     - department
   - Update KPI tiles and anomaly list dynamically without full page reload.
   - Use the project’s existing preferred interaction pattern:
     - Blazor component state updates
     - enhanced navigation
     - partial rendering
     - API fetch + state refresh
   - Preserve usability:
     - loading state
     - empty state
     - no-results state
   - Keep filter state explicit and easy to test.

6. **Build reusable UI components**
   - Create a KPI tile component that displays:
     - KPI name
     - current value
     - baseline/comparison
     - trend direction indicator
     - department if needed
   - Create an anomaly list component that displays:
     - anomaly title/summary
     - severity badge
     - department
     - timestamp
     - link to metric/workflow context
   - Keep components presentational where possible and pass data via parameters.

7. **Wire routing/context links**
   - Ensure each anomaly item links to the underlying metric or workflow context.
   - Reuse existing routes if available.
   - If no dedicated detail page exists, link to the nearest valid dashboard/task/workflow detail route rather than inventing a large new feature.

8. **Add tests**
   - Unit tests for anomaly detection:
     - threshold breach
     - baseline deviation
     - no anomaly when within bounds
   - Unit tests for severity mapping:
     - verify expected severity for representative inputs
   - Integration/component/API tests for filter behavior:
     - department filter changes result set
     - time range filter changes result set
     - updates occur without requiring full page navigation where testable in current stack
   - Keep tests aligned with existing test infrastructure in the repo.

9. **Polish and align with architecture**
   - Respect modular monolith boundaries:
     - UI in Web
     - query/application logic in Application
     - persistence in Infrastructure
   - Keep tenant isolation enforced in all queries.
   - If dashboard query caching exists, ensure filter keys include department/time range.
   - Avoid leaking raw internal entities to UI.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run automated tests:
   - `dotnet test`

3. Manually validate in the web app:
   - Open dashboard/cockpit page
   - Confirm KPI tiles render per department
   - Confirm each tile shows:
     - current value
     - baseline/comparison
     - trend direction
   - Confirm anomalies appear when:
     - thresholds are breached
     - baseline deviation exceeds configured percentage
   - Confirm each anomaly shows:
     - severity
     - impacted department
     - timestamp
     - working link to metric/workflow context
   - Confirm changing department filter updates KPI/anomaly data without full page reload
   - Confirm changing time range filter updates KPI/anomaly data without full page reload
   - Confirm empty states are sensible when no data matches filters

4. Validate tenant safety:
   - Review query paths to ensure `company_id` scoping is applied consistently
   - Confirm no cross-tenant data can appear in dashboard results

5. Validate code quality:
   - No business logic hidden in Razor markup
   - No duplicated anomaly rule logic across layers
   - DTOs and enums are named clearly and consistently

# Risks and follow-ups
- **Unclear existing metric model**: The repository may not yet have a mature KPI storage/query model. If so, implement the smallest viable query/service layer and document any temporary assumptions.
- **Severity mapping ambiguity**: Acceptance criteria require severity mapping tests but do not define exact thresholds. Infer from existing config/models if present; otherwise implement a clear, documented mapping and note it in code comments/tests.
- **Dashboard architecture variance**: If the dashboard is SSR-first, interactive filtering may require a lightweight Blazor interactive component rather than a full page rewrite.
- **Missing context routes**: If no metric/workflow detail route exists, link to the closest valid context page and note a follow-up for richer drill-down.
- **Caching bugs**: If Redis/dashboard caching is already in place, filtered results may be incorrectly reused unless cache keys include tenant, department, and time range.
- **Test placement uncertainty**: Use the existing test project conventions in the repo; if only API tests exist, place logic tests in the nearest appropriate test project or add a focused test project only if necessary.

Potential follow-ups after completion:
- Persist anomaly history for trend analysis and auditability
- Add richer drill-down pages for KPI and anomaly context
- Add configurable severity bands in admin settings
- Add dashboard query caching/invalidations if performance becomes an issue