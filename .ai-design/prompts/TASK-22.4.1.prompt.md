# Goal
Implement backlog task **TASK-22.4.1 — Refactor Dashboard.razor layout hierarchy and responsive grid styling for the new section order** for story **US-22.4 Rebuild dashboard layout, deep links, and telemetry for action-first usage**.

Deliver a production-ready update to the Blazor web dashboard so that:

- `Dashboard.razor` renders sections in this exact order:
  1. `CompanyHealthBanner`
  2. `TodayFocusPanel`
  3. `TopActionsList`
  4. `FinanceSnapshot` and `OperationsSnapshot` in a shared responsive grid
  5. `AgentActivityPanel`
  6. `DepartmentCards`
- The dashboard uses a responsive two-column desktop layout and collapses to a single column on smaller viewports.
- All dashboard action CTAs navigate to valid deep links with required query parameters for context.
- Telemetry captures:
  - focus item clicks
  - first action timestamp
  - scroll depth per dashboard session
  - measurable time-to-first-action when any actionable CTA is clicked
- Department cards show no more than 3 key signals and suppress zero-value metrics.

Keep the implementation aligned with the existing Blazor Web App architecture and avoid introducing unnecessary framework or architectural changes.

# Scope
In scope:

- Refactor the dashboard page/component composition and markup hierarchy.
- Update or add responsive layout CSS for the new section order and desktop/mobile behavior.
- Ensure dashboard CTA links are valid, contextual deep links.
- Add or wire telemetry for dashboard session behavior and first-action measurement.
- Update department card rendering rules to cap visible signals at 3 and hide zero-value metrics.
- Add or update tests where the solution already has coverage patterns for Blazor/UI/view-model logic.

Out of scope:

- Rebuilding unrelated dashboard data queries.
- Creating new backend APIs unless strictly required for existing deep-link or telemetry contracts.
- Broad redesign of all dashboard child components beyond what is needed for ordering, layout, CTA routing, telemetry, and department-card signal filtering.
- Mobile app changes.
- Large-scale analytics platform changes beyond the dashboard telemetry needed by this task.

# Files to touch
Start by locating the actual dashboard implementation and related components in `src/VirtualCompany.Web`. Expect to touch some subset of the following, based on what exists in the repo:

- `src/VirtualCompany.Web/Components/Pages/Dashboard.razor`
- `src/VirtualCompany.Web/Components/Pages/Dashboard.razor.cs`
- `src/VirtualCompany.Web/Components/Layout/...` or dashboard section component folders
- Dashboard child components such as:
  - `CompanyHealthBanner*`
  - `TodayFocusPanel*`
  - `TopActionsList*`
  - `FinanceSnapshot*`
  - `OperationsSnapshot*`
  - `AgentActivityPanel*`
  - `DepartmentCards*`
- Dashboard-specific styling files, for example:
  - `Dashboard.razor.css`
  - shared site/dashboard stylesheet files under `src/VirtualCompany.Web/wwwroot/css/`
- Navigation/deep-link helpers or route constants if present
- Telemetry abstractions/services in Web/Application layers if already present
- Any DTO/view-model classes that shape dashboard CTA URLs or department signals
- Relevant test files under:
  - `tests/VirtualCompany.Api.Tests/` if there are integration tests covering routes/contracts
  - any existing web/component/unit test project if present in the solution

Before editing, inspect the repo and update the exact file list based on actual locations. Prefer modifying existing dashboard components over creating parallel replacements.

# Implementation plan
1. **Discover the current dashboard structure**
   - Find `Dashboard.razor` and identify:
     - current section order
     - current layout containers
     - where CTA links are generated
     - where telemetry is currently emitted, if anywhere
     - how department cards are populated/rendered
   - Identify whether child sections are standalone components or inline markup.
   - Identify whether responsive behavior is currently implemented with CSS grid, flexbox, Bootstrap utilities, or custom classes.

2. **Refactor the section hierarchy in `Dashboard.razor`**
   - Reorder the rendered sections to exactly match the acceptance criteria.
   - Preserve existing data flow and parameters into child components.
   - Keep the page readable by using semantic wrapper regions/sections where appropriate.
   - If the snapshots are currently independent blocks, place `FinanceSnapshot` and `OperationsSnapshot` inside a shared grid container dedicated to the paired layout.

3. **Implement responsive layout behavior**
   - Use a clear dashboard layout structure that supports:
     - desktop: two-column layout where appropriate
     - tablet/mobile: single-column stacking
   - Prefer CSS Grid for the main dashboard layout and the finance/operations paired section unless the codebase already standardizes on another approach.
   - Ensure the layout does not break section order in the DOM; visual layout should still respect accessibility and reading order.
   - Add or update CSS breakpoints so smaller viewports collapse to one column.
   - Verify spacing, alignment, and card widths remain consistent with existing design tokens/utilities.

4. **Validate and fix dashboard CTA deep links**
   - Audit all actionable CTAs rendered from:
     - `TodayFocusPanel`
     - `TopActionsList`
     - snapshots
     - activity panel
     - department cards
     - any banner actions
   - Ensure each CTA points to a valid route in the app.
   - Include required query parameters for context, e.g. `/approvals?filter=pending`.
   - Avoid hardcoded malformed URLs or missing context parameters.
   - If the codebase has route helpers/constants, use them instead of duplicating strings.
   - If a CTA currently has no valid destination, either map it to the correct existing route or disable/remove it only if necessary and justified by the acceptance criteria.

