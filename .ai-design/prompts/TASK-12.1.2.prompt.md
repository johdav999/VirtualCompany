# Goal

Implement backlog task **TASK-12.1.2** for **ST-601 Executive cockpit dashboard** so that **users can drill into agents, tasks, workflows, and approvals from dashboard widgets** in the Blazor web app.

This task should add the missing navigation and interaction flow from dashboard summary widgets/cards/feed items into the appropriate detail pages, while preserving tenant scoping, role-aware visibility, and existing architecture boundaries.

Because no explicit acceptance criteria were provided for the task itself, derive implementation behavior from the parent story acceptance criteria and architecture:
- Dashboard users must be able to navigate from relevant widgets to:
  - agent detail/roster views
  - task detail/list views
  - workflow instance/detail/list views
  - approval detail/inbox views
- Navigation must be tenant-scoped and safe.
- Empty or unavailable targets should fail gracefully.
- Keep implementation web-first in **Blazor Web App** and avoid introducing mobile-specific work.

# Scope

In scope:
- Update executive dashboard widget UI so clickable drill-in affordances exist where appropriate.
- Wire dashboard cards, counts, alerts, recent activity items, and/or briefing references to destination routes.
- Add or refine route/query parameter handling needed for dashboard-origin navigation.
- Ensure destination pages can accept drill-in context such as entity id, filter, or status.
- Add minimal application/query support if the dashboard currently lacks the identifiers needed to navigate.
- Add tests for navigation-related behavior and any new query contracts.
- Preserve tenant isolation and authorization expectations.

Out of scope:
- Building entirely new agent/task/workflow/approval detail pages if they already exist; prefer reusing current pages.
- Redesigning the dashboard visual system beyond what is needed for drill-in.
- Mobile companion changes.
- Broad analytics redesign or new KPI calculations.
- Large refactors unrelated to dashboard drill-through.

If a destination page does not yet exist for one of the entity types, implement the smallest consistent fallback:
- route to the relevant list page with a pre-applied filter, or
- add a lightweight detail shell only if necessary to satisfy drill-in behavior.

# Files to touch

Start by inspecting these likely areas and adjust based on actual code structure:

- `src/VirtualCompany.Web/**`
  - Dashboard page/component(s) for ST-601 executive cockpit
  - Shared widget/card/feed components used by dashboard
  - Routing/navigation helpers
  - Existing pages for agents, tasks, workflows, approvals
- `src/VirtualCompany.Application/**`
  - Dashboard query models/handlers if widget DTOs need entity ids, route metadata, or filter metadata
  - Query contracts for recent activity / alerts / approvals summaries
- `src/VirtualCompany.Api/**`
  - Only if web app depends on API endpoints that must expose additional drill-in metadata
- `src/VirtualCompany.Shared/**`
  - Shared DTOs/view models if dashboard contracts are shared across layers
- `tests/VirtualCompany.Api.Tests/**`
  - Add/adjust tests if API query contracts change
- If there is a web test project or component test location elsewhere in the repo, use it for Blazor navigation/component tests

Also inspect:
- `README.md`
- `src/VirtualCompany.Web/VirtualCompany.Web.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`

# Implementation plan

1. **Discover current dashboard and destination routes**
   - Find the executive cockpit dashboard implementation in `src/VirtualCompany.Web`.
   - Identify all current widgets that represent drillable entities:
     - pending approvals
     - alerts
     - department KPI cards
     - recent activity feed
     - daily briefing references
   - Inventory existing routes/pages for:
     - agents
     - tasks
     - workflows
     - approvals
   - Document route patterns before changing code.

2. **Map widget types to drill-in destinations**
   - Define a simple routing matrix, for example:
     - agent-related widget/card/feed item → agent detail page or roster filtered by agent/status
     - task-related widget/feed item → task detail page or tasks list filtered by status/type
     - workflow-related widget/feed item → workflow instance/detail page or workflows list filtered by state
     - approval-related widget/card/feed item → approval detail page or approval inbox filtered by pending/status
   - Prefer direct entity detail navigation when the dashboard item already represents a specific entity id.
   - Use filtered list navigation when the widget is aggregate-only.

