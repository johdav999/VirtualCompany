# Goal
Implement backlog task **TASK-22.4.4 — Simplify DepartmentCard rendering to high-signal summaries and hide zero-value metrics** for story **US-22.4 Rebuild dashboard layout, deep links, and telemetry for action-first usage**.

Deliver the dashboard update so that:

- `Dashboard.razor` renders sections in this exact order:
  1. `CompanyHealthBanner`
  2. `TodayFocusPanel`
  3. `TopActionsList`
  4. `FinanceSnapshot` and `OperationsSnapshot` grid
  5. `AgentActivityPanel`
  6. `DepartmentCards`
- The dashboard uses a responsive two-column layout on desktop and collapses to one column on smaller viewports.
- All dashboard CTAs navigate to valid deep links with required query parameters for context.
- Telemetry records:
  - focus item clicks
  - first action timestamp
  - scroll depth
  - time-to-first-action for each dashboard session
- Department cards show **at most 3 key signals** and **hide zero-value metrics**.

Work within the existing .NET / Blazor architecture and preserve tenant-scoped behavior.

# Scope
In scope:

- Update dashboard composition and layout in the Blazor web app.
- Refactor department card view models and rendering logic to prioritize high-signal metrics.
- Ensure zero-value metrics are filtered out before rendering.
- Cap displayed department signals to 3.
- Verify or implement deep-link routing for dashboard CTAs.
- Add or complete dashboard telemetry capture for session/action behavior.
- Add/update tests for rendering logic, telemetry behavior where testable, and deep-link generation.

Out of scope unless required by existing code structure:

- Rebuilding unrelated dashboard data aggregation services.
- Changing backend persistence schema unless telemetry storage already depends on it.
- Broad redesign of all dashboard widgets beyond what is necessary for acceptance criteria.
- Mobile app changes.

# Files to touch
Inspect first, then modify only the minimum necessary set. Likely candidates:

- `src/VirtualCompany.Web/Pages/Dashboard.razor`
- `src/VirtualCompany.Web/Pages/Dashboard.razor.cs` or equivalent code-behind/viewmodel if present
- Dashboard component files under something like:
  - `src/VirtualCompany.Web/Components/Dashboard/`
  - `src/VirtualCompany.Web/Shared/`
- Department card component files, likely something like:
  - `DepartmentCard.razor`
  - `DepartmentCard.razor.cs`
  - related DTO/view model classes
- Dashboard styling files:
  - component-scoped `.razor.css`
  - shared site/dashboard CSS
- Telemetry-related services/interfaces in:
  - `src/VirtualCompany.Web/`
  - `src/VirtualCompany.Application/`
  - `src/VirtualCompany.Shared/`
- Navigation/deep-link helpers if present
- Relevant tests in:
  - `tests/VirtualCompany.Api.Tests/` only if API-backed route/query generation is covered there
  - any web/component/unit test project if present in solution

If no dedicated dashboard component folder exists, search for:
- `Dashboard`
- `DepartmentCard`
- `TopActionsList`
- `TodayFocusPanel`
- `AgentActivityPanel`
- telemetry service names
- `NavigationManager`
- existing analytics/telemetry abstractions

# Implementation plan
1. **Discover current dashboard structure**
   - Find the current `Dashboard.razor` and all child components.
   - Map the current render order, layout containers, and CTA navigation behavior.
   - Identify how department card metrics are currently supplied and rendered.
   - Identify existing telemetry patterns used elsewhere in the web app.

2. **Reorder dashboard sections**
   - Update `Dashboard.razor` so the rendered order exactly matches the acceptance criteria.
   - If snapshots are separate components, place `FinanceSnapshot` and `OperationsSnapshot` in the same grid section.
   - Preserve existing data loading and authorization/tenant context.

3. **Implement responsive layout**
   - Use a clear two-column desktop layout with a single-column collapse on smaller viewports.
   - Prefer existing design system/classes if available; otherwise use component-scoped CSS.
   - Ensure section order remains logical in both desktop and mobile layouts.
   - Avoid brittle CSS that depends on hard-coded viewport assumptions.

4. **Simplify DepartmentCard rendering**
   - Refactor department card input shaping so cards receive a filtered, ordered list of candidate signals.
   - Apply these rules before rendering:
     - remove metrics with zero, null, empty, or otherwise non-meaningful values
     - sort/prioritize by signal importance if such metadata exists
     - otherwise use a deterministic fallback priority already implied by current business meaning
     - take only the top 3 remaining signals
   - Ensure the UI does not render placeholders for suppressed metrics.
   - Keep empty-state behavior sensible if a department has no non-zero signals.

