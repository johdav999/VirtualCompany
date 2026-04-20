# Goal
Implement backlog task **TASK-18.1.1 — invoice review list page with finance workflow data binding and filterable table** for story **US-18.1 ST-FUI-201 — Invoice review workbench and recommendation drilldown**.

Deliver a production-ready vertical slice in the existing **.NET / Blazor Web App** solution that adds:

- A finance review list route in the web UI
- Data binding to the existing finance workflow APIs
- A filterable invoice review table with URL-synced query parameters
- A dedicated invoice review result/detail page
- Permission-aware action controls for approve / reject / send-for-follow-up
- Loading, empty, and API error states for both list and detail views
- Component tests covering the key UI states and permission/actionability behavior

Follow existing project conventions and architecture. Reuse existing API contracts, auth/tenant context, and finance workflow endpoints where available. Prefer extending current application/web patterns over introducing new frameworks or abstractions.

# Scope
In scope:

- Add or wire up a **finance review list page** in `VirtualCompany.Web`
- Bind the page to existing finance workflow APIs that return reviewable invoices
- Render table columns for:
  - invoice number
  - supplier name
  - amount
  - currency
  - risk level
  - recommendation status
  - confidence
  - last updated timestamp
- Add filters for:
  - status
  - supplier
  - risk level
  - recommendation outcome
- Keep filters reflected in the **URL query string**
- Add row selection/navigation to a **dedicated review result page**
- Render detail page fields:
  - recommendation summary
  - recommended action
  - confidence
  - link to source invoice
  - link to related approval when present
- Show approve / reject / send-for-follow-up actions **only** when:
  - current user has permission
  - invoice is in an actionable state
- Render loading / empty / error states for both list and detail pages
- Add component tests for the above states

Out of scope unless required to complete wiring:

- Creating new finance workflow business logic if equivalent APIs already exist
- Redesigning shared layout/navigation beyond adding the needed route entry point
- Broad refactors of unrelated finance modules
- Mobile implementation
- New backend domain model unless the existing API surface is missing a minimal query/detail contract

If existing APIs are missing a small adapter/query endpoint needed by the UI, implement the thinnest backend addition necessary and keep it aligned with CQRS-lite and tenant-scoped authorization.

# Files to touch
Inspect the solution first, then update the most relevant files in these areas.

Likely web/UI files:
- `src/VirtualCompany.Web/**`
- Finance pages/components under existing feature folders, or create a focused folder such as:
  - `src/VirtualCompany.Web/Pages/Finance/Review/*`
  - or the project’s existing equivalent routing/component structure
- Shared table/filter/status components if already present
- Navigation/menu files if the route must be discoverable

