# Goal
Implement backlog task **TASK-22.1.2 — Add `GET /api/dashboard/focus` endpoint with tenant and user scoping** for story **US-22.1 Implement Today’s Focus aggregation API and primary decision panel**.

Deliver a production-ready vertical slice that:
- Adds a tenant- and user-scoped backend API endpoint at `GET /api/dashboard/focus`
- Returns **3 to 5** `FocusItem` records ordered by `PriorityScore` descending
- Aggregates focus candidates from **approvals, tasks, anomalies, and finance alerts** when those domains have data
- Ensures every returned item includes non-empty:
  - `Id`
  - `Title`
  - `Description`
  - `ActionType`
  - `PriorityScore`
  - `NavigationTarget`
- Normalizes `PriorityScore` to an integer in the range **0..100**
- Wires the web dashboard **TodayFocusPanel** to render returned items as cards with CTA buttons
- Ensures CTA click navigates to the exact `NavigationTarget` returned by the API

Work within the existing **.NET modular monolith** architecture and preserve clean boundaries:
- API layer: HTTP contract and auth/tenant resolution
- Application layer: query + aggregation orchestration
- Domain/infrastructure: data access and scoring inputs
- Web layer: panel rendering and navigation behavior

# Scope
In scope:
- Add/extend backend query contract, handler/service, endpoint/controller/minimal API registration, DTOs, and tests
- Enforce **company/tenant scoping** and **user scoping**
- Aggregate from currently available domain tables/services for:
  - approvals
  - tasks
  - anomalies
  - finance alerts
- If some domains are not yet fully implemented in this codebase, integrate with existing equivalents or add safe placeholder adapters that return no items rather than inventing fake persistence
- Implement deterministic selection and ordering logic to return **3 to 5** items when enough data exists
- Normalize scores to `0..100`
- Update the Blazor dashboard panel to consume the API and render cards + CTA navigation
- Add automated tests covering API behavior, scoping, ordering, and UI behavior where practical

Out of scope:
- New broad dashboard redesign beyond TodayFocusPanel
- New persistence model for unrelated dashboard widgets
- Mobile app changes
- Large refactors of auth/tenant infrastructure unless required for this endpoint
- Inventing unsupported anomaly/finance subsystems if they do not exist; instead, integrate through extension points and document follow-ups

# Files to touch
Inspect the solution first and then touch the minimum necessary files in these likely areas.

Backend:
- `src/VirtualCompany.Api/**`
  - endpoint/controller for dashboard focus
  - request context / auth / tenant resolution wiring
  - API response models if defined here
- `src/VirtualCompany.Application/**`
  - dashboard query contract and handler
  - focus aggregation service
  - DTOs/view models
  - interfaces for domain data providers
- `src/VirtualCompany.Domain/**`
  - only if shared domain concepts/enums/value objects are needed
- `src/VirtualCompany.Infrastructure/**`
  - repository/query implementations for approvals/tasks/anomalies/finance alerts
  - EF Core query composition or SQL access
  - DI registrations

Frontend:
- `src/VirtualCompany.Web/**`
  - TodayFocusPanel component/page
  - dashboard API client/service
  - card rendering and CTA navigation

Tests:
- `tests/VirtualCompany.Api.Tests/**`
  - endpoint/query tests
  - tenant/user scoping tests
  - ordering and score normalization tests
- Add web/UI tests only if the repo already has an established pattern; otherwise keep UI validation lightweight and document manual verification

Also review:
- `README.md`
- any existing dashboard, approvals, tasks, alerts, or query patterns in the solution
- any existing auth/tenant abstractions before introducing new ones

# Implementation plan
1. **Discover existing patterns before coding**
   - Inspect how the solution currently implements:
     - authenticated API endpoints
     - company/tenant resolution
     - current-user resolution
     - CQRS/query handlers
     - dashboard endpoints
     - Blazor data fetching and navigation
   - Reuse established naming, folder structure, DI registration, and test style.

