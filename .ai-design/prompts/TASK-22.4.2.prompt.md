# Goal
Implement backlog task **TASK-22.4.2 — Implement deep-link routing for approvals, tasks, and finance actions with contextual query parameters** for story **US-22.4 Rebuild dashboard layout, deep links, and telemetry for action-first usage**.

Deliver a production-ready implementation in the existing **.NET / Blazor Web App** solution that:

- Updates `Dashboard.razor` to render sections in the exact required order.
- Uses a responsive layout with desktop two-column behavior and single-column collapse on smaller viewports.
- Ensures all dashboard CTAs navigate to valid deep links with contextual query parameters.
- Captures dashboard telemetry for:
  - focus item clicks
  - first action timestamp
  - scroll depth
  - measurable time-to-first-action per dashboard session
- Limits department cards to at most 3 key signals and suppresses zero-value metrics.

Do not redesign unrelated dashboard features. Keep changes aligned with the modular monolith and existing Blazor patterns in the repo.

# Scope
In scope:

- Dashboard page/component composition and ordering
- Responsive dashboard layout styling/markup
- CTA routing and query-string generation for approvals, tasks, and finance actions
- Dashboard session telemetry instrumentation
- Department card rendering rules for signal count and zero suppression
- Tests for routing/query generation, telemetry behavior, and rendering rules where practical

Out of scope unless required to satisfy build/tests:

- Large visual redesign beyond acceptance criteria
- New backend analytics platform or external telemetry vendor integration
- Reworking unrelated modules/pages
- Mobile app changes
- Broad refactors of dashboard data contracts unless necessary for this task

Acceptance criteria to satisfy exactly:

1. `Dashboard.razor` renders sections in this order:
   - `CompanyHealthBanner`
   - `TodayFocusPanel`
   - `TopActionsList`
   - `FinanceSnapshot` and `OperationsSnapshot` grid
   - `AgentActivityPanel`
   - `DepartmentCards`
2. Dashboard uses a responsive two-column layout on desktop and collapses to a single column on smaller viewports.
3. All dashboard action CTAs route to valid deep links including required query parameters for context, e.g. `/approvals?filter=pending`.
4. Telemetry records focus item clicks, first action timestamp, and scroll depth for each dashboard session.
5. A dashboard session emits a measurable time-to-first-action metric when a user clicks any actionable CTA.
6. Department cards render no more than 3 key signals and suppress zero-value metrics.

# Files to touch
Inspect the repo first and then update the actual matching files. Expected likely touch points include:

- `src/VirtualCompany.Web/Pages/Dashboard.razor`
- `src/VirtualCompany.Web/Pages/Dashboard.razor.cs` or equivalent code-behind/viewmodel if present
- Dashboard child components under something like:
  - `src/VirtualCompany.Web/Components/Dashboard/...`
  - `src/VirtualCompany.Web/Shared/...`
- Dashboard styling files, likely one of:
  - `src/VirtualCompany.Web/wwwroot/css/...`
  - component-scoped `.razor.css` files
- Telemetry service/contracts in web/application layers, likely under:
  - `src/VirtualCompany.Web/Services/...`
  - `src/VirtualCompany.Application/...`
  - `src/VirtualCompany.Shared/...`
- JS interop files if scroll tracking is implemented client-side:
  - `src/VirtualCompany.Web/wwwroot/js/...`
- Route targets for approvals/tasks/finance pages if query handling needs to be validated or added
- Relevant test projects/files, likely:
  - `tests/VirtualCompany.Api.Tests/...`
  - any existing web/component/unit test project if present

Before editing, locate:
- the actual dashboard page/component
- existing telemetry abstractions
- existing navigation/deep-link helpers
- existing department card models
- any current query parameter conventions for approvals/tasks/finance pages

If a reusable helper does not exist, add a small focused one rather than duplicating URL construction logic across components.

# Implementation plan
1. **Discover current dashboard structure**
   - Find the current `Dashboard.razor` and all child components.
   - Map current render order against required order.
   - Identify how data is passed into:
     - `CompanyHealthBanner`
     - `TodayFocusPanel`
     - `TopActionsList`
     - `FinanceSnapshot`
     - `OperationsSnapshot`
     - `AgentActivityPanel`
     - `DepartmentCards`
   - Confirm whether these are already separate components or need extraction/reordering only.

2. **Reorder dashboard sections to match acceptance criteria**
   - Update `Dashboard.razor` markup so the sections render in this exact order:
     1. `CompanyHealthBanner`
     2. `TodayFocusPanel`
     3. `TopActionsList`
     4. `FinanceSnapshot` and `OperationsSnapshot` in a shared grid row
     5. `AgentActivityPanel`
     6. `DepartmentCards`
   - Preserve existing data loading and empty-state behavior.
   - Avoid introducing hidden conditional ordering bugs; if a section is conditionally absent, keep the remaining visible sections in the same relative order.

3. **Implement responsive layout**
   - Add or update layout containers/CSS so:
     - desktop uses a two-column layout where appropriate
     - smaller viewports collapse to a single column
   - Prefer CSS Grid/Flex with clear breakpoints already used in the app.
   - Ensure the `FinanceSnapshot` and `OperationsSnapshot` render as a grid pair on desktop and stack on smaller screens.
   - Keep accessibility and semantic structure intact.

4. **Standardize dashboard CTA deep links**
   - Audit all actionable CTAs in dashboard components:
     - focus items
     - top actions
     - finance actions
     - approvals links
     - task links
     - any department card action links
   - Ensure each CTA navigates to a valid route with contextual query parameters.
   - Examples of expected patterns:
     - `/approvals?filter=pending`
     - `/tasks?filter=today`
     - `/tasks?status=blocked&source=dashboard`
     - `/finance?action=review&range=this-month`
   - Add a small helper/builder for dashboard deep links if needed, e.g.:
     - `DashboardDeepLinkBuilder`
     - typed methods for approvals/tasks/finance routes
   - Include a `source=dashboard` or equivalent context parameter if consistent with current conventions and useful for telemetry attribution.
   - Do not hardcode malformed or inconsistent query strings across components.