5. **Preserve high-signal summaries**
   - Keep department cards concise and scannable.
   - Prefer summary metrics that indicate actionability or health over verbose metric lists.
   - If the current model lacks a way to prioritize signals, add a lightweight priority field or helper method in the view model layer rather than embedding business logic directly in markup.

6. **Validate and fix dashboard CTA deep links**
   - Audit every actionable CTA on the dashboard:
     - focus items
     - top actions
     - snapshot actions
     - department card actions if any
     - agent activity actions if any
   - Ensure each route is valid and includes required query parameters for context, e.g. `/approvals?filter=pending`.
   - Centralize route construction in a helper/view model if repeated in multiple components.
   - Do not leave raw string concatenation duplicated across components if avoidable.

7. **Implement/complete dashboard telemetry**
   - Reuse existing telemetry abstractions if present; do not invent a parallel pattern.
   - Ensure each dashboard session can capture:
     - session start
     - focus item click events
     - first actionable CTA click timestamp
     - computed/measurable time-to-first-action
     - scroll depth milestones or max scroll depth
   - If telemetry is client-side in Blazor, use the least invasive approach:
     - existing JS interop if already used for scroll tracking
     - otherwise add a small focused interop module for scroll depth/session events
   - Prevent duplicate first-action emission:
     - first action should only be recorded once per session
     - subsequent CTA clicks can still be tracked as normal action events if the existing telemetry model supports it

8. **Keep telemetry measurable**
   - Ensure time-to-first-action is derived from a stable session start timestamp and first CTA click timestamp.
   - Prefer explicit event payload fields over implicit log parsing.
   - Include enough context to distinguish dashboard sessions and action source.

9. **Add/update tests**
   - Add unit tests for department signal filtering logic:
     - zero values suppressed
     - max 3 signals rendered/returned
     - deterministic ordering
     - empty/non-zero edge cases
   - Add tests for deep-link generation if route helpers/view models are introduced.
   - Add tests for telemetry service logic where feasible:
     - first action only recorded once
     - time-to-first-action computed/emitted
   - If component tests exist, add a render test for department cards to verify hidden zero metrics.

10. **Keep implementation clean**
   - Prefer moving filtering/prioritization logic out of Razor markup into a helper, presenter, or view model.
   - Avoid mixing telemetry concerns deeply into visual components when a parent/page-level coordinator can handle them.
   - Maintain naming consistency with existing dashboard components.

# Validation steps
1. **Code inspection**
   - Confirm `Dashboard.razor` section order exactly matches:
     - `CompanyHealthBanner`
     - `TodayFocusPanel`
     - `TopActionsList`
     - `FinanceSnapshot` and `OperationsSnapshot` grid
     - `AgentActivityPanel`
     - `DepartmentCards`

2. **Behavior verification**
   - Run the app and verify:
     - desktop shows two-column layout
     - smaller viewport collapses to one column
     - section ordering remains correct
     - department cards show no more than 3 signals
     - zero-value metrics are not visible

3. **Deep-link verification**
   - Click every dashboard CTA and confirm navigation succeeds.
   - Verify required query parameters are present, e.g. pending approvals routes include filter context.

4. **Telemetry verification**
   - Confirm telemetry fires for:
     - focus item click
     - first action timestamp
     - scroll depth
     - time-to-first-action
   - Verify first-action telemetry is emitted once per dashboard session.

5. **Run tests/build**
   - Execute:
     - `dotnet build`
     - `dotnet test`
   - If there are targeted test projects for web/component logic, run those specifically as well.

6. **Regression check**
   - Ensure tenant-scoped dashboard data still loads correctly.
   - Ensure no broken rendering for departments with all-zero metrics or fewer than 3 meaningful metrics.

# Risks and follow-ups
- **Risk: telemetry architecture may be incomplete or inconsistent**
  - If no existing dashboard telemetry abstraction exists, add the smallest reusable service possible and document assumptions.
- **Risk: department signal priority may be ambiguous**
  - If business priority is not encoded, implement a deterministic priority map and note it in code comments/tests.
- **Risk: Blazor render/layout changes may affect mobile ordering**
  - Validate actual DOM/render order, not just visual placement.
- **Risk: duplicated deep-link logic across components**
  - Consolidate route generation now to avoid future drift.
- **Risk: zero-value suppression edge cases**
  - Define zero-like values consistently for numeric, percentage, and count metrics.

Follow-ups to note in your final implementation summary if not fully addressed by existing architecture:

- whether telemetry is only client-observable or also persisted server-side
- any assumptions made for department signal prioritization
- any dashboard CTAs that required route normalization or query parameter fixes