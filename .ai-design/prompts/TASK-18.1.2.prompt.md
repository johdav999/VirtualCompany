# Goal
Implement backlog task **TASK-18.1.2 — Build invoice recommendation detail page with approval deep links and action state handling** for **US-18.1 ST-FUI-201 — Invoice review workbench and recommendation drilldown**.

Deliver a finance review experience in the **Blazor Web App** that:
- exposes a finance review route listing invoices in reviewable statuses from existing finance workflow APIs,
- supports URL-synced filtering,
- opens a dedicated invoice recommendation detail page,
- shows recommendation and related navigation links,
- conditionally exposes approve/reject/send-for-follow-up actions based on permission and actionable state,
- renders loading, empty, and error states for list and detail views,
- includes component tests covering those states and action visibility behavior.

# Scope
In scope:
- Web UI only in `src/VirtualCompany.Web`
- Reuse existing finance workflow APIs and DTOs if already present
- Add or extend typed web-side API client/service wrappers as needed
- Add finance review list page and invoice recommendation detail page
- Add URL query-string filter synchronization
- Add conditional action state handling in the detail page
- Add deep links to source invoice and related approval when present
- Add component tests for list/detail loading, empty, error, and action visibility states

Out of scope unless required by existing contracts:
- Backend domain changes
- New persistence or schema changes
- New approval workflow rules
- Mobile app changes
- Broad redesign of finance APIs
- Full end-to-end browser automation

Assumptions to verify before coding:
- Existing finance workflow APIs already return reviewable invoices and invoice recommendation detail data, or can be composed from existing endpoints without backend changes
- Existing auth/authorization context in the web app exposes current-user permissions or enough data to infer action availability
- Existing routes/pages for source invoice and approval detail already exist or there is an agreed placeholder route pattern to link to

# Files to touch
Inspect first, then update only the minimum necessary set. Likely areas:

- `src/VirtualCompany.Web/...`
  - finance review list page/component
  - invoice recommendation detail page/component
  - shared finance models/view models
  - typed API client/service for finance workflow endpoints
  - query-string helper utilities if already present
  - authorization/permission helper usage in UI
  - navigation route registration if applicable
  - reusable loading/empty/error state components if already present

- `src/VirtualCompany.Shared/...`
  - shared DTOs only if the web app consumes shared contracts and a missing UI-facing contract must be added without changing backend behavior

- `tests/...`
  - existing web/component test project if present
  - otherwise the most appropriate test project already covering Blazor/UI components

Before editing, locate:
- existing finance workflow pages/components
- existing approval inbox/detail routes
- existing invoice/source document routes
- existing query-string filter patterns
- existing component test conventions and libraries
- existing permission-check helpers and action gating patterns

# Implementation plan
1. **Discover existing contracts and patterns**
   - Search the solution for:
     - finance workflow API clients/endpoints
     - invoice review/recommendation DTOs
     - approval detail routes
     - invoice detail/source routes
     - query-string bound filters
     - loading/empty/error UI patterns
     - component tests for similar list/detail pages
   - Do not invent parallel patterns if established ones already exist.

2. **Define the finance review route**
   - Add a route for the finance review workbench list page in the web app.
   - Ensure the page loads invoices with reviewable statuses from the existing finance workflow API.
   - Keep the page tenant/user scoped through existing authenticated API client behavior.

3. **Build the invoice review list UI**
   - Render a table/list with the required columns:
     - invoice number
     - supplier name
     - amount
     - currency
     - risk level
     - recommendation status
     - confidence
     - last updated timestamp
   - Make each row selectable/navigable to the dedicated review result page.
   - Prefer existing table and formatting components/helpers for currency, timestamps, badges, and status chips.

4. **Implement filter model and URL synchronization**
   - Add filters for:
     - status
     - supplier
     - risk level
     - recommendation outcome
   - Initialize filter state from the URL query string on page load.
   - Update the query string when filters change.
   - Preserve deep-linkability and browser back/forward behavior.
   - Avoid infinite navigation loops by comparing current and next query state before navigating.
   - Keep query parameter names stable and readable.

5. **Handle list loading, empty, and error states**
   - Show loading UI while fetching.
   - Show an empty state when no invoices match.
   - Show an API error state with a retry action when the request fails.
   - Ensure these states are mutually exclusive and testable.