2. **Define the response contract**
   - Add a `FocusItem` response model with:
     - `Id`
     - `Title`
     - `Description`
     - `ActionType`
     - `PriorityScore`
     - `NavigationTarget`
   - Ensure all fields are required/non-null in code.
   - Add a query/response wrapper if the codebase uses one; otherwise return a list/array directly.
   - Keep the contract stable and simple for web/mobile reuse.

3. **Add application-layer query**
   - Create a query such as `GetDashboardFocusQuery(companyId, userId)` and a handler/service.
   - The handler must:
     - validate/require company and user context
     - gather candidate items from available providers
     - merge candidates across domains
     - normalize scores
     - sort descending by `PriorityScore`
     - return **up to 5**
     - ensure at least **3** when enough source data exists
   - Prefer a provider-based design:
     - `IApprovalFocusProvider`
     - `ITaskFocusProvider`
     - `IAnomalyFocusProvider`
     - `IFinanceAlertFocusProvider`
     - or one generic `IFocusItemSource`
   - This keeps domain-specific logic isolated and extensible.

4. **Implement tenant and user scoping**
   - Scope every source query by `companyId`.
   - Scope user-relevant items by `userId` where applicable, for example:
     - approvals assigned to the user or their role/membership
     - tasks assigned to or created for the user, or otherwise relevant per existing business rules
     - anomalies/finance alerts filtered through company scope and optionally user visibility rules if such rules exist
   - Do not trust arbitrary querystring `companyId`/`userId` without checking against authenticated context and membership rules already present in the app.
   - If the app convention is to derive user/company from claims/headers/context rather than raw query params, follow that convention exactly.
   - If acceptance requires `companyId` and `userId`, support them in the way the existing API style expects, but still authorize them.

5. **Build focus candidate mapping per domain**
   - For each domain, map source records into a common candidate shape:
     - source type
     - source id
     - title
     - description
     - action type
     - raw score inputs
     - navigation target
   - Suggested examples:
     - approvals → “Review approval”, target approval detail/inbox route
     - tasks → “Resume task” / “Resolve blocked task”, target task detail route
     - anomalies → “Investigate anomaly”, target anomaly detail/list route
     - finance alerts → “Review finance alert”, target finance alert detail/list route
   - Keep descriptions concise and user-facing.
   - Ensure `NavigationTarget` is an exact app route the web UI can navigate to.

6. **Implement scoring and normalization**
   - Create deterministic scoring logic per candidate using available signals such as:
     - urgency/status
     - due date proximity/overdue
     - severity
     - approval pending age
     - blocked state
     - financial risk/impact if available
   - Normalize final scores to integer `0..100`.
   - Avoid randomization.
   - If raw scores differ by domain, normalize after aggregation or map each domain into a common weighted scale.
   - Add tie-breakers for stable ordering, e.g.:
     1. `PriorityScore` descending
     2. source severity/urgency descending
     3. created/updated timestamp descending
     4. id ascending

7. **Selection logic**
   - Return **3 to 5** items when enough candidates exist.
   - If fewer than 3 candidates exist in the system for that scoped user/company, return the available count rather than fabricating items.
   - Prefer source diversity when practical, but do not violate score ordering acceptance criteria.
   - Include items from approvals, tasks, anomalies, and finance alerts **when data exists** in those domains.
   - A reasonable approach:
     - collect top candidates from each source
     - merge all
     - normalize and sort
     - take top 5
     - if one source dominates, that is acceptable as long as other domain items can appear when their scores warrant it
   - If product intent clearly favors domain diversity and the code can support it without complexity, add a light balancing rule and document it.

8. **Expose API endpoint**
   - Add `GET /api/dashboard/focus`.
   - Follow existing API conventions for:
     - route registration
     - auth attributes/policies
     - result envelope/problem details
   - Return appropriate responses:
     - `200 OK` with focus items for authorized requests
     - `401` if unauthenticated
     - `403`/`404` per existing tenant access conventions for unauthorized tenant access
   - Keep endpoint thin; delegate logic to application layer.

