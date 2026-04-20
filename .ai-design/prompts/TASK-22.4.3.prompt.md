# Goal
Implement backlog task **TASK-22.4.3 — Add DashboardInteractionService to capture focus clicks, time-to-first-action, and scroll depth events** for **US-22.4 Rebuild dashboard layout, deep links, and telemetry for action-first usage**.

Deliver a production-ready dashboard telemetry implementation in the existing .NET solution that:

- captures dashboard session telemetry for:
  - focus item clicks
  - time-to-first-action
  - scroll depth milestones
- supports the rebuilt dashboard layout and CTA deep-link behavior required by the acceptance criteria
- fits the existing architecture boundaries for Blazor Web, ASP.NET Core modular monolith, tenant-aware analytics/cockpit behavior, and audit/telemetry separation
- is testable, minimally invasive, and safe for SSR/interactive Blazor behavior

# Scope
In scope:

- Add a **DashboardInteractionService** in the web app layer to manage per-dashboard-session interaction tracking.
- Wire the service into **Dashboard.razor** and any related dashboard components needed to:
  - start a dashboard session
  - record first actionable CTA click
  - record focus item clicks
  - record scroll depth milestones
- Ensure dashboard sections render in this exact order:
  1. CompanyHealthBanner
  2. TodayFocusPanel
  3. TopActionsList
  4. FinanceSnapshot and OperationsSnapshot grid
  5. AgentActivityPanel
  6. DepartmentCards
- Ensure dashboard layout is responsive:
  - two-column on desktop
  - single-column on smaller viewports
- Ensure all dashboard CTAs navigate to valid deep links with required query parameters for context.
- Ensure department cards:
  - render no more than 3 key signals
  - suppress zero-value metrics
- Add or update telemetry contracts, JS interop, and tests as needed.

Out of scope unless required by existing code patterns:

- Building a full analytics warehouse or backend reporting pipeline
- Broad redesign of unrelated dashboard data queries
- Mobile-specific telemetry
- Reworking unrelated audit event infrastructure unless the current app already routes UI telemetry through a shared endpoint/service

# Files to touch
Inspect first, then update the smallest coherent set of files likely including:

- `src/VirtualCompany.Web/Pages/Dashboard.razor`
- `src/VirtualCompany.Web/Pages/Dashboard.razor.cs` or code-behind/viewmodel if present
- `src/VirtualCompany.Web/Components/...` for:
  - `CompanyHealthBanner`
  - `TodayFocusPanel`
  - `TopActionsList`
  - `FinanceSnapshot`
  - `OperationsSnapshot`
  - `AgentActivityPanel`
  - `DepartmentCards`
- `src/VirtualCompany.Web/Services/.../DashboardInteractionService.cs`
- `src/VirtualCompany.Web/wwwroot/js/...` for scroll/click telemetry interop
- `src/VirtualCompany.Web/_Imports.razor` or DI registration location
- `src/VirtualCompany.Web/Program.cs`
- Shared DTO/event contract files if telemetry payloads are modeled centrally:
  - `src/VirtualCompany.Shared/...`
  - `src/VirtualCompany.Application/...`
- API endpoint or application service files only if the web app persists telemetry server-side rather than client-only logging
- Tests:
  - `tests/VirtualCompany.Api.Tests/...` if API endpoint behavior is involved
  - any existing web/component/unit test project if present

Also inspect for existing patterns before creating new ones:

- telemetry/event services
- JS interop modules
- dashboard query/view models
- navigation/deep-link helpers
- responsive layout conventions
- component test patterns

# Implementation plan
1. **Discover existing dashboard and telemetry patterns**
   - Locate the current dashboard page/component tree and identify how sections are currently composed.
   - Find any existing telemetry abstractions, event logging services, audit event services, or JS interop helpers.
   - Determine whether dashboard telemetry should:
     - stay in the web layer only,
     - call an API endpoint,
     - or flow through an existing application service.
   - Reuse established naming, DI, and folder conventions.

2. **Define the dashboard interaction model**
   - Introduce a focused service abstraction, e.g.:
     - `IDashboardInteractionService`
     - `DashboardInteractionService`
   - The service should manage a single dashboard session lifecycle per page visit, including:
     - session identifier
     - session start timestamp
     - first action recorded flag/timestamp
     - highest scroll depth milestone reached
   - Model telemetry events clearly, for example:
     - `dashboard_session_started`
     - `dashboard_focus_item_clicked`
     - `dashboard_first_action_recorded`
     - `dashboard_scroll_depth_reached`
   - Keep telemetry separate from business audit events unless the codebase already intentionally combines them.

3. **Implement session lifecycle and time-to-first-action**
   - Start a dashboard session when the dashboard becomes interactive.
   - Record session start using a monotonic or UTC timestamp approach consistent with the app.
   - On the first actionable CTA click:
     - compute elapsed time from session start
     - emit a measurable time-to-first-action metric/event
     - ensure it is emitted only once per dashboard session
   - Subsequent CTA clicks may still emit click events, but must not overwrite the first-action metric.

4. **Capture focus item clicks**
   - In `TodayFocusPanel` or the component rendering focus items, instrument actionable focus CTAs.
   - Emit telemetry with enough context to be useful but not excessive, such as:
     - session id
     - tenant/company context if already available in telemetry conventions
     - focus item id/type
     - destination route
     - position/index
   - Ensure the click still navigates normally.

