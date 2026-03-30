# Goal

Implement backlog task **TASK-12.1.1** for **ST-601 Executive cockpit dashboard** by delivering the initial **web executive dashboard** in the Blazor app that shows:

- **Daily briefing**
- **Pending approvals**
- **Alerts**
- **Department KPI cards**
- **Recent activity feed**

The implementation must fit the existing **.NET modular monolith** architecture, respect **multi-tenant scoping**, and use a **CQRS-lite query path** for dashboard data. Prefer a thin UI over a dedicated application query/service layer, with room for Redis caching later but without making caching a blocker for this task.

Because no explicit acceptance criteria were provided for the task beyond the story text, treat the story acceptance criteria as the source of truth and implement a pragmatic v1 that is production-shaped, tenant-safe, and extensible.

# Scope

In scope:

- Add a tenant-scoped dashboard page in the **Blazor Web** app as the primary executive cockpit landing experience.
- Add an application-layer query/DTO contract to fetch dashboard data in one response model.
- Implement backend query logic that aggregates:
  - latest daily briefing content
  - pending approvals summary/list
  - alerts summary/list
  - department KPI cards
  - recent activity feed
- Support drill-in navigation from widgets to relevant pages/routes where those routes already exist or can be stubbed safely.
- Provide meaningful empty states when:
  - no briefing exists
  - no approvals exist
  - no alerts exist
  - no KPI data exists
  - no recent activity exists
  - workspace is not yet configured enough to show meaningful data
- Ensure all data access is **company/tenant scoped**.
- Keep performance reasonable for interactive use by:
  - limiting list sizes
  - projecting only required fields
  - avoiding N+1 queries
  - using efficient read models

Out of scope unless already trivial in the codebase:

- Full KPI analytics engine or complex trend computation
- Mobile implementation
- Real-time push updates
- Redis caching if no cache abstraction already exists
- New notification generation pipelines
- New approval workflow behavior
- New audit subsystem behavior beyond reading existing data
- Pixel-perfect design system work beyond consistent existing app patterns

If some underlying data sources do not yet exist in the codebase, implement safe fallback behavior and shape the dashboard around currently available entities rather than blocking the task.

# Files to touch

Touch only the files needed to implement the feature cleanly. Likely areas:

- `src/VirtualCompany.Web/**`
  - dashboard page/component
  - shared dashboard widget components if appropriate
  - navigation/menu updates
  - page models/view models
- `src/VirtualCompany.Application/**`
  - dashboard query
  - dashboard DTOs/read models
  - query handler/service interface
- `src/VirtualCompany.Infrastructure/**`
  - query implementation using EF Core or existing data access patterns
  - repository/read service wiring
- `src/VirtualCompany.Api/**`
  - only if the web app consumes API endpoints rather than direct application services
  - endpoint/controller/minimal API for dashboard query if needed
- `src/VirtualCompany.Domain/**`
  - only if a small domain enum/value object is truly required; avoid unnecessary domain churn

Also inspect before coding:

- `README.md`
- solution and project references
- existing tenant resolution/auth patterns
- existing dashboard/home page routes
- existing approvals/tasks/audit/messages/notifications models
- existing UI component conventions in the Blazor app

Do not introduce broad architectural changes. Prefer extending existing patterns already present in the repository.

# Implementation plan

1. **Inspect current solution structure and existing patterns**
   - Identify how the web app currently gets authenticated user and company context.
   - Find existing modules/entities for:
     - approvals
     - tasks/workflows
     - messages/briefings
     - notifications/alerts
     - audit events/activity
     - agents/departments/KPIs
   - Determine whether the Blazor app calls the Application layer directly or via API endpoints.
   - Reuse existing route/layout/navigation conventions.

2. **Define a dashboard read model in the Application layer**
   - Create a single query contract such as `GetExecutiveDashboardQuery`.
   - Create DTOs/read models for:
     - `ExecutiveDashboardDto`
     - `DailyBriefingDto`
     - `PendingApprovalItemDto`
     - `AlertItemDto`
     - `DepartmentKpiCardDto`
     - `RecentActivityItemDto`
   - Include only fields needed by the UI, for example:
     - ids
     - titles/summaries
     - status/severity
     - timestamps
     - counts
     - department names
     - trend indicator if derivable
     - target links/route parameters if your app pattern supports it

3. **Implement tenant-scoped query logic**
   - Build a single application service/query handler that resolves the current company context and returns the dashboard DTO.
   - Aggregate data from existing sources with pragmatic mapping:
     - **Daily briefing**: latest generated summary/message/notification for the company, preferably from communication/messages if briefings are stored there
     - **Pending approvals**: approvals with pending status, ordered by oldest/newest as appropriate, limited to a small number plus total count
     - **Alerts**: use notifications/escalations/workflow failures/high-priority exceptions if an alerts table does not exist
     - **Department KPI cards**: derive simple cards from available data, e.g. task counts by department/agent role, approval backlog by department, workflow completion counts, or existing KPI records if present
     - **Recent activity feed**: use audit events, recent tasks, workflow events, or messages, whichever is already implemented and most appropriate
   - Keep all queries filtered by `company_id`.
   - Use projections and `AsNoTracking()` for read performance if EF Core is used.
   - Limit list sizes, e.g. top 5–10 items per section.

