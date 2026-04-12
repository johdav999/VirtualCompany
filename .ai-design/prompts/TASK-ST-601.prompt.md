# Goal
Implement **TASK-ST-601 — Executive cockpit dashboard** for the .NET multi-project solution so founders can view a tenant-scoped company-wide dashboard in the web app showing:
- daily briefing
- pending approvals
- alerts
- department KPI cards
- recent activity feed
- drill-in navigation to agents, tasks, workflows, and approvals
- useful empty states when the workspace has little or no data

The implementation should fit the existing architecture:
- **Blazor Web App** frontend
- **ASP.NET Core** backend
- **Application layer CQRS-lite**
- **tenant-scoped queries**
- **Redis-friendly caching hooks**
- **PostgreSQL-backed aggregates**
- no mobile work in this task

If the codebase already contains partial cockpit, analytics, approvals, tasks, workflows, audit, or briefing functionality, extend and reuse it rather than duplicating patterns.

# Scope
In scope:
- Add or complete a **dashboard query/use case** in the application layer for executive cockpit data.
- Add required **domain/application DTOs/view models** for dashboard sections.
- Implement **tenant-scoped data access** in infrastructure/repositories/query services.
- Expose the dashboard through the existing API/web composition pattern used by the solution.
- Build or complete the **Blazor web dashboard page/components**.
- Include:
  - daily briefing summary block
  - pending approvals widget
  - alerts widget
  - department KPI cards
  - recent activity feed
  - drill-through links/navigation targets
  - empty states for no agents / no workflows / no knowledge / no activity
- Add **basic performance-conscious aggregation**, including cache seams if the project already uses caching abstractions.
- Add tests for application/query behavior and any critical UI/API behavior already covered by project conventions.

Out of scope unless required by existing patterns:
- New mobile UI
- Full forecasting/anomaly engine
- New notification delivery system
- New audit subsystem
- New workflow engine behavior
- Pixel-perfect design beyond existing design system
- Large schema redesigns unless absolutely necessary to support the dashboard

Because the story backlog includes acceptance criteria but the task says none were explicitly provided, treat the backlog story criteria as the target behavior:
- dashboard shows daily briefing, pending approvals, alerts, department KPI cards, recent activity feed
- users can drill into agents, tasks, workflows, approvals
- queries are tenant-scoped and performant
- empty states guide setup

# Files to touch
Touch only the files needed after inspecting the solution structure. Likely areas:

- `src/VirtualCompany.Application/**`
  - dashboard/cockpit query
  - DTOs/view models
  - interfaces for query services/caching
- `src/VirtualCompany.Domain/**`
  - only if shared domain enums/value objects are needed
- `src/VirtualCompany.Infrastructure/**`
  - query service/repository implementations
  - SQL/EF projections
  - caching integration if present
- `src/VirtualCompany.Api/**`
  - endpoint/controller/minimal API wiring if the web app consumes API endpoints
- `src/VirtualCompany.Web/**`
  - executive cockpit page
  - reusable dashboard widgets/components
  - navigation links
  - empty state components/content
- `src/VirtualCompany.Shared/**`
  - shared contracts only if this solution already centralizes read models there
- `tests/VirtualCompany.Api.Tests/**`
  - API/query integration tests
- other test projects if present and aligned with current conventions

Also inspect:
- `README.md`
- solution/project startup wiring
- existing auth/tenant resolution patterns
- existing pages for agents/tasks/approvals/workflows to link into
- any existing briefing, approval, audit, analytics, or notification models

Do **not** modify archived migration docs unless absolutely necessary. If schema changes are required, follow the repository’s actual migration approach rather than inventing one.

# Implementation plan
1. **Inspect the current solution before coding**
   - Identify:
     - how tenant context is resolved
     - whether the web app calls the API or application services directly
     - whether EF Core, Dapper, or custom SQL is used for read models
     - existing entities/tables for approvals, tasks, workflows, messages, audit events, agents, and briefings
     - existing dashboard/home page routes
     - existing caching abstractions and Redis usage
   - Summarize findings in your working notes and align implementation to current patterns.

2. **Define the executive cockpit read model**
   Create a single top-level dashboard DTO/view model with sections such as:
   - `DailyBriefing`
   - `PendingApprovals`
   - `Alerts`
   - `DepartmentKpis`
   - `RecentActivity`
   - `EmptyStateFlags`
   - optional metadata like `GeneratedAtUtc`, `CompanyId`, `CacheTimestamp`

   Keep the model UI-friendly and read-optimized. Prefer explicit fields over loosely typed blobs.

3. **Implement application-layer query/use case**
   Add a query such as `GetExecutiveCockpitDashboardQuery` and handler/service that:
   - requires tenant/company context
   - returns the full dashboard read model
   - orchestrates section-level data retrieval through interfaces
   - remains free of UI concerns
   - supports cancellation tokens
   - is deterministic and testable

   If the codebase uses MediatR or a similar pattern, follow it. If not, use the existing application service/query service pattern.