6. **Build the invoice recommendation detail page**
   - Add a dedicated route using the invoice identifier from the list.
   - Fetch recommendation detail from the existing API client/service.
   - Render:
     - recommendation summary
     - recommended action
     - confidence
     - link to source invoice when present
     - link to related approval when present
   - If source invoice or approval is absent, omit the link cleanly rather than rendering broken placeholders.

7. **Implement action state handling**
   - Determine whether approve, reject, and send-for-follow-up actions should be shown/enabled based on:
     - current user permission
     - invoice actionable state
   - Reuse existing permission helpers and server-returned state flags if available.
   - Prefer a view-model method/property such as:
     - `CanApprove`
     - `CanReject`
     - `CanSendForFollowUp`
     - `IsActionable`
   - If the API already returns allowed actions, trust server-driven action availability over duplicating business rules in the UI.
   - If both permission and state are needed, combine them conservatively:
     - hide or disable actions when permission is missing
     - hide or disable actions when invoice is not actionable
   - Match the acceptance criteria exactly: actions are exposed only when both conditions are satisfied.

8. **Wire action triggers**
   - If existing endpoints/actions already exist, connect buttons to them.
   - Handle in-flight action state to prevent duplicate submissions.
   - Refresh detail state after action completion so the UI reflects the new actionable/non-actionable state.
   - If action execution is not part of this task’s intended scope, still implement correct visibility/state handling and use existing navigation/deep links where appropriate. Prefer not to stub fake actions unless the codebase already uses placeholders.

9. **Handle detail loading, empty/not-found, and error states**
   - Show loading while fetching detail.
   - Show a not-found or empty state if the invoice detail is unavailable.
   - Show an API error state with retry on failure.
   - Keep state rendering consistent with the list page.

10. **Add component tests**
    - Cover list page/component:
      - loading state
      - empty state
      - API error state
      - rows render required fields
      - filters initialize from query string
      - filter changes update query string
    - Cover detail page/component:
      - loading state
      - empty/not-found state
      - API error state
      - recommendation summary/action/confidence render
      - source invoice link shown only when present
      - related approval link shown only when present
      - approve/reject/send-for-follow-up actions shown only when user has permission and invoice is actionable
      - actions hidden/disabled when permission or actionable state is missing
    - Follow existing test style and libraries already used in the repo.

11. **Keep implementation aligned with architecture**
    - Respect clean boundaries:
      - UI in web project
      - typed API access through existing application/service abstractions where applicable
      - no direct DB access from UI
    - Keep authorization checks policy-driven or server-driven where possible.
    - Avoid embedding domain rules in Razor markup beyond presentation gating.

12. **Document any gaps**
    - If required API fields are missing, note them clearly in code comments/TODOs and in the final task summary.
    - If deep-link target routes do not exist, use the nearest existing route and document the follow-up.

# Validation steps
1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify in the web app:
   - Navigate to the new finance review route
   - Confirm invoices with reviewable statuses load
   - Confirm each row shows all required fields
   - Apply each filter and confirm the URL query string updates
   - Refresh the page and confirm filters restore from the URL
   - Select an invoice and confirm navigation to the detail page
   - Confirm recommendation summary, recommended action, confidence, and deep links render correctly
   - Confirm source invoice link is absent when not provided
   - Confirm related approval link is absent when not provided
   - Confirm approve/reject/send-for-follow-up actions appear only for permitted users and actionable invoices
   - Confirm loading, empty, and error states render correctly on both list and detail views

4. If component tests use bUnit or similar, ensure they assert:
   - rendered state transitions
   - conditional action visibility
   - query-string synchronization behavior

# Risks and follow-ups
- **API contract mismatch:** Existing finance workflow APIs may not expose all fields required by the acceptance criteria, especially confidence, recommendation outcome, related approval ID, or actionable flags.
- **Permission ambiguity:** If the UI lacks a reliable permission source, action visibility may need to be server-driven from the API response rather than inferred client-side.
- **Route dependency risk:** Deep links depend on existing source invoice and approval detail routes; missing routes may require a follow-up task.
- **Query-string complexity:** Blazor navigation/query synchronization can cause loops or stale state if not implemented carefully.
- **Action semantics:** If approve/reject/follow-up endpoints are not already available, this task may need to stop at correct action exposure/state handling and create a backend follow-up.

Potential follow-ups to note if discovered:
- add/extend finance workflow API fields for UI completeness,
- standardize server-driven `allowedActions` on review detail responses,
- add shared badge/formatting components for finance statuses and risk levels,
- add end-to-end tests for finance review drilldown and approval deep links.