9. **Wire infrastructure queries**
   - Implement source queries against existing persistence models.
   - Reuse existing repositories/DbContext patterns.
   - Keep queries read-only and efficient:
     - filter early by `company_id`
     - filter by user relevance where applicable
     - project only needed fields
     - limit candidate counts before in-memory merge
   - If anomalies or finance alerts do not yet have concrete tables/entities:
     - search for existing alert/notification/audit/KPI exception models that can serve as source data
     - if none exist, implement provider stubs returning empty results and clearly note follow-up gaps in comments/tests only if necessary

10. **Update TodayFocusPanel in Blazor**
    - Find the dashboard panel component and connect it to the new API.
    - Render each focus item as a card with:
      - title
      - short description
      - CTA button
    - CTA label can derive from `ActionType` or existing UX conventions.
    - On click, navigate using the exact `NavigationTarget` from the API via `NavigationManager.NavigateTo(...)`.
    - Handle loading, empty, and error states gracefully.
    - Do not hardcode routes in the component beyond consuming `NavigationTarget`.

11. **Add tests**
    - Backend tests:
      - authorized request returns ordered items by descending `PriorityScore`
      - every item has non-empty required fields
      - every `PriorityScore` is integer `0..100`
      - tenant scoping prevents cross-company leakage
      - user scoping prevents unrelated user items from appearing
      - source inclusion works for approvals/tasks/anomalies/finance alerts when data exists
      - endpoint returns max 5 items
      - endpoint returns available items if fewer than 3 exist
    - UI/component tests if supported:
      - panel renders cards from API payload
      - CTA click navigates to exact `NavigationTarget`
    - If no UI test harness exists, keep backend automated and provide manual verification steps for the panel.

12. **Keep implementation clean**
    - Avoid embedding business logic in controllers or Razor components.
    - Avoid direct DB access from API or web layers.
    - Keep DTOs separate from persistence entities.
    - Preserve modular monolith boundaries and CQRS-lite style.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify API:
   - Start the app using the repo’s normal local run configuration.
   - Call `GET /api/dashboard/focus` as an authenticated user in a valid company context.
   - Confirm:
     - response count is between 3 and 5 when enough data exists
     - items are ordered by `PriorityScore` descending
     - all required fields are populated
     - scores are integers between 0 and 100
     - cross-tenant data is not returned
     - user-irrelevant items are not returned

4. Manually verify source coverage:
   - Seed or use test data for approvals, tasks, anomalies, and finance alerts.
   - Confirm items from each domain appear when data exists and is relevant.

5. Manually verify web panel:
   - Open dashboard in the Blazor web app.
   - Confirm TodayFocusPanel renders cards with title, short description, and CTA.
   - Click each CTA and verify navigation goes to the exact `NavigationTarget` returned by the API.

6. If there are snapshot/golden/manual test docs in the repo, update them only if the repo already uses that pattern.

# Risks and follow-ups
- **Unknown existing data models for anomalies and finance alerts**  
  These domains may not yet have concrete persistence/entities. Reuse existing alert/exception models if present; otherwise implement empty providers and document a follow-up to connect real sources.

- **Ambiguity in companyId/userId sourcing**  
  Acceptance mentions valid `companyId` and `userId`, but the app may derive these from auth context rather than request parameters. Follow existing security conventions and do not weaken authorization for convenience.

- **NavigationTarget route mismatch**  
  Ensure returned routes correspond to actual web routes. If some detail pages do not exist yet, target the nearest valid destination page and document any UX gap.

- **Score normalization fairness across domains**  
  Different domains may have different urgency semantics. Keep scoring deterministic and simple now; document future tuning if needed.

- **Insufficient seeded data for 3–5 items**  
  The endpoint should not fabricate items. Tests should distinguish “enough data exists” from sparse environments.

- **UI test coverage may be limited**  
  If the repo lacks a Blazor component test setup, prioritize backend automation and provide explicit manual verification for panel rendering and navigation.

- **Potential follow-up tasks**
  - refine cross-domain scoring weights with product input
  - add caching for dashboard focus query if needed
  - add richer source attribution/icons in the panel
  - add anomaly/finance alert domain integrations if currently stubbed