Likely API/application files if UI contracts are missing:
- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`
- `src/VirtualCompany.Shared/**`

Likely test files:
- `tests/**`
- Existing web/component test project if present
- If there is no dedicated web test project, use the established test location/pattern already in the repo

Also inspect:
- `README.md`
- `src/VirtualCompany.Web/VirtualCompany.Web.csproj`
- `src/VirtualCompany.Api/VirtualCompany.Api.csproj`
- `src/VirtualCompany.Application/VirtualCompany.Application.csproj`
- `src/VirtualCompany.Shared/VirtualCompany.Shared.csproj`

Before coding, identify:
- Existing finance workflow API clients/contracts
- Existing permission/authorization helpers
- Existing query-string binding patterns in Blazor pages
- Existing loading/empty/error state components
- Existing component/UI test conventions

# Implementation plan
1. **Discover existing finance workflow surface**
   - Search for invoice, finance review, recommendation, approval, workflow, and finance API client code.
   - Identify:
     - list endpoint for reviewable invoices
     - detail endpoint for a single invoice review result
     - action endpoints/commands for approve, reject, follow-up
     - DTOs already available in shared contracts
   - Do not duplicate contracts if reusable ones already exist.

2. **Define/confirm UI contracts**
   - Ensure the web layer has a stable model for:
     - list item row
     - list filters
     - detail view
     - action availability / permission flags
   - If backend already returns permission/actionability metadata, use it directly.
   - If not, add minimal fields or a thin view model mapping layer.
   - Keep naming explicit and finance-domain aligned.

3. **Add finance review list route**
   - Create a route such as `/finance/reviews` or match existing route conventions in the app.
   - Ensure it is tenant-aware and requires authenticated access.
   - Add navigation entry if the app exposes finance workflow navigation.
   - Keep route naming consistent with existing finance/approval pages.

4. **Implement list page data loading**
   - On page load, call the existing finance workflow API for reviewable invoices.
   - Support filters:
     - status
     - supplier
     - risk level
     - recommendation outcome
   - Parse initial filter values from the URL query string.
   - When filters change, update the URL query string and reload data.
   - Avoid unnecessary duplicate requests when query state is unchanged.

5. **Render filterable table**
   - Display the required columns exactly from acceptance criteria.
   - Format:
     - amount/currency consistently
     - confidence as percentage or existing app convention
     - timestamp using existing locale/date formatting pattern
   - Make each row selectable/clickable to navigate to the detail page.
   - Preserve current filters in the URL when navigating back if feasible via browser history/query state.

6. **Implement list states**
   - Loading state while fetching
   - Empty state when no invoices match filters
   - Error state when API call fails
   - Reuse shared state components if available
   - Ensure states are accessible and testable with stable selectors/text

7. **Add review result/detail route**
   - Create a route such as `/finance/reviews/{invoiceId}` or match existing identifier conventions.
   - Load review result data from the finance workflow API.
   - Render:
     - recommendation summary
     - recommended action
     - confidence
     - source invoice link
     - related approval link when present
   - Handle missing optional links gracefully.

8. **Implement permission-aware actions**
   - Show approve / reject / send-for-follow-up only when both are true:
     - user has permission
     - invoice is actionable
   - Prefer server-provided booleans/capabilities if available.
   - If permission checks are client-derived, use existing authorization/policy helpers and still rely on backend enforcement.
   - Buttons should be hidden or disabled according to existing UX conventions; acceptance criteria says “expose ... only when,” so prefer not rendering unavailable actions unless the app convention strongly favors disabled controls.

9. **Wire action execution**
   - Connect action buttons to existing API commands.
   - Handle optimistic vs reload behavior according to existing patterns; safest default is execute then refresh detail.
   - Show in-progress state for action submission.
   - Handle success/failure feedback using existing notification/toast/error patterns.
   - After action, ensure the page reflects updated actionable state and recommendation/workflow status.

10. **Implement detail states**
    - Loading state while fetching detail
    - Empty/not-found state if invoice review result is unavailable
    - Error state for API failures
    - Keep behavior distinct for 404 vs generic API error if existing infrastructure supports it

11. **Backend thin additions only if necessary**
    - If the existing finance workflow APIs do not expose exactly what the UI needs, add minimal query endpoints or DTO fields in:
      - API
      - Application
      - Shared contracts
    - Keep them tenant-scoped, authorization-protected, and aligned with modular monolith boundaries.
    - Do not bypass application layer from controllers.

12. **Testing**
    - Add component tests for:
      - list loading state
      - list empty state
      - list error state
      - list renders required columns/values
      - filters initialize from query string
      - filter changes update query string
      - row selection navigates to detail route
      - detail loading state
      - detail empty/not-found state
      - detail error state
      - detail renders recommendation summary/action/confidence/links
      - action buttons visible only when permitted and actionable
    - If action button click tests are practical in the existing test setup, add at least one success-path interaction test.

13. **Quality constraints**
    - Follow existing naming, folder structure, and dependency boundaries.
    - Keep UI logic thin; move API/data mapping into services where that is the project convention.
    - Avoid introducing dead code or speculative abstractions.
    - Ensure null/optional fields are handled safely.
    - Preserve tenant isolation and authorization assumptions throughout.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Navigate to the new finance review list route
   - Confirm invoices with reviewable statuses load from the existing finance workflow API
   - Confirm table shows:
     - invoice number
     - supplier name
     - amount
     - currency
     - risk level
     - recommendation status
     - confidence
     - last updated timestamp
   - Apply each filter and confirm:
     - results update
     - URL query string updates
     - page reload/deep link restores filter state
   - Select an invoice row and confirm navigation to detail page
   - Confirm detail page shows:
     - recommendation summary
     - recommended action
     - confidence
     - source invoice link
     - related approval link when present
   - Verify action buttons:
     - appear only for permitted users in actionable states
     - do not appear otherwise
   - Verify loading, empty, and error states on both list and detail views

4. If backend additions were required:
   - Verify API endpoints are tenant-scoped and authorized
   - Verify UI uses shared contracts cleanly
   - Verify no direct infrastructure/data access leaks into web layer

5. Include in your final implementation notes:
   - routes added
   - files changed
   - whether existing APIs were reused or minimally extended
   - any assumptions made about permission/actionability metadata

# Risks and follow-ups
- **Existing API mismatch:** The current finance workflow APIs may not expose all fields needed by the UI. If so, add only minimal DTO/query extensions and document them.
- **Permission ambiguity:** If permission logic is split between backend and frontend, prefer backend-provided action capability flags and keep backend enforcement authoritative.
- **Query-string sync complexity:** Blazor query parameter synchronization can cause duplicate loads or navigation loops; implement carefully and test initialization vs user-driven updates.
- **Unknown test setup:** The repo may not yet have a dedicated component test project for Blazor. Reuse the established test pattern rather than inventing a new one unless absolutely necessary.
- **Navigation conventions:** Route and menu placement should match existing finance/approval UX patterns; inspect before choosing final paths.
- **Action side effects:** Approve/reject/follow-up may alter workflow/approval state; refresh detail after action to avoid stale UI.
- **Follow-up work:** Pagination, sorting, richer drilldown, audit timeline, and approval history may be natural next tasks but are not required here.