5. **Validate target pages accept the deep links**
   - Check approvals/tasks/finance pages for query parameter binding.
   - If needed, add minimal support so the linked pages can parse and honor the expected query parameters.
   - Keep this minimal and focused on validity/usability of the deep links, not a broad filtering overhaul.
   - If a route does not exist, wire the CTA to the closest valid existing route and document the gap in follow-ups.

6. **Add dashboard session telemetry model**
   - Implement or extend a dashboard telemetry service to track a per-session lifecycle.
   - Session should capture at minimum:
     - session start timestamp
     - focus item click events
     - first action timestamp
     - scroll depth milestones or max scroll depth
     - time-to-first-action metric
   - Prefer a lightweight client-side session ID generated on dashboard load and included in emitted events.
   - Keep telemetry payloads structured and tenant-safe; do not include sensitive business content unnecessarily.

7. **Instrument actionable CTA clicks**
   - For every actionable dashboard CTA:
     - emit a click event with action type, target route, and context
     - if this is the first actionable click in the session, record first action timestamp
     - emit or compute time-to-first-action from dashboard load/session start
   - Ensure focus item clicks are explicitly tracked, not just generic CTA clicks.
   - Avoid double-counting when navigation and click handlers both fire.

8. **Implement scroll depth tracking**
   - Use Blazor JS interop if needed to observe scroll progress.
   - Track either:
     - max scroll percentage reached, and/or
     - milestone events such as 25/50/75/100
   - Persist/report scroll depth once per milestone or as max depth to avoid noisy telemetry.
   - Clean up event listeners on component disposal.

9. **Enforce department card signal rules**
   - Update department card rendering logic so:
     - zero-value metrics/signals are suppressed
     - no more than 3 key signals are shown
   - Apply this in the view model/preparation layer if possible, not only in markup.
   - Preserve meaningful ordering of signals; if there is no explicit priority, use existing order after filtering.
   - Ensure cards with fewer than 3 non-zero signals still render cleanly.

10. **Add tests**
   - Add focused tests for:
     - dashboard section ordering if testable at component/viewmodel level
     - deep-link generation/query parameters
     - department card filtering to max 3 non-zero signals
     - telemetry first-action behavior and time-to-first-action calculation
   - If UI component tests are not available in the repo, add unit tests around helpers/services/viewmodels instead of introducing a large new test framework.
   - Keep tests deterministic.

11. **Keep implementation aligned with architecture**
   - Respect clean boundaries:
     - UI concerns in Web
     - reusable contracts/helpers in Shared/Application only if broadly useful
   - Do not put telemetry persistence logic directly into page markup if an existing service abstraction exists.
   - Keep tenant context and audit/telemetry concerns structured and future-extensible.

# Validation steps
1. **Codebase discovery**
   - Search for dashboard and related components:
     - `Dashboard.razor`
     - `CompanyHealthBanner`
     - `TodayFocusPanel`
     - `TopActionsList`
     - `FinanceSnapshot`
     - `OperationsSnapshot`
     - `AgentActivityPanel`
     - `DepartmentCards`

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Manual dashboard verification**
   - Launch the web app and navigate to dashboard.
   - Confirm visible section order matches acceptance criteria exactly.
   - Confirm desktop layout shows two-column behavior where intended.
   - Confirm smaller viewport collapses to one column.

5. **Manual deep-link verification**
   - Click every dashboard CTA.
   - Verify resulting URLs include expected contextual query parameters.
   - Verify destination pages load successfully and reflect the intended context/filter where supported.

6. **Manual telemetry verification**
   - Open browser dev tools/network/logging as appropriate.
   - Confirm dashboard session starts on page load.
   - Click a focus item and verify telemetry event is emitted.
   - Click first actionable CTA and verify:
     - first action timestamp is recorded
     - time-to-first-action metric is emitted/measurable
   - Scroll the dashboard and verify scroll depth telemetry updates without excessive duplicate events.

7. **Department card verification**
   - Confirm each department card shows at most 3 signals.
   - Confirm zero-value metrics are not rendered.
   - Confirm cards still render correctly when all or most signals are filtered out.

8. **Regression check**
   - Verify no broken navigation, console errors, or JS interop disposal issues.
   - Verify no unrelated pages fail due to query parameter or telemetry changes.

# Risks and follow-ups
- **Unknown existing telemetry stack**: the repo may not yet have a telemetry abstraction for dashboard sessions. If absent, add a minimal internal service and keep it easy to replace later.
- **Unknown route/query conventions**: approvals/tasks/finance pages may not consistently support query parameters yet. Implement the smallest valid support needed and document any remaining normalization work.
- **Blazor render/event duplication risk**: click handlers plus navigation can accidentally emit duplicate telemetry. Guard first-action logic carefully.
- **Scroll tracking noise/perf risk**: throttle or milestone-based tracking to avoid excessive event volume.
- **Responsive layout regressions**: verify existing shared CSS breakpoints before adding new ones to avoid conflicting styles.
- **Department signal semantics**: if “zero-value” can mean numeric zero, null, empty string, or false-like values, align with existing domain meaning and document assumptions in code/tests.
- **Potential follow-up**:
  - unify all dashboard deep links behind a shared route builder
  - standardize `source=dashboard` attribution across destination pages and telemetry
  - add richer dashboard analytics dashboards/reporting later if product wants aggregate action-first usage metrics