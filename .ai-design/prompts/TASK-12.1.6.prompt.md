# Goal

Implement backlog task **TASK-12.1.6 — Start with summary KPIs and simple trend indicators** for **ST-601 Executive cockpit dashboard**.

Deliver a first usable version of the executive cockpit KPI area in the web app that shows tenant-scoped summary metrics and lightweight trend indicators, aligned with the existing architecture:

- **Blazor Web App** frontend
- **ASP.NET Core / Application layer** query handling
- **PostgreSQL-backed aggregates**
- Optional **Redis caching hook** if the project already has a dashboard/query cache pattern

The implementation should favor a pragmatic v1 dashboard slice:
- summary KPI cards
- simple trend direction indicators
- safe empty states
- tenant-scoped, performant query path
- no forecasting, anomaly detection, or advanced charting yet

Because no explicit acceptance criteria were provided for this task, infer them from **ST-601 notes** and the architecture/backlog context. Keep the implementation small, coherent, and production-lean.

# Scope

Implement only the minimum needed to support **summary KPIs and simple trend indicators** on the executive cockpit dashboard.

Include:

1. **Dashboard KPI query contract and handler**
   - Add an application-layer query that returns executive summary KPI data for the current company/tenant.
   - The result should include:
     - KPI label/key
     - current value
     - previous comparison value
     - trend direction (`up`, `down`, `flat`, `unknown`)
     - optional delta text/percentage for display
     - optional status/semantic hint for UI styling

2. **Initial KPI set**
   Use only data that is likely already modeled in the current domain/backlog and can be derived safely from existing tables/entities. Prefer:
   - pending approvals count
   - open tasks count
   - completed tasks in recent period
   - active agents count
   - optionally workflow exceptions / blocked tasks count if already easy to derive

   Do **not** invent complex business KPIs requiring new ingestion pipelines or integrations.

3. **Simple trend calculation**
   - Compare a current period to a previous equivalent period.
   - Keep period logic simple and deterministic, such as:
     - last 7 days vs previous 7 days, or
     - today vs yesterday for operational counts
   - Use a shared helper/value object so trend logic is not duplicated in UI.

4. **Tenant-scoped data access**
   - Ensure all dashboard KPI queries are filtered by `company_id` / tenant context.
   - Reuse existing tenant resolution patterns in the solution.

5. **Executive dashboard UI**
   - Add or update the dashboard page/component in the Blazor web app to render KPI cards.
   - Each card should show:
     - title
     - current value
     - simple trend indicator
     - concise comparison text
   - Keep styling simple and consistent with the existing app.

6. **Empty/loading/error states**
   - If there is no meaningful data yet, show setup-friendly empty messaging rather than broken/blank cards.
   - Handle null/zero previous-period values safely.
   - Avoid divide-by-zero and misleading percentages.

7. **Tests**
   - Add focused tests for trend calculation and KPI query behavior.
   - Add UI/component tests only if the repo already uses a pattern for them; otherwise keep tests at application/domain level.

Out of scope:
- advanced charts
- drill-down pages
- forecasting/anomaly detection
- mobile implementation
- new notification flows
- broad dashboard redesign
- introducing a new analytics warehouse
- large schema redesign unless absolutely required

# Files to touch

Touch only the files needed for this task, but expect changes in these areas:

- `src/VirtualCompany.Application/**`
  - add dashboard/cockpit query DTOs, query, handler, and trend helper logic
- `src/VirtualCompany.Infrastructure/**`
  - implement data access for KPI aggregation if application queries rely on infrastructure repositories/read services
- `src/VirtualCompany.Api/**`
  - expose or wire the dashboard KPI endpoint if the web app consumes API endpoints rather than direct server-side application services
- `src/VirtualCompany.Web/**`
  - update executive cockpit/dashboard page and KPI card rendering
- `src/VirtualCompany.Domain/**`
  - only if a small value object/enum for trend direction belongs here; avoid unnecessary domain expansion
- `tests/VirtualCompany.Api.Tests/**`
  - add integration/API tests if this project is where query endpoint coverage lives
- other `tests/**` projects if there is a more appropriate application-layer test project already in the solution

Before coding, inspect:
- existing dashboard/cockpit pages
- existing CQRS/query patterns
- tenant context abstractions
- existing task/approval/agent/workflow read models
- any existing caching abstractions
- current test conventions

# Implementation plan

1. **Inspect current solution patterns**
   - Find how the web dashboard is currently implemented.
   - Identify whether the web app calls the API or uses server-side MediatR/application services directly.
   - Find existing entities/tables for:
     - tasks
     - approvals
     - agents
     - workflows or exceptions
   - Find tenant-scoping conventions and authorization patterns.
   - Find any existing dashboard DTOs or analytics query services.

2. **Define a minimal KPI response model**
   Create a response shape suitable for the UI, for example:
   - dashboard summary response containing a list of KPI items
   - each KPI item includes:
     - `Key`
     - `Label`
     - `CurrentValue`
     - `PreviousValue`
     - `TrendDirection`
     - `DeltaValue` and/or `DeltaPercentage`
     - `ComparisonLabel`
     - `IsEmpty`

   Keep naming aligned with existing project conventions.