5. **Capture scroll depth**
   - Add a lightweight JS interop module to observe dashboard scroll progress.
   - Emit milestone-based events only, not noisy continuous events. Recommended milestones:
     - 25%
     - 50%
     - 75%
     - 100%
   - Deduplicate milestones so each is emitted once per session.
   - Dispose listeners cleanly on component teardown to avoid leaks or duplicate subscriptions.

6. **Rebuild/verify dashboard section order**
   - Update `Dashboard.razor` composition so the rendered order exactly matches the acceptance criteria.
   - If the current layout uses nested grids/regions, preserve visual design while ensuring DOM/component order remains correct.
   - Keep the finance and operations snapshots grouped in the same grid row/section.

7. **Implement responsive layout**
   - Use existing app styling conventions first.
   - Ensure:
     - desktop: two-column layout
     - smaller viewports: single-column stacked layout
   - Prefer CSS grid/flex with clear breakpoints already used by the app.
   - Avoid introducing a new styling system if one already exists.

8. **Validate and fix CTA deep links**
   - Audit all dashboard action CTAs in:
     - focus panel
     - top actions
     - banners
     - cards/snapshots if actionable
   - Ensure every CTA routes to a valid deep link with required context query parameters.
   - Example target shape:
     - `/approvals?filter=pending`
   - Centralize route generation if there is repeated logic, e.g. a helper that builds dashboard deep links safely.
   - Avoid hardcoded malformed URLs or missing context parameters.

9. **Constrain department card signals**
   - Update department card rendering logic so each card:
     - shows at most 3 key signals
     - suppresses zero-value metrics
   - Apply filtering before rendering.
   - If fewer than 3 non-zero signals exist, render only those available.
   - Keep ordering deterministic and aligned with product intent.

10. **Register dependencies and interop**
    - Register `DashboardInteractionService` in DI with the correct lifetime for Blazor usage.
    - Add JS module references and initialization/disposal hooks.
    - Ensure SSR-safe behavior:
      - no JS interop before interactive render
      - null/failed interop handled gracefully

11. **Add tests**
    - Add unit/component tests for:
      - first action metric emitted once
      - focus click telemetry emitted with expected payload
      - scroll depth milestone deduplication logic
      - department cards suppress zero metrics and cap at 3 signals
      - deep links include required query parameters
    - If practical in current test setup, add rendering/order assertions for dashboard section order.
    - If API persistence is involved, add endpoint/application tests for telemetry ingestion.

12. **Keep implementation production-safe**
    - Do not log sensitive payloads.
    - Keep telemetry payloads compact and tenant-safe.
    - Ensure failures in telemetry do not block navigation or dashboard rendering.
    - Prefer fire-and-forget only if existing patterns safely support it; otherwise await lightweight calls with graceful fallback.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual dashboard verification in the web app:
   - Open dashboard and confirm section order is exactly:
     1. CompanyHealthBanner
     2. TodayFocusPanel
     3. TopActionsList
     4. FinanceSnapshot and OperationsSnapshot grid
     5. AgentActivityPanel
     6. DepartmentCards
   - Resize viewport:
     - desktop shows two-column layout
     - smaller viewport collapses to one column

4. Manual CTA verification:
   - Click each dashboard CTA and confirm navigation targets are valid and include required query parameters where applicable.
   - Specifically verify examples like:
     - `/approvals?filter=pending`

5. Manual telemetry verification:
   - Open browser dev tools/network or app logging output depending on implementation.
   - Confirm dashboard session starts once per page visit.
   - Click a focus item CTA:
     - focus click event is emitted
     - first action metric is emitted
     - time-to-first-action value is populated
   - Click additional CTAs:
     - click events continue if intended
     - first action metric is not emitted again
   - Scroll through the dashboard:
     - milestone events fire at expected thresholds
     - each threshold is emitted only once

6. Manual department card verification:
   - Confirm each department card shows no more than 3 signals.
   - Confirm zero-value metrics are hidden.

7. Regression check:
   - Ensure telemetry failures do not break dashboard rendering or navigation.
   - Ensure no duplicate JS listeners after navigating away and back.

# Risks and follow-ups
- **Unknown existing telemetry architecture**
  - There may already be a shared telemetry/event pipeline. Reuse it instead of inventing a parallel path.

- **Blazor render mode / JS interop timing**
  - If the dashboard uses SSR with later interactivity, JS interop must only initialize after first interactive render.

- **Duplicate event emission**
  - Re-renders, reconnections, or repeated component initialization can cause duplicate session or scroll events if not guarded carefully.

- **Navigation race conditions**
  - CTA telemetry must not block or break navigation. If telemetry is async, ensure the UX remains responsive.

- **Responsive layout regressions**
  - Reordering sections for acceptance criteria may unintentionally affect existing CSS/grid behavior. Validate across breakpoints.

- **Deep-link drift**
  - Hardcoded routes across multiple components can become inconsistent. A follow-up may be to centralize dashboard route generation.

- **Telemetry persistence gap**
  - If no backend endpoint exists yet, a follow-up task may be needed to persist and report these events in analytics infrastructure.

- **Test coverage limitations**
  - If the solution lacks web component test infrastructure, add focused unit tests now and note a follow-up for richer Blazor component/UI tests.