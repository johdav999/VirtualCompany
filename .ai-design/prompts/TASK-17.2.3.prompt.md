# Goal

Implement `TASK-17.2.3` for **US-17.2 ST-FUI-172 — Finance summary views for cash, balances, monthly summary, and anomalies** by adding reusable UI state components and wiring finance summary screens to existing backend endpoints in the Blazor web app.

The coding agent should deliver:

- Reusable **loading**, **empty**, and **error** state components for finance views
- Finance summary screens for:
  - cash position
  - balances
  - monthly summary
  - anomalies
- Client-side navigation between those screens without full page reload
- Tenant-scoped data loading from existing finance backend endpoints
- Integration coverage proving UI-visible summary values match backend responses for the active tenant
- Monthly summary category breakdown rendering when category aggregate data is returned

Do not redesign the finance domain or backend contracts unless required to consume existing endpoints cleanly.

# Scope

In scope:

- Inspect existing finance UI, routing, API client/service layer, DTOs, and tests
- Reuse existing backend finance endpoints if already present
- Add or extend typed query/service methods in the web/app client layer to call those endpoints
- Create reusable finance-oriented state components or generic state components usable by finance pages
- Update the four finance summary screens to support:
  - loading
  - empty
  - success
  - error
- Ensure navigation between finance summary screens uses Blazor routing/linking and does not trigger full page reload
- Render monthly summary expense breakdown by category when category aggregate data exists
- Add/extend integration tests for tenant scoping and response/UI consistency

Out of scope unless necessary for compilation or endpoint consumption:

- New finance calculations in backend business logic
- New database schema/migrations
- Broad visual redesign outside the finance summary views
- Mobile app changes
- Non-finance dashboard work

# Files to touch

Likely areas to inspect and update first:

- `src/VirtualCompany.Web/**`
- `src/VirtualCompany.Shared/**`
- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `tests/VirtualCompany.Api.Tests/**`

Probable concrete file categories:

- Finance pages/components in Blazor web app
  - e.g. `Pages/Finance/*`, `Components/Finance/*`, `Shared/*`
- Navigation/menu components for finance section
- Web-side API client/service abstractions for finance endpoints
- Shared DTOs/contracts if already centralized
- API endpoint definitions only if the UI currently lacks access to existing finance endpoints
- Integration tests covering finance endpoints and tenant scoping
- Component tests/UI tests if the repo already uses them

If present, prefer touching existing finance-specific files over introducing parallel abstractions.

# Implementation plan

1. **Discover current finance implementation**
   - Find existing finance routes, pages, nav, DTOs, and API endpoints.
   - Identify the existing backend endpoints for:
     - cash position
     - balances
     - monthly summary
     - anomalies
   - Confirm how tenant context is resolved today in API and web layers.
   - Determine whether the web app currently uses SSR, interactive components, or a mixed pattern for these views.

2. **Define a reusable state pattern**
   - Introduce reusable components for:
     - loading state
     - empty state
     - error state
   - Keep them generic enough for finance summary views, but optimized for current use.
   - Recommended approach:
     - a small set of components such as `LoadingState`, `EmptyState`, `ErrorState`
     - or a single wrapper like `DataStateContainer` if that matches existing patterns
   - Ensure components support:
     - title/message
     - optional retry callback/event
     - optional icon/visual variant
     - accessibility-friendly markup

3. **Standardize finance page data-loading flow**
   - For each finance summary screen, implement a consistent state model:
     - `IsLoading`
     - `HasError`
     - `ErrorMessage`
     - `HasData` / empty detection
     - response model
   - Use existing typed clients/services to fetch data from backend endpoints.
   - If no typed client exists, add one in the appropriate web/shared layer rather than calling `HttpClient` ad hoc from pages.

4. **Implement the four finance summary screens**
   - Update cash position screen to:
     - call existing endpoint
     - show loading/empty/error/success states
   - Update balances screen similarly
   - Update monthly summary screen similarly
   - Update anomalies screen similarly
   - Empty-state rules should be based on actual response semantics:
     - null/missing payload
     - zero records
     - no aggregates returned
   - Avoid treating valid zero-valued summaries as empty unless the contract clearly indicates no data.

5. **Render monthly category breakdown**
   - Inspect monthly summary response contract for category aggregate data.
   - If category aggregate data is returned, render an expense breakdown by category.
   - If not returned, do not fail the page; simply omit that section or show a contextual empty subsection.
   - Preserve backend contract naming and avoid inventing new fields unless already required by shared DTO cleanup.

6. **Ensure tenant-scoped correctness**
   - Verify all finance requests are made in the active tenant context.
   - Confirm API-side tenant enforcement is already applied.
   - If tests reveal gaps, fix tenant scoping in the query/endpoint layer rather than in UI-only filtering.
   - UI must display values exactly from backend responses for the active tenant.

7. **Enable client-side navigation**
   - Ensure links between finance summary screens use Blazor navigation primitives (`NavLink`, `NavigationManager`, route components) and do not force full reloads.
   - If current implementation uses plain anchors or server redirects, replace with Blazor-native navigation where appropriate.
   - Preserve route structure if already established.

8. **Add integration tests**
   - Add or extend tests to verify:
     - each finance endpoint returns tenant-scoped data
     - unauthorized cross-tenant access is blocked or isolated
     - summary values used by UI-facing contracts match expected backend responses
     - monthly summary includes category breakdown data when available
   - If there is an existing web/component test setup, add UI-state coverage there too.
   - If not, prioritize API integration tests plus any existing page-level tests already used in the repo.

9. **Polish and align with existing conventions**
   - Match naming, folder structure, and component style already used in the solution.
   - Keep logic out of Razor markup where possible; prefer code-behind/view-model/service patterns if already used.
   - Avoid introducing unnecessary new frameworks or packages.

# Validation steps

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Navigate to each finance summary screen:
     - cash position
     - balances
     - monthly summary
     - anomalies
   - Confirm each screen shows:
     - loading state during fetch
     - empty state when endpoint returns no meaningful data
     - success state when data exists
     - error state on failed request
   - Confirm monthly summary shows category breakdown when category aggregates are present
   - Confirm switching between finance screens does not trigger full page reload

4. Validate tenant behavior:
   - Use or add test fixtures for multiple tenants
   - Confirm active tenant only sees its own finance summaries
   - Confirm values displayed are identical to backend response payloads for that tenant

5. Regression check:
   - Ensure no unrelated routes/components break
   - Ensure finance navigation remains functional
   - Ensure no backend contract changes broke existing consumers

# Risks and follow-ups

- **Risk: unclear existing finance endpoint contracts**
  - Mitigation: inspect current API contracts first and adapt UI to them rather than guessing shapes.

- **Risk: empty-state ambiguity**
  - Some finance summaries may legitimately contain zero values.
  - Mitigation: define empty based on absence of records/aggregates, not numeric zero alone.

- **Risk: tenant scoping may currently be incomplete in backend**
  - Mitigation: fix at API/query layer if tests expose leakage; do not rely on UI filtering.

- **Risk: navigation behavior depends on current Blazor render mode**
  - Mitigation: preserve existing routing/render mode conventions and use Blazor-native links/components.

- **Risk: no existing UI test harness**
  - Mitigation: prioritize integration tests around API contracts and add focused component/page tests only if supported by the repo.

Follow-ups after completion if needed:

- Consolidate reusable state components for use beyond finance views
- Add skeleton loaders or richer visual states if design system work is planned
- Add component-level tests for state components if the web project adopts a UI test framework
- Consider a shared finance summary layout/tab shell if the four screens currently duplicate structure