5. **Add dashboard session telemetry**
   - Implement or wire a dashboard session concept scoped to a page visit/render lifecycle.
   - Capture:
     - focus item clicks
     - first actionable CTA click timestamp
     - scroll depth for the session
     - time-to-first-action metric derived from session start to first CTA click
   - Reuse existing telemetry abstractions if present; do not invent a parallel telemetry pipeline.
   - If JS interop is needed for scroll depth:
     - keep it minimal
     - register/unregister listeners correctly
     - avoid memory leaks
     - ensure the Blazor component disposes subscriptions cleanly
   - Ensure first-action telemetry is emitted once per session, even if multiple CTAs are clicked later.
   - Make telemetry event names/properties consistent and structured.

6. **Update department card signal rendering**
   - Locate the rendering logic for department card metrics/signals.
   - Filter out zero-value metrics before rendering.
   - Limit displayed signals to a maximum of 3 per card.
   - Preserve stable ordering of the most important signals based on existing business priority if already defined.
   - If no non-zero signals remain, render an appropriate compact empty/neutral state rather than placeholder noise.

7. **Keep implementation cohesive**
   - Avoid pushing presentation-only logic into backend layers unless already required by existing architecture.
   - If signal filtering is purely UI concern, keep it in the web/view-model layer.
   - If CTA generation already belongs to a dashboard view-model or query response mapper, update it there for consistency.
   - Keep telemetry code isolated behind a service/helper so the page markup stays maintainable.

8. **Add or update tests**
   - Add focused tests for any extracted logic such as:
     - department signal filtering and max-3 behavior
     - CTA URL generation with required query parameters
     - first-action telemetry guard behavior if implemented in testable service logic
   - If there is an existing component test pattern, add coverage for section order and rendered links.
   - Do not add brittle tests that depend on incidental CSS class names unless that is the established convention.

9. **Document assumptions in code comments only where needed**
   - Add concise comments for non-obvious telemetry/session behavior or scroll-depth interop.
   - Do not over-comment straightforward markup or CSS.

# Validation steps
1. **Build and test**
   - Run:
     - `dotnet build`
     - `dotnet test`
   - Fix any warnings/errors introduced by the changes.

2. **Manual dashboard verification**
   - Launch the web app and navigate to the dashboard.
   - Confirm the rendered section order is exactly:
     1. CompanyHealthBanner
     2. TodayFocusPanel
     3. TopActionsList
     4. FinanceSnapshot + OperationsSnapshot grid
     5. AgentActivityPanel
     6. DepartmentCards

3. **Responsive verification**
   - Test at desktop and smaller viewport widths.
   - Confirm:
     - desktop uses a two-column layout where intended
     - smaller viewports collapse to a single column
     - no overlapping, clipping, or incorrect ordering occurs

4. **CTA verification**
   - Click every dashboard action CTA.
   - Confirm each route resolves successfully and includes expected query parameters for context.
   - Verify examples like approvals use contextual filters such as `?filter=pending` where required.

5. **Telemetry verification**
   - Confirm a dashboard session starts on page load/render.
   - Click a focus item and verify telemetry is emitted.
   - Click the first actionable CTA and verify:
     - first action timestamp is recorded
     - time-to-first-action is emitted/measurable
     - repeated CTA clicks do not duplicate the “first action” event for the same session
   - Scroll the page and verify scroll-depth telemetry updates/emits appropriately.
   - Use existing logging/dev telemetry sinks/test hooks in the repo to validate behavior.

6. **Department card verification**
   - Confirm each department card renders at most 3 signals.
   - Confirm zero-value metrics are not shown.
   - Confirm cards still render cleanly when fewer than 3 non-zero signals exist.

7. **Regression check**
   - Verify no broken navigation, no console errors, and no JS interop disposal issues when navigating away from the dashboard.

# Risks and follow-ups
- **Risk: dashboard component locations may differ from assumptions**
  - Mitigation: inspect actual file structure first and adapt the implementation to existing conventions.

- **Risk: telemetry infrastructure may be incomplete or inconsistent**
  - Mitigation: reuse existing abstractions where possible; if missing, add the smallest viable extension rather than a new telemetry subsystem.

- **Risk: scroll-depth tracking may require JS interop**
  - Mitigation: keep interop narrowly scoped, disposable, and resilient to prerender/interactive lifecycle differences in Blazor.

- **Risk: some CTA destinations may not yet exist or may use different route patterns**
  - Mitigation: map to existing valid routes and include required query parameters; note any blocked CTA gaps clearly in the final implementation notes if discovered.

- **Risk: department signal priority may be ambiguous**
  - Mitigation: preserve existing ordering from the source data unless there is an explicit business-priority field already available.

Follow-ups to note if encountered:
- Standardize dashboard route generation through a shared route helper if links are currently duplicated.
- Consider adding dedicated component tests for dashboard composition if the repo lacks UI coverage.
- Consider centralizing dashboard telemetry event names/properties if this task reveals duplication across components.