4. **Design KPI card derivation pragmatically**
   - Since explicit KPI persistence may not yet exist, implement a v1 card strategy based on available data.
   - Prefer department cards that show:
     - department name
     - primary metric value
     - optional secondary label
     - optional trend text/icon if easy to compute
   - Example fallback derivations:
     - open tasks by department
     - completed tasks in last 7 days by department
     - pending approvals touching a department
     - active agents by department
   - Document in code comments that this is a v1 derived KPI approach pending richer analytics.

5. **Build the Blazor dashboard page**
   - Add or update the main dashboard route, likely the authenticated landing page.
   - Render sections for:
     - daily briefing
     - pending approvals
     - alerts
     - department KPI cards
     - recent activity
   - Use existing UI primitives/styles.
   - Keep the page responsive and readable.
   - Add loading, error, and empty states.
   - Add drill-in links/buttons:
     - approvals -> approvals page/inbox
     - alerts -> notifications/inbox if available
     - activity items -> linked task/workflow/approval/agent pages where possible
     - KPI cards -> filtered task/agent pages if available

6. **Add empty-state guidance**
   - If the workspace has no meaningful data, show setup-oriented guidance such as:
     - hire agents
     - create workflows
     - upload knowledge
     - review approvals
   - Keep copy concise and actionable.
   - Do not hardcode links to routes that do not exist.

7. **Wire navigation**
   - Ensure the dashboard is discoverable from the main navigation.
   - If a dashboard/home page already exists, replace or extend it rather than creating duplicate entry points.

8. **Keep implementation safe and incremental**
   - Avoid inventing new persistence tables unless absolutely necessary.
   - If a source is missing:
     - return an empty section
     - log/debug as appropriate
     - do not fail the whole dashboard
   - Make the dashboard query resilient so one missing optional source does not break the page.

9. **Add tests where the repo pattern supports them**
   - Application/query tests for:
     - tenant scoping
     - empty-state response
     - pending approvals filtering
     - activity ordering
     - KPI card derivation
   - If UI tests are not present, at least add component/page-level coverage if there is an existing pattern; otherwise keep to application tests.

10. **Document assumptions in code**
   - Add concise comments where data is derived rather than sourced from a dedicated analytics model.
   - Make it easy for future tasks to replace derived KPI logic with richer analytics/caching.

# Validation steps

1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`

2. **Manual dashboard verification**
   - Launch the web app and sign in with a user that has a valid company membership.
   - Navigate to the dashboard.
   - Verify the page renders without errors.

3. **Tenant isolation verification**
   - Confirm dashboard data only shows records for the active company.
   - If you have seed data for multiple companies, verify no cross-tenant leakage in:
     - approvals
     - alerts
     - activity
     - KPI cards
     - briefing

4. **Section-by-section verification**
   - **Daily briefing**
     - shows latest available briefing/summary if present
     - shows empty state if absent
   - **Pending approvals**
     - shows pending items only
     - count matches visible/total semantics
     - links navigate correctly
   - **Alerts**
     - shows alert-like items from the chosen source
     - severity/status is understandable
   - **Department KPI cards**
     - cards render consistently
     - values are derived correctly from available data
   - **Recent activity**
     - items are ordered by recency
     - labels and timestamps are readable
     - links work where available

5. **Empty-state verification**
   - Test with a newly created or sparse company.
   - Confirm the dashboard does not crash and instead shows setup guidance.

6. **Performance sanity check**
   - Inspect query behavior for obvious N+1 issues.
   - Ensure list sections are capped and page load remains interactive.

7. **Regression check**
   - Verify navigation, authentication, and any existing home/dashboard route behavior still work.
   - Verify no unrelated pages broke due to shared layout or menu changes.

# Risks and follow-ups

- **Data source ambiguity**: “daily briefing,” “alerts,” and “department KPI cards” may not yet have dedicated persisted models. Use the best existing sources and document the mapping clearly.
- **KPI fidelity risk**: v1 KPI cards may be derived from operational data rather than a true analytics pipeline. This is acceptable for this task but should be refined later.
- **Caching not yet implemented**: if aggregate queries become expensive, a follow-up should add Redis-backed caching for dashboard read models.
- **Drill-in route gaps**: some target pages may not exist yet. Link only to existing routes or add safe placeholders if the app already uses that pattern.
- **Alert model mismatch**: if notifications/alerts are not yet modeled separately, use workflow failures/escalations/approval urgency as a temporary alert feed.
- **Authorization nuance**: if role-based visibility differs by widget, ensure the dashboard does not expose restricted data to lower-privilege users within the same tenant.
- **Future follow-up candidates**:
  - dedicated analytics projections/materialized views
  - richer trend indicators
  - configurable dashboard widgets
  - Redis caching
  - mobile dashboard companion alignment
  - dashboard-specific API endpoint if the current web architecture later requires separation