3. **Implement simple trend logic**
   Add a reusable helper/service/value object for trend calculation:
   - if current > previous => `up`
   - if current < previous => `down`
   - if equal => `flat`
   - if previous unavailable => `unknown`
   - percentage delta should be null when previous is zero unless the team already has a preferred convention

   Also add a small formatter for comparison text, e.g.:
   - `"vs previous 7 days"`
   - `"+3 from previous period"`
   - `"No prior period data"`

4. **Implement application query**
   Add a query such as `GetExecutiveDashboardSummaryQuery` or extend an existing dashboard query if one already exists.

   The handler should:
   - resolve current tenant/company
   - aggregate the selected KPIs
   - compute previous-period values
   - map to the summary DTO
   - return a stable result even when there is no data

   Prefer a read-model/query-service approach over loading large domain aggregates.

5. **Implement infrastructure read access**
   Add efficient aggregate queries against PostgreSQL-backed entities.
   Favor grouped/count queries over materializing rows.

   Suggested KPI derivations:
   - **Pending approvals**
     - current: approvals with pending status now or created in current operational window, depending on existing dashboard semantics
     - previous: prior equivalent period count if period-based
   - **Open tasks**
     - current: tasks in `new`, `in_progress`, `awaiting_approval`, maybe `blocked`
     - previous: equivalent prior period snapshot is harder; if snapshot logic is too expensive, use a period-based KPI instead such as tasks created/opened in period, or choose a simpler KPI with clean comparison
   - **Completed tasks**
     - current: tasks completed in last 7 days
     - previous: tasks completed in previous 7 days
   - **Active agents**
     - current: agents with active status
     - previous: previous count may be omitted or marked unknown if historical comparison is not available without audit/history tables

   Important: choose KPIs whose trend can be computed honestly from available data. It is acceptable for some cards to show `unknown` trend if no reliable previous-period comparison exists.

6. **Wire endpoint or page data loading**
   Depending on architecture:
   - add/update API endpoint for dashboard summary, or
   - inject application query into the Blazor page model/component

   Ensure:
   - authenticated access
   - tenant scoping
   - no cross-tenant leakage
   - graceful failure behavior

7. **Build KPI card UI**
   Update the executive cockpit dashboard page to render the KPI cards near the top of the page.

   UI requirements:
   - compact summary cards
   - simple trend indicator using icon/text/color
   - accessible labels, not color-only meaning
   - empty state text when data is absent
   - no heavy chart library

   Example card content:
   - `Pending approvals`
   - `12`
   - `↑ +4 vs previous 7 days`

8. **Handle empty states carefully**
   - If the company has no agents/tasks/approvals yet, show cards with zero values and helpful text.
   - If previous-period data is unavailable, show neutral trend text like:
     - `No prior data`
     - `Trend unavailable`
   - Avoid fake percentages when previous is zero.

9. **Add tests**
   Add tests for:
   - trend direction calculation
   - zero/previous-null handling
   - tenant scoping in KPI query
   - expected KPI counts from seeded test data
   - endpoint response shape if API endpoint is added

10. **Keep implementation incremental**
   - Do not over-generalize into a full analytics subsystem.
   - Leave clear extension points for future department KPI cards, alerts, and drill-downs.

# Validation steps

1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **Manual dashboard verification**
   - Start the app and navigate to the executive cockpit/dashboard.
   - Verify KPI cards render for a tenant with seeded/sample data.
   - Verify cards render sensible zero/empty states for a new tenant.

3. **Tenant isolation verification**
   - Use or simulate two companies/tenants.
   - Confirm dashboard KPI values only reflect the active company context.

4. **Trend verification**
   - Seed or create data across two periods.
   - Confirm:
     - higher current period => up
     - lower current period => down
     - equal => flat
     - no previous data => unknown/neutral

5. **Performance sanity check**
   - Ensure the dashboard summary query does not issue obviously wasteful N+1 queries.
   - Prefer a small number of aggregate queries.
   - If caching already exists, verify cache keys are tenant-scoped.

6. **UI sanity check**
   - Confirm trend indicators are readable and not dependent on color alone.
   - Confirm card layout works in common desktop widths.
   - Confirm no unhandled exceptions when values are null/zero.

# Risks and follow-ups

- **Risk: unreliable historical comparisons**
  - Some KPIs, such as “active agents” or “currently open tasks,” may not have a trustworthy previous-period comparison without snapshots or audit history.
  - Mitigation: allow `unknown` trend for those cards, or choose period-based KPIs first.

- **Risk: dashboard semantics ambiguity**
  - “Summary KPIs” is broad and no explicit acceptance criteria were provided.
  - Mitigation: keep the KPI set small, operational, and clearly derived from existing entities.

- **Risk: performance drift**
  - Dashboard aggregates can become expensive as data grows.
  - Mitigation: use aggregate SQL queries and add tenant-scoped caching only if a project pattern already exists.

- **Risk: inconsistent tenant enforcement**
  - Dashboard queries are especially sensitive to cross-tenant leakage.
  - Mitigation: reuse established tenant filters and add tests specifically for company scoping.

Follow-ups after this task:
- add department-level KPI cards
- add drill-down links from KPI cards to tasks/approvals/agents
- introduce richer trend visualizations or sparklines
- add Redis-backed caching for expensive dashboard aggregates if not already present
- expand dashboard to include alerts, recent activity, and daily briefing sections per ST-601