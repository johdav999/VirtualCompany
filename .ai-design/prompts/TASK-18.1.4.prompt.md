# Goal
Implement automated test coverage for `TASK-18.1.4` by adding component and integration tests for the finance invoice review workbench UI, specifically covering:

- invoice review list route availability
- invoice row field rendering
- filter behavior and URL query string synchronization
- invoice detail/review result page rendering
- permission- and state-gated action visibility
- loading, empty, and API error states for both list and detail views

The work must validate the behavior described in `US-18.1 ST-FUI-201 — Invoice review workbench and recommendation drilldown` without introducing unrelated feature changes unless required to make the tests reliable and maintainable.

# Scope
In scope:

- Add or update Blazor component tests for:
  - finance review list page/component
  - finance review detail page/component
  - loading, empty, and error states
  - conditional action rendering based on permissions and invoice actionable state
- Add integration-style tests for:
  - finance review route availability
  - data loading from existing finance workflow APIs via mocked/fake HTTP responses or existing test server mechanisms
  - filter/query string round-tripping
  - navigation from list row selection to detail page
- Add any minimal test helpers, fixtures, fake API handlers, or builders needed to support the above
- Make small production-code adjustments only if necessary to improve testability, determinism, accessibility selectors, or state exposure

Out of scope:

- Building new finance workflow APIs
- Redesigning invoice review UI
- Changing domain behavior beyond what is required for testability
- Adding mobile coverage
- Broad refactors outside the invoice review feature area

# Files to touch
Inspect the existing implementation first, then update only the relevant files. Likely areas include:

- `src/VirtualCompany.Web/**`
  - finance review list page/component
  - finance review detail page/component
  - routing/navigation setup
  - filter/query string handling
  - permission-based action rendering
  - any view models or API client wrappers used by the pages
- `tests/**`
  - existing web/component test project if present
  - existing API/integration/UI test project if present
  - shared test utilities/builders/fixtures
- Solution/project files only if a missing test project/package/reference is required

Before editing, locate the concrete files for:
- the finance review route/page
- the detail page
- the finance API client/service used by the UI
- the current web test project and test framework setup

Prefer extending existing test projects and conventions over creating new ones.

# Implementation plan
1. **Discover current implementation and test patterns**
   - Search the web project for finance review/invoice review pages, components, routes, and API client usage.
   - Search the tests directory for existing Blazor component tests, bUnit usage, Playwright/TestServer/WebApplicationFactory patterns, and fake HTTP handlers.
   - Identify the exact route paths and component names already used by the feature.

2. **Map acceptance criteria to explicit test cases**
   Create a test matrix that covers at minimum:

   - **List route**
     - finance review route renders successfully
     - page requests reviewable invoices from existing finance workflow API abstraction
   - **List row rendering**
     - each row shows:
       - invoice number
       - supplier name
       - amount
       - currency
       - risk level
       - recommendation status
       - confidence
       - last updated timestamp
   - **Filters**
     - status filter updates results and query string
     - supplier filter updates results and query string
     - risk level filter updates results and query string
     - recommendation outcome filter updates results and query string
     - initial query string hydrates filter state on page load
   - **Navigation**
     - selecting an invoice navigates to dedicated review result page
   - **Detail page**
     - renders recommendation summary
     - renders recommended action
     - renders confidence
     - renders source invoice link
     - renders related approval link when present
     - hides related approval link when absent
   - **Action visibility**
     - approve/reject/send-for-follow-up actions visible only when:
       - current user has permission
       - invoice is actionable
     - actions hidden/disabled when user lacks permission
     - actions hidden/disabled when invoice is not actionable
   - **States**
     - list loading state
     - list empty state
     - list API error state
     - detail loading state
     - detail empty/not-found state if applicable
     - detail API error state

3. **Add/strengthen test fixtures**
   - Create reusable invoice review DTO builders/factories for:
     - reviewable invoice list items
     - invoice detail/review result payloads
     - permission variants
     - actionable vs non-actionable states
     - related approval present/absent
   - Add fake HTTP/message handler or existing API stub wiring for deterministic responses.
   - Add helpers for query string assertions and navigation assertions.

4. **Implement component tests for list page/component**
   - Render the list component/page with mocked service/API responses.
   - Assert loading indicator before completion if the component supports async loading.
   - Assert empty state when API returns no invoices.
   - Assert error state when API fails.
   - Assert row content for all required fields.
   - Assert filter controls render and update component state.
   - If query string sync is implemented at component/page level, assert URL updates via `NavigationManager`.

5. **Implement component tests for detail page/component**
   - Render detail page/component with mocked detail response.
   - Assert recommendation summary, recommended action, confidence, source invoice link, and related approval link behavior.
   - Assert loading and error states.
   - Assert conditional rendering of approve/reject/send-for-follow-up actions based on:
     - permission set
     - actionable state

6. **Implement integration tests for route + navigation + query string behavior**
   Use the project’s existing preferred integration approach:
   - If there is a Blazor-friendly integration harness, use it.
   - If there is a server-backed integration test setup, use `WebApplicationFactory` or equivalent.
   - If only component-level navigation testing exists, keep it there but ensure route-level behavior is still validated.

   Cover:
   - navigating to finance review route loads page
   - query string parameters initialize filters
   - changing filters updates query string
   - selecting a row navigates to detail route
   - detail route loads expected invoice review result

7. **Make minimal production changes only if needed**
   If tests are hard to write because the UI lacks stable hooks:
   - add semantic labels, `data-testid`, or accessible names sparingly
   - extract filter state mapping/query parsing into testable methods/classes
   - avoid changing user-visible behavior unless fixing a bug exposed by tests

8. **Keep tests maintainable**
   - Follow Arrange/Act/Assert structure.
   - Prefer descriptive test names tied to acceptance criteria.
   - Reuse builders and fixtures to avoid duplication.
   - Avoid brittle markup assertions when semantic assertions are possible.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run the relevant test project(s) during development:
   - `dotnet test`

3. Ensure the new tests specifically cover:
   - list route rendering
   - row field rendering
   - filter/query string synchronization
   - detail page rendering
   - permission/actionable-state action visibility
   - loading/empty/error states for list and detail

4. If production code was adjusted for testability, verify no regressions by running the full test suite:
   - `dotnet test`

5. In the final summary, include:
   - which files were changed
   - which acceptance criteria are covered by which tests
   - any gaps caused by missing existing infrastructure or ambiguous implementation details

# Risks and follow-ups
- The repository may not yet contain a dedicated Blazor component test project or bUnit setup. If missing, add the smallest viable test infrastructure and note it clearly.
- The exact finance review route and API client names may differ from the backlog wording; align tests to the implemented feature, not invented names.
- Query string synchronization may currently be partially implemented or coupled to UI events in a way that is hard to test; extract minimal logic if necessary.
- Permission evaluation may be embedded in page logic or authorization wrappers; ensure tests validate rendered behavior rather than internal implementation details.
- Loading/empty/error states may not currently be distinguishable in markup; add stable selectors or accessible text only where necessary.
- If integration coverage is limited by current test infrastructure, prioritize high-confidence component tests plus the strongest route/navigation integration tests feasible, and document any remaining gap as a follow-up.