4. **Implement tenant-scoped infrastructure queries**
   Build efficient read-side queries for:
   - **Daily briefing**
     - use latest generated company briefing/summary/message if such data exists
     - if no briefing exists, return null/empty state rather than fabricating data
   - **Pending approvals**
     - count + top items ordered by urgency/created date
     - include drill-in identifiers
   - **Alerts**
     - derive from existing notifications, escalations, workflow failures, blocked tasks, or approval urgency depending on available data
     - if no dedicated alerts model exists, use a pragmatic derived projection from existing entities
   - **Department KPI cards**
     - start simple and grounded in available data, e.g.:
       - task counts by department/status
       - active agents by department
       - workflow counts
       - approval backlog
       - recent completion trend if feasible
     - include simple trend indicators only if data is readily available
   - **Recent activity feed**
     - use audit events, task updates, workflow changes, approvals, or messages depending on what exists
     - normalize into a concise feed item model

   All queries must:
   - filter by `company_id`
   - avoid cross-tenant joins/leaks
   - be efficient for interactive use
   - use pagination/limits for list widgets

5. **Add caching where appropriate**
   If the solution already has caching abstractions:
   - cache the assembled dashboard or expensive aggregate sections with a short TTL
   - key by tenant/company and relevant user scope if needed
   - ensure stale data risk is acceptable for dashboard summaries

   If no caching abstraction exists:
   - add only a minimal seam/interface, not a full caching framework
   - keep implementation conservative and easy to disable

6. **Build the Blazor executive cockpit page**
   Implement or update the main dashboard page in `src/VirtualCompany.Web`:
   - render all required widgets
   - use existing layout/components/styles
   - show loading, empty, and error states
   - make cards clickable where drill-in is expected
   - keep the page accessible and SSR-friendly if that is the app pattern

   Suggested sections:
   - top summary / daily briefing
   - approvals + alerts row
   - KPI cards row/grid
   - recent activity feed
   - setup guidance empty states

7. **Implement drill-through navigation**
   Link widgets to existing routes/pages for:
   - agents
   - tasks
   - workflows
   - approvals

   If target pages do not yet exist, link to the closest existing index/list page and avoid creating large new features outside scope.

8. **Implement empty states carefully**
   Detect and guide for cases like:
   - no agents configured
   - no workflows defined/run
   - no knowledge uploaded
   - no recent activity
   - no pending approvals
   - no alerts

   Empty states should be actionable and point users toward setup flows if those routes already exist.

9. **Add tests**
   Add tests aligned with existing conventions:
   - application/query tests verifying:
     - tenant scoping
     - section population
     - empty state behavior
     - no-data behavior
   - infrastructure/API tests verifying:
     - forbidden/not found behavior across tenants if applicable
     - dashboard returns only tenant-owned data
   - UI/component tests only if the repo already uses them

10. **Keep implementation incremental and reviewable**
   Prefer a small set of cohesive commits/changes:
   - read model + query
   - infrastructure aggregation
   - web page/widgets
   - tests

11. **Document assumptions in code comments only where necessary**
   Do not over-comment. Add concise comments only for non-obvious aggregation logic or fallback alert derivation.

# Validation steps
Run and verify as much of the following as the repository supports:

1. Build:
   - `dotnet build`

2. Tests:
   - `dotnet test`

3. Manual validation in the web app:
   - sign in as a user with a valid company membership
   - open the executive cockpit/dashboard
   - verify the page loads without errors
   - verify daily briefing section renders correctly when present and shows a helpful empty state when absent
   - verify pending approvals widget shows tenant-only approvals
   - verify alerts widget shows tenant-only alerts/exceptions
   - verify KPI cards render meaningful values from available data
   - verify recent activity feed is ordered and tenant-scoped
   - verify drill-in links navigate to agents/tasks/workflows/approvals pages
   - verify empty states appear for a newly created or sparse workspace

4. Tenant isolation checks:
   - use or simulate two companies
   - confirm dashboard data for company A never includes company B records
   - confirm direct route/API access respects tenant scoping

5. Performance sanity checks:
   - inspect query count and obvious N+1 issues
   - ensure list widgets are capped
   - ensure aggregate queries are bounded and suitable for interactive use
   - verify cache usage if implemented

6. Regression checks:
   - existing pages for agents/tasks/workflows/approvals still function
   - no broken navigation/layout in the web app

# Risks and follow-ups
- **Ambiguous source for “daily briefing”**: the codebase may not yet persist generated briefings. If absent, render a clear empty state and wire to the nearest existing summary/message source rather than inventing a fake generator.
- **Alerts model may not exist yet**: derive alerts from existing approvals, blocked tasks, failed workflows, or escalations in a transparent way. Keep the derivation isolated so a future dedicated alerts subsystem can replace it.
- **KPI definitions may be underspecified**: use simple, explainable operational KPIs based on available data now; avoid speculative business metrics.
- **Performance risk**: dashboard aggregation can become query-heavy. Prefer read-optimized projections, limits, and short-lived caching seams.
- **Navigation dependencies**: some drill-in targets may be incomplete. Reuse existing routes and avoid expanding scope into full new pages.
- **Authorization nuance**: dashboard is company-wide but still must respect tenant and any role-based restrictions already present in the app.
- **Schema uncertainty**: if required data structures are missing, keep schema changes minimal and aligned with the repository’s migration strategy.

Follow-up candidates after this task:
- dedicated analytics projections/materialized views
- richer trend charts and forecasting
- explicit alerts domain model
- dashboard personalization
- cache invalidation strategy tied to outbox/events
- mobile summary parity for ST-604