3. **Add drill-in metadata to dashboard view models if needed**
   - If current dashboard DTOs only expose display text/counts, extend them minimally to include:
     - entity id where applicable
     - entity type
     - status/filter key
     - destination hint or route-safe metadata
   - Keep contracts explicit and typed; do not embed raw URLs from lower layers unless that is already an established pattern.
   - Ensure all query handlers remain tenant-scoped.

4. **Implement Blazor navigation from widgets**
   - Update dashboard components so clickable elements use `NavLink`, `NavigationManager`, or existing navigation abstractions consistently.
   - Make cards and feed items visibly interactive only when drill-in is available.
   - Preserve accessibility:
     - semantic links/buttons
     - keyboard focusability
     - clear labels/tooltips where useful
   - Avoid making non-drillable widgets appear clickable.

5. **Support filtered list/detail landing behavior**
   - Ensure destination pages can consume route/query parameters from dashboard navigation.
   - Examples:
     - approvals page opens with `pending` filter
     - tasks page opens with `awaiting_approval` or `blocked`
     - workflows page opens with `failed` or `in_progress`
     - agents roster opens filtered by department/status
   - If detail pages exist, ensure invalid or cross-tenant ids are handled gracefully with not found/forbidden-safe UX.

6. **Handle recent activity and alert drill-through carefully**
   - For activity items, determine target entity from activity type.
   - For alerts, route to the most relevant underlying entity:
     - approval alert → approval detail/inbox
     - workflow failure alert → workflow instance/detail
     - blocked task alert → task detail/list
     - agent health alert → agent detail/roster
   - If the underlying entity no longer exists or is inaccessible, show a safe fallback state rather than breaking navigation.

7. **Keep architecture boundaries clean**
   - UI should not infer tenant access by itself; rely on existing application/API query boundaries.
   - Do not bypass application layer to construct entity existence checks.
   - Keep dashboard query enrichment in application layer, not ad hoc in Razor components.

8. **Add tests**
   - Add or update tests for:
     - dashboard query/view model includes drill-in metadata where expected
     - tenant-scoped query behavior remains intact
     - route/filter handling on destination pages or API endpoints if changed
     - component/navigation behavior if there is an existing Blazor test setup
   - At minimum, cover the routing contract and any new query logic.

9. **Polish and verify**
   - Confirm empty states still guide setup when there are no agents/workflows/knowledge items.
   - Confirm drill-in does not appear on empty widgets with no meaningful destination.
   - Ensure styling remains consistent with current dashboard patterns.

# Validation steps

1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Manual web validation in the executive dashboard:
   - Open dashboard as a valid tenant user.
   - Verify clickable drill-in behavior from:
     - pending approvals widget
     - recent activity items
     - any alert widget/items
     - any agent/task/workflow summary cards present
   - Confirm each navigation lands on the correct page or filtered list.

3. Tenant/authorization validation:
   - Verify drill-in only resolves entities for the active company.
   - Attempt navigation with an id from another tenant if practical; confirm safe not found/forbidden behavior.
   - Verify restricted users do not see links/actions they should not access.

4. Empty-state validation:
   - With no agents/tasks/workflows/approvals, confirm dashboard still renders sensible empty states.
   - Confirm non-actionable empty widgets are not misleadingly clickable.

5. Regression validation:
   - Ensure dashboard load still works and no widget data is broken by DTO changes.
   - Ensure existing routes for agents/tasks/workflows/approvals still function directly.

# Risks and follow-ups

- **Risk: destination pages may not exist or may be incomplete**
  - Mitigation: route to filtered list pages as fallback rather than creating large new detail experiences.

- **Risk: dashboard DTOs may not carry enough metadata**
  - Mitigation: add minimal typed drill-in metadata in application contracts, not raw UI-only hacks.

- **Risk: cross-tenant leakage through ids in links**
  - Mitigation: rely on existing tenant-scoped queries and safe handling on destination pages; never trust client-side ids alone.

- **Risk: ambiguous alert/activity routing**
  - Mitigation: implement a deterministic mapping from activity/alert type to target entity/page and document it in code comments.

- **Risk: clickable cards may hurt accessibility or usability**
  - Mitigation: use semantic links/buttons, visible hover/focus states, and only enable drill-in when valid.

Follow-ups after this task, if needed:
- Add richer breadcrumb/context when arriving from dashboard.
- Add deep-link support from daily briefing content to underlying entities.
- Add dashboard widget telemetry to measure drill-in usage.
- Expand automated UI/component tests if current coverage is limited.