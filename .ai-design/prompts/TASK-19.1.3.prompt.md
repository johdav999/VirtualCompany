# Goal
Implement backlog task **TASK-19.1.3 — Build admin page layout with section containers and async state handling** for **US-19.1 ST-FUI-301 — Finance sandbox administration page**.

Deliver a finance sandbox admin page in the Blazor web app that:
- is reachable from finance area navigation via a dedicated route
- renders section containers for:
  - dataset generation
  - anomaly injection
  - simulation controls
  - tool execution visibility
  - domain events
- enforces role-based access so only authorized finance sandbox admin/tester users can access it
- prevents unauthorized users from loading admin data
- loads each section independently with its own async lifecycle and visible loading, empty, and error states

This task is focused on **page composition, routing, authorization gating, and per-section async UI state handling**, not on implementing the full business workflows behind each section.

# Scope
In scope:
- Add a dedicated finance sandbox admin page route in the web app
- Add finance navigation entry to reach the page
- Build the page shell/layout and section containers
- Implement access denied UX for unauthorized users
- Ensure admin data is not requested when the user lacks the required role
- Implement independent async loading for each section
- Show loading, empty, success, and error states per section
- Wire the page to existing or placeholder query/service contracts as needed
- Add tests for authorization behavior, route rendering, and section state handling where practical

Out of scope:
- Full CRUD/actions for dataset generation, anomaly injection, simulation execution, tool execution replay, or domain event publishing
- Backend domain logic beyond minimal query endpoints/contracts needed to support the page
- Deep styling/polish beyond existing design system/layout conventions
- New identity model design; use existing role/membership patterns
- Mobile app changes

# Files to touch
Inspect the solution first and adjust to actual structure/naming, but expect to touch files in these areas:

- `src/VirtualCompany.Web/**`
  - finance area navigation/menu component
  - finance sandbox admin page/component
  - shared async state UI components if they already exist
  - auth/role-aware rendering helpers if present
  - page-specific view models or DTOs
- `src/VirtualCompany.Shared/**`
  - shared DTOs/contracts for finance sandbox admin section data, if shared between web and API
- `src/VirtualCompany.Api/**`
  - minimal finance sandbox admin endpoints if the page currently has no backing API
  - authorization policy wiring if API-level protection is needed
- `src/VirtualCompany.Application/**`
  - query handlers/services for section data retrieval
- `src/VirtualCompany.Domain/**`
  - only if role constants/policies/domain enums belong here
- `tests/VirtualCompany.Api.Tests/**`
  - API authorization and endpoint tests if endpoints are added/changed
- Add web/UI tests if a web test project already exists; if not, add only if consistent with repo patterns

Before editing, identify:
- where finance navigation is defined
- how Blazor routing is organized
- how authorization is currently enforced in the web app
- whether there is an existing async state component/pattern
- whether finance sandbox APIs/contracts already exist

# Implementation plan
1. **Inspect existing patterns**
   - Find the finance area pages and navigation structure in `VirtualCompany.Web`
   - Find existing role/authorization patterns:
     - `[Authorize]`
     - policy-based checks
     - role constants
     - membership/tenant context resolution
   - Find existing async section loading patterns:
     - loading spinners/skeletons
     - empty state components
     - error panels
   - Find any existing finance sandbox-related contracts/endpoints

2. **Define the route and navigation entry**
   - Add a dedicated route for the finance sandbox admin page under the finance area, following existing route conventions
   - Add a navigation item in the finance area menu/sidebar
   - Ensure visibility of the nav item follows product expectations:
     - if current app pattern hides restricted nav items, do that
     - otherwise allow navigation but enforce access denied on page load
   - Keep naming aligned with story/task terminology: “Finance Sandbox Admin” or existing finance naming conventions

3. **Implement authorization gating**
   - Use existing policy-based authorization if available; prefer a named policy over ad hoc role checks
   - Authorized roles should map to the app’s existing membership/role model and include the intended admin/tester access
   - On page initialization:
     - determine whether the current user is authorized before requesting section data
     - if unauthorized, render an access denied state and do not invoke section data loaders
   - If the app already uses API authorization too, enforce the same policy server-side for any backing endpoints

4. **Build the page shell**
   - Create the finance sandbox admin page component with:
     - page title/header
     - short descriptive text if consistent with app patterns
     - five section containers:
       - dataset generation
       - anomaly injection
       - simulation controls
       - tool execution visibility
       - domain events
   - Use existing card/section/layout components where possible
   - Keep each section isolated so it can manage its own async state independently

5. **Implement per-section async state model**
   - For each section, create a small view model/state object that can represent:
     - not started
     - loading
     - success with data
     - empty
     - error with message
   - Do not use one page-level loading flag for all sections
   - Each section should load independently so one failure does not block the others
   - Prefer a reusable generic async state wrapper if the codebase already has one; otherwise add a lightweight local pattern consistent with the repo

6. **Wire section data loading**
   - For each section, call its own query/service method asynchronously
   - Start loads independently, ideally concurrently after authorization succeeds
   - Handle each result separately:
     - null/no records/config absent => empty state
     - exception/non-success => error state
     - valid payload => render section content
   - If backend support is incomplete, add minimal placeholder query contracts/endpoints returning shape-compatible data so the UI can be integrated now without inventing full business logic

7. **Render section states**
   - Each section container must visibly render:
     - loading state
     - empty state
     - error state
     - populated state
   - Keep the UX concise and operational
   - Use existing shared components for alerts/placeholders/spinners if available
   - Ensure error text is safe and user-facing, not raw exception dumps

8. **Protect admin data loading**
   - Verify unauthorized users cannot trigger admin data fetches from the page
   - If using child components per section, pass authorization state down or gate rendering so child components do not self-load when unauthorized
   - If API endpoints are added/used, ensure they also reject unauthorized access

9. **Add tests**
   - Add or update tests to cover:
     - finance navigation includes the admin route entry
     - authorized user can access the page
     - unauthorized user sees access denied
     - unauthorized user does not trigger admin data loading
     - each section can render loading/empty/error/success states
   - Prefer existing test styles/frameworks already used in the repo
   - If UI component tests are not established, cover the critical behavior at API/service/unit level and keep UI tests minimal but meaningful

10. **Keep implementation incremental and clean**
   - Avoid overengineering a generic dashboard framework unless the repo already has one
   - Keep contracts and state models narrowly scoped to this page
   - Add TODO comments only where a later task will replace placeholders with real admin actions/data

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manual validation in the web app:
   - Navigate to the finance area
   - Confirm a finance sandbox admin navigation entry exists
   - Open the finance sandbox admin route
   - Confirm the page renders all five sections:
     - dataset generation
     - anomaly injection
     - simulation controls
     - tool execution visibility
     - domain events

4. Authorization validation:
   - Sign in as an authorized admin/tester user
   - Confirm the page loads and section requests execute
   - Sign in as an unauthorized user
   - Confirm access denied is shown
   - Confirm no admin section data requests are made for unauthorized access

5. Async state validation:
   - For each section, verify:
     - loading state appears while fetching
     - empty state appears when no data is returned
     - error state appears when the request fails
     - success state appears when data is available
   - Confirm one section failing does not prevent the others from loading/rendering

6. Navigation and route validation:
   - Confirm the route is reachable directly by URL
   - Confirm finance navigation links correctly to the route
   - Confirm unauthorized deep-link access still shows access denied and does not load data

# Risks and follow-ups
- **Role ambiguity risk:** The backlog says “authorized admin/tester role,” but the current membership model may not yet have a dedicated tester role. Reuse existing role constants/policies where possible and document any temporary mapping.
- **Missing backend contracts risk:** Some or all section data sources may not exist yet. If so, add minimal query contracts/placeholders without inventing full domain behavior.
- **UI test coverage risk:** If the repo lacks Blazor component testing infrastructure, avoid introducing a large new test stack just for this task; cover critical logic with existing test patterns.
- **Navigation consistency risk:** Finance navigation may be centralized or generated dynamically. Follow the existing pattern rather than hardcoding a one-off menu item.
- **Authorization duplication risk:** Keep web and API authorization aligned to avoid a page that hides data client-side but exposes it server-side.
- **Follow-up likely needed:** Subsequent tasks should implement real section interactions and richer data models for:
  - dataset generation controls
  - anomaly injection actions
  - simulation execution/status
  - tool execution inspection details
  